using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using WebGallery.Data;
using WebGallery.Models;
using WebGallery.Services;

void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine(message); }
var temp = Directory.CreateTempSubdirectory("gallery-date-test-").FullName;
try {
    var media = Directory.CreateDirectory(Path.Combine(temp,"media")).FullName;
    void Save(string name,string? date) {
        using var image = new Image<Rgb24>(30,20);
        if(date is not null) { image.Metadata.ExifProfile = new ExifProfile(); image.Metadata.ExifProfile.SetValue(ExifTag.DateTimeOriginal,date); }
        image.SaveAsJpeg(Path.Combine(media,name));
    }
    Save("older.jpg","2020:01:02 03:04:05"); Save("newer.jpg","2024:01:02 03:04:05"); Save("unknown.jpg",null);
    File.SetLastWriteTimeUtc(Path.Combine(media,"older.jpg"),DateTime.UtcNow.AddHours(1));
    var services = new ServiceCollection().AddLogging();
    services.AddDbContext<GalleryDbContext>(o=>o.UseSqlite($"Data Source={Path.Combine(temp,"test.db")}"));
    services.AddSingleton<IWebHostEnvironment>(new Env(temp));
    services.AddSingleton<IOptions<GalleryOptions>>(Options.Create(new GalleryOptions { CachePath=Path.Combine(temp,"cache") }));
    services.AddSingleton<GalleryIndexService>(); services.AddSingleton<DateTakenIndexer>(); services.AddScoped<FileSystemService>();
    await using var provider=services.BuildServiceProvider();
    using var scope=provider.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<GalleryDbContext>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE GalleryIndexEntries DROP COLUMN DateTakenTicks");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE GalleryIndexEntries DROP COLUMN DateTakenScanAfter");
    await GalleryIndexService.EnsureSchemaAsync(db); await GalleryIndexService.EnsureSchemaAsync(db);
    var owner=new ApplicationUser{Id="owner",UserName="owner"}; db.Users.Add(owner);
    var root=new UserRoot{OwnerUserId=owner.Id,Name="Media",PhysicalPath=media};db.Add(root);await db.SaveChangesAsync();
    var logical=FileSystemService.RootMarker(root.Id);
    var index=provider.GetRequiredService<GalleryIndexService>(); index.Refresh(root.Id,"",true);
    var worker=provider.GetRequiredService<DateTakenIndexer>();
    Check(worker.Workers==4,"Default metadata concurrency is four");worker.SetWorkers(2);Check(worker.Workers==2,"Metadata worker limit can change at runtime");worker.SetWorkers(4);
    await worker.StartAsync(default);
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(40));
    while(await db.GalleryIndexEntries.AnyAsync(x=>x.DateTakenScanAfter==0,timeout.Token)) await Task.Delay(100,timeout.Token);
    var files=scope.ServiceProvider.GetRequiredService<FileSystemService>();
    Check(files.List(owner,logical,"taken","asc").Select(x=>x.Name).SequenceEqual(new[]{"older.jpg","newer.jpg","unknown.jpg"}),"Taken ascending; unknown last");
    Check(files.List(owner,logical,"taken","desc").Select(x=>x.Name).SequenceEqual(new[]{"newer.jpg","older.jpg","unknown.jpg"}),"Taken descending; unknown last");
    Check(files.List(owner,logical,"date","desc")[0].Name=="older.jpg","Modified remains independent");
    Check(files.GetFileItem(owner,logical+"/older.jpg")?.DateTaken?.Year==2020,"Selected-file share reads indexed date");
    await worker.StopAsync(default);
    var indexingProgress=await index.IndexProgressAsync(default);
    Check(indexingProgress.Total==3 && indexingProgress.Completed==3 && indexingProgress.WithDate==2 && indexingProgress.WithoutDate==1 && indexingProgress.Pending==0 && indexingProgress.Retry==0,"Indexing progress distinguishes completed dates and missing EXIF");
    await db.GalleryIndexEntries.ExecuteUpdateAsync(x=>x.SetProperty(e=>e.ThumbnailSignature,"keep-small").SetProperty(e=>e.MediumThumbnailSignature,"keep-medium"));
    index.Rebuild();
    Check(await db.GalleryIndexEntries.CountAsync(x=>x.DateTakenScanAfter==0)==3,"Re-index queues every image including missing EXIF");
    Check(await db.GalleryIndexEntries.CountAsync(x=>x.DateTakenTicks!=null)==2,"Known dates remain available during re-index");
    Check(await db.GalleryIndexEntries.AllAsync(x=>x.ThumbnailSignature=="keep-small" && x.MediumThumbnailSignature=="keep-medium"),"Re-index preserves thumbnail readiness");
    Check(await db.GalleryIndexFolders.AllAsync(x=>x.NextScan==0),"Re-index queues all indexed folders");
    index.Rebuild();
    Check(await db.GalleryIndexEntries.CountAsync()==3,"Repeated re-index does not duplicate or clear entries");
    Save("older.jpg","2025:02:03 04:05:06");index.Refresh(root.Id,"",true);
    Check(await db.GalleryIndexEntries.AsNoTracking().AnyAsync(x=>x.Name=="older.jpg" && x.DateTakenTicks==null && x.DateTakenScanAfter==0),"Source change invalidates metadata");
    Check((await DateTakenIndexer.ReadAsync(Path.Combine(media,"older.jpg"),"exiftool",default))?.Year==2025,"Header extraction reads changed date");
    Check(DateTakenIndexer.Parse("0000:00:00 00:00:00")==null,"Invalid date is unknown");
    var many=Enumerable.Range(0,1001).Select(i=>new WebGallery.ViewModels.GalleryItemViewModel($"image-{i:D4}.jpg",$"image-{i:D4}.jpg",false,true,false,1,DateTimeOffset.UtcNow,"JPG",[],new DateTime(2024,1,1).AddSeconds(i))).ToArray();
    Check(FileSystemService.SortTaken(many,false).Skip(500).First().Name=="image-0500.jpg" && FileSystemService.SortTaken(many,true).First().Name=="image-1000.jpg","1001 images sorted globally before paging");
    var tied=many.Select(x=>x with { DateTaken=new DateTime(2024,1,1) }).ToArray();
    Check(FileSystemService.SortTaken(tied,true).First().Name=="image-1000.jpg" && FileSystemService.SortTaken(tied,false).First().Name=="image-0000.jpg","Equal dates follow selected filename direction");
    var unknown=many.Select((x,i)=>x with { DateTaken=null, ModifiedUtc=new DateTimeOffset(2030,1,1,0,0,0,TimeSpan.Zero).AddSeconds(-i) }).ToArray();
    Check(FileSystemService.SortTaken(unknown,true).First().Name=="image-0000.jpg" && FileSystemService.SortTaken(unknown,false).First().Name=="image-1000.jpg","Missing dates follow modified time rather than filename across pages");
    Check(FileSystemService.SortTaken(new[]{many[0],unknown[1000]},true).First().Name=="image-0000.jpg","Missing dates remain after known dates in descending order");
    var mixed=new[]{many[20],unknown[900],many[10],unknown[800]};
    Check(FileSystemService.SortTaken(mixed,false).Select(x=>x.Name).SequenceEqual(new[]{"image-0010.jpg","image-0020.jpg","image-0900.jpg","image-0800.jpg"}),"Ascending: capture dates first, then modified dates");
    Check(FileSystemService.SortTaken(mixed,true).Select(x=>x.Name).SequenceEqual(new[]{"image-0020.jpg","image-0010.jpg","image-0800.jpg","image-0900.jpg"}),"Descending: capture dates first even when undated files have newer modification times");
    var bulk=Directory.CreateDirectory(Path.Combine(media,"bulk")).FullName;
    for(var i=0;i<96;i++)File.Copy(Path.Combine(media,"older.jpg"),Path.Combine(bulk,$"{i:D3}.jpg"));
    File.Copy(Path.Combine(media,"older.jpg"),Path.Combine(bulk,"vanished.jpg"));
    index.Refresh(root.Id,"bulk",true);File.Delete(Path.Combine(bulk,"vanished.jpg"));
    using var pipeline=ActivatorUtilities.CreateInstance<DateTakenIndexer>(provider);
    pipeline.SetWorkers(4);pipeline.RequestFolder(root.Id,GalleryIndexService.Key("bulk"));await pipeline.StartAsync(default);
    using var bulkTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(35));
    while(await db.GalleryIndexEntries.AnyAsync(x=>x.DateTakenScanAfter==0,bulkTimeout.Token))await Task.Delay(50,bulkTimeout.Token);
    await pipeline.StopAsync(default);
    Check(await db.GalleryIndexEntries.CountAsync(x=>x.ParentKey==GalleryIndexService.Key("bulk") && x.DateTakenScanAfter==long.MaxValue && x.DateTakenTicks!=null)==96,"Continuous multi-worker pipeline flushes more than two result batches");
    Check(await db.GalleryIndexEntries.AnyAsync(x=>x.Name=="vanished.jpg" && x.DateTakenScanAfter>0 && x.DateTakenScanAfter<long.MaxValue),"Failed read is deferred while other workers finish");
    if(args.Length==2) Check(await DateTakenIndexer.ReadAsync(args[1],args[0],default) is not null,"Real RAW date extracted without pixel decode");
} finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(temp,true); }
sealed class Env(string root):IWebHostEnvironment {
    public string EnvironmentName{get;set;}="Development"; public string ApplicationName{get;set;}="WebGallery";
    public string ContentRootPath{get;set;}=root; public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
    public string WebRootPath{get;set;}=root; public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
}
