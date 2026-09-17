using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using WebGallery.Models;
using WebGallery.Services;

var ffmpeg=args[0]; var source=args[1];
var temp=Directory.CreateTempSubdirectory("gallery-video-test-").FullName;
void Check(bool value,string name) { if(!value)throw new Exception(name);Console.WriteLine("PASS "+name); }
try {
 var options=Options.Create(new GalleryOptions {CachePath=temp,FfmpegPath=ffmpeg});
 var settings=new ThumbnailQueueSettings(2);settings.SetBackgroundWorkers(1);
 using var queue=new ThumbnailWorkQueue(settings);await queue.StartAsync(default);
 var service=new ThumbnailService(options,new Env(temp),queue,settings);
 var outputs=await Task.WhenAll(Enumerable.Range(0,4).Select(i=>service.GetOrCreateAsync("owner"+i,source,ThumbnailPriority.Visible,default)));
 Check(outputs.Distinct().Count()==1,"Concurrent video requests share one cache file across owners");
 var info=await Image.IdentifyAsync(outputs[0]);Check(info.Width<=480 && info.Height<=360,"Small video WebP within bounds");
 var stamp=File.GetLastWriteTimeUtc(outputs[0]);
 Check(await service.GetOrCreateAsync("other",source,ThumbnailPriority.Background,default)==outputs[0] && File.GetLastWriteTimeUtc(outputs[0])==stamp,"Background cache hit does not rewrite");
 var medium=await service.GetOrCreateAsync("owner",source,ThumbnailPriority.Visible,default,medium:true);
 var large=await Image.IdentifyAsync(medium);Check(large.Width==1500 && Math.Abs((double)large.Width/large.Height-(double)info.Width/info.Height)<.02,"Medium preserves video aspect ratio");
 await using(var shortFrame=await VideoThumbnail.OpenAsync(args[2],ffmpeg,"exiftool",default)) Check((await Image.IdentifyAsync(shortFrame)).Width>0,"Short clip falls back to first frame");
 var invalid=Path.Combine(temp,"broken.mp4");await File.WriteAllTextAsync(invalid,"not video");
 try {await VideoThumbnail.OpenAsync(invalid,ffmpeg,"exiftool",default);throw new Exception("Expected rejection");}catch(VideoThumbnailException){Console.WriteLine("PASS invalid video rejects");}
 using var cancellation=new CancellationTokenSource();cancellation.Cancel();
 try {await VideoThumbnail.OpenAsync(source,ffmpeg,"exiftool",cancellation.Token);throw new Exception("Expected cancellation");}catch(OperationCanceledException){Console.WriteLine("PASS cancellation");}
 await queue.StopAsync(default);
}finally{Directory.Delete(temp,true);}
sealed class Env(string root):IWebHostEnvironment {
 public string EnvironmentName{get;set;}="Development";public string ApplicationName{get;set;}="WebGallery";
 public string ContentRootPath{get;set;}=root;public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
 public string WebRootPath{get;set;}=root;public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
}
