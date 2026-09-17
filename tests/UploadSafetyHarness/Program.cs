using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Hosting;
using WebGallery.Services;
using WebGallery.Models;
using WebGallery.Data;
using System.Text.Json;

var home=Path.Combine(Path.GetTempPath(),"webgallery-upload-safety-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(home);var media=Path.Combine(home,"media");Directory.CreateDirectory(media);
var services=new ServiceCollection().AddDbContext<GalleryDbContext>(o=>o.UseSqlite("Data Source="+Path.Combine(home,"fixture.db"))).BuildServiceProvider();
UserRoot root;
using(var scope=services.CreateScope()) {
    var db=scope.ServiceProvider.GetRequiredService<GalleryDbContext>();db.Database.EnsureCreated();
    db.Users.Add(new ApplicationUser { Id="owner",UserName="owner" });root=new UserRoot{OwnerUserId="owner",Name="Media",PhysicalPath=media};db.UserRoots.Add(root);db.SaveChanges();
}
var sessions=new UploadSessions(Options.Create(new GalleryOptions{CachePath=Path.Combine(home,"cache")}),new Env(home),services.GetRequiredService<IServiceScopeFactory>());
var marker=FileSystemService.RootMarker(root.Id);
foreach(var bad in new[]{"..","a/b","a\\b","CON.txt","CON .txt","NUL","COM1", "LPT².log","foo.","name ","x:y","a%2fb","Thumb.db",".secret"}) {
    try{WritePaths.Name(bad);throw new Exception("Accepted bad name: "+bad);}catch(InvalidOperationException) { }
}
if(WritePaths.Name("user-file.part")!="user-file.part" || FileSystemService.IsIgnoredFileName("user-file.part"))throw new Exception("Ordinary .part ignored");
try{WritePaths.Resolve(root,marker+"/../escape",false);throw new Exception("Traversal accepted");}catch(InvalidOperationException) { }
var upload=sessions.Start("owner",root,marker,"photo.jpg",20);File.WriteAllText(upload.Temp,"partial");upload.Updated=DateTime.UtcNow.AddDays(-2);
var unrelated=Path.Combine(Path.GetDirectoryName(upload.Temp)!,"user-file.part");File.WriteAllText(unrelated,"real user file");File.SetLastWriteTimeUtc(unrelated,DateTime.UtcNow.AddDays(-2));
var id=Guid.NewGuid().ToString("N");var stage=Path.Combine(media,UploadSessions.PublishName(id));File.WriteAllText(stage,"unfinished");
if(!FileSystemService.IsIgnoredFileName(Path.GetFileName(stage)))throw new Exception("Publication stage is visible");
var journal=Path.Combine(Path.GetDirectoryName(upload.Temp)!,"webgallery-upload-"+id+".wg-upload-segment.wg-publish.json");
File.WriteAllText(journal,JsonSerializer.Serialize(new UploadSessions.PublishJournal(id,"owner",root.Id,root.PhysicalPath,marker)));File.SetLastWriteTimeUtc(journal,DateTime.UtcNow.AddDays(-2));
await sessions.CleanupAsync();
if(File.Exists(upload.Temp)||File.Exists(stage)||File.Exists(journal)||!File.Exists(unrelated))throw new Exception("Cleanup ownership check failed");
Console.WriteLine("PASS: portable names, traversal, unique ignored staging, expired upload and restart-journal cleanup, ordinary .part preserved. Isolated fixture retained: "+home);

sealed class Env(string root) : IWebHostEnvironment {
 public string ApplicationName{get;set;}="Test";public string EnvironmentName{get;set;}="Test";
 public string ContentRootPath{get;set;}=root;public string WebRootPath{get;set;}=root;
 public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
}
