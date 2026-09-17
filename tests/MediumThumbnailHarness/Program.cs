using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WebGallery.Models;
using WebGallery.Services;
using System.Net;

var root=Directory.CreateTempSubdirectory("gallery-medium-").FullName;
void Check(bool condition,string message) { if(!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
try {
    var path=Path.Combine(root,"source.jpg");
    using(var image=new Image<Rgb24>(2400,1600,new Rgb24(20,80,130))) await image.SaveAsJpegAsync(path);
    var opts=Options.Create(new GalleryOptions { CachePath=Path.Combine(root,"cache"),ResizerServiceUrl="https://fixture/ThumbService",ResizerServiceBackgroundWorkers=1 });
    using var transport=new ResizerFixture();
    using var remote=new RemoteResizer(opts,transport);
    var limits=new ThumbnailQueueSettings(2);
    limits.SetBackgroundWorkers(1);
    using var queue=new ThumbnailWorkQueue(limits); await queue.StartAsync(default);
    var thumbnails=new ThumbnailService(opts,new Env(root),queue,limits,remote:remote);
    var offlinePath=Path.Combine(root,"offline.jpg");File.Copy(path,offlinePath);
    var offlineMedium=await thumbnails.GetOrCreateAsync("owner",offlinePath,ThumbnailPriority.Visible,default,"contain-v1",true);
    Check(transport.Posts==0 && (await Image.IdentifyAsync(offlineMedium)).Width==1500,"offline on-demand creates local medium");
    Check(Directory.EnumerateFiles(opts.Value.CachePath,"*.webp",SearchOption.AllDirectories).Count()==2,"direct medium ensures small cache first");
    await remote.StartAsync(default);
    using var timeout=new CancellationTokenSource(5000);
    while(!remote.IsAvailable) await Task.Delay(10,timeout.Token);
    var result=await Task.WhenAll(Enumerable.Range(0,8).Select(i=>thumbnails.GetOrCreateAsync("owner-"+i,path,ThumbnailPriority.Visible,default,"contain-v1",true)));
    Check(result.Distinct().Count()==1 && transport.Posts==2,"concurrent medium requests generate small then medium once each through shared stripe");
    var info=await Image.IdentifyAsync(result[0]); Check(info.Width==1500 && info.Height==1000,"medium WebP preserves aspect ratio within 1500px");
    var small=await thumbnails.GetOrCreateAsync("owner",path,ThumbnailPriority.Visible,default,"contain-v1");
    Check(small!=result[0] && (await Image.IdentifyAsync(small)).Width==480,"small and medium caches are separate");
    Check(await thumbnails.GetOrCreateAsync("another-owner",path,ThumbnailPriority.Visible,default,"contain-v1")==small,"small cache shared across owners");
    var offline=new ThumbnailService(opts,new Env(root),queue,new ThumbnailQueueSettings());
    Check(await offline.GetOrCreateAsync("owner",path,ThumbnailPriority.Visible,default,"contain-v1",true)==result[0],"cached medium served with no remote configured");
    Check(await thumbnails.GetOrCreateAsync("owner",path,ThumbnailPriority.Background,default,"contain-v1",true)==result[0],"background reuses existing medium cache");
    var legacySource=Path.Combine(root,"legacy.jpg"); File.Copy(path,legacySource);
    var legacyInfo=new FileInfo(legacySource);
    var legacyIdentity=$"contain-v1|owner|{legacyInfo.FullName}|{legacyInfo.Length}|{legacyInfo.LastWriteTimeUtc.Ticks}|480x360|{opts.Value.ThumbnailQuality}";
    var legacyKey=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(legacyIdentity))).ToLowerInvariant();
    var legacyFile=Path.Combine(opts.Value.CachePath,legacyKey[..2],legacyKey+".webp");
    Directory.CreateDirectory(Path.GetDirectoryName(legacyFile)!); await File.WriteAllBytesAsync(legacyFile,new byte[]{1,2,3});
    var generated=await thumbnails.GetOrCreateAsync("owner",legacySource,ThumbnailPriority.Visible,default,"contain-v1");
    Check(generated!=legacyFile && (await Image.IdentifyAsync(generated)).Width==480 && File.ReadAllBytes(legacyFile).SequenceEqual(new byte[]{1,2,3}),"Legacy cache is ignored without moving or deleting it");
    if(args.Length==2) {
        opts.Value.ExifToolPath=args[0];
        var before=transport.Posts;
        var rawSmall=await thumbnails.GetOrCreateAsync("raw-owner",args[1],ThumbnailPriority.Visible,default,"contain-v1");
        var rawMedium=await thumbnails.GetOrCreateAsync("raw-owner",args[1],ThumbnailPriority.Visible,default,"contain-v1",true);
        Check(transport.Posts==before+2 && File.Exists(rawSmall) && File.Exists(rawMedium) && remote.IsAvailable,"RAW embedded preview succeeds through remote small and medium queues");
    }
    if(args.Length==3) {
        opts.Value.FfmpegPath=args[0];
        var before=transport.Posts;
        var videoSmall=await thumbnails.GetOrCreateAsync("video-owner",args[1],ThumbnailPriority.Visible,default,"contain-v1");
        var videoMedium=await thumbnails.GetOrCreateAsync("video-owner",args[1],ThumbnailPriority.Visible,default,"contain-v1",true);
        Check(transport.Posts==before+2 && File.Exists(videoSmall) && File.Exists(videoMedium) && remote.IsAvailable,"Video frames succeed through remote small and medium queues");
    }
    foreach(var priority in new[]{ThumbnailPriority.Background,ThumbnailPriority.Visible}) {
        var busyPath=Path.Combine(root,$"busy-{priority}.jpg");File.Copy(path,busyPath);
        transport.Busy=true;var beforePosts=transport.Posts;
        var beforeFiles=Directory.EnumerateFiles(opts.Value.CachePath,"*.webp",SearchOption.AllDirectories).Count();
        var waiting=thumbnails.GetOrCreateAsync("owner",busyPath,priority,default,"contain-v1");
        using(var deadline=new CancellationTokenSource(5000))while(transport.Posts==beforePosts)await Task.Delay(10,deadline.Token);
        await Task.Delay(100);
        Check(!waiting.IsCompleted && remote.IsAvailable && Directory.EnumerateFiles(opts.Value.CachePath,"*.webp",SearchOption.AllDirectories).Count()==beforeFiles,$"{priority} HTTP429 waits remotely, no local cache generated");
        transport.Busy=false;await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }
    foreach(var status in new[]{413,422}) {
        transport.Reject=status;var rejectedPath=Path.Combine(root,$"reject-{status}.jpg");File.Copy(path,rejectedPath);
        var beforeFiles=Directory.EnumerateFiles(opts.Value.CachePath,"*.webp",SearchOption.AllDirectories).Count();
        try { await thumbnails.GetOrCreateAsync("owner",rejectedPath,ThumbnailPriority.Visible,default,"contain-v1");throw new Exception("Expected rejection"); }catch(RemoteThumbnailRejectedException){}
        Check(remote.IsAvailable && Directory.EnumerateFiles(opts.Value.CachePath,"*.webp",SearchOption.AllDirectories).Count()==beforeFiles,$"HTTP{status} does not trigger local decode or mark service offline");
    }
    transport.Reject=0;
    var preemptPath=Path.Combine(root,"preempt.jpg");File.Copy(path,preemptPath);
    transport.Hold=true;
    var backgroundJob=thumbnails.GetOrCreateAsync("owner",preemptPath,ThumbnailPriority.Background,default,"contain-v1",true);
    await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    transport.Hold=false;
    var demandPath=Path.Combine(root,"demand.jpg");File.Copy(path,demandPath);
    await thumbnails.GetOrCreateAsync("owner",demandPath,ThumbnailPriority.Visible,default,"contain-v1",true);
    try { await backgroundJob;throw new Exception("Remote background should be cancelled"); } catch(BackgroundThumbnailPreemptedException){}
    Check(remote.IsAvailable,"Remote preemption preserves connected status and foreground proceeds");
    Check(!Directory.EnumerateFiles(opts.Value.CachePath,"*.new",SearchOption.AllDirectories).Any(),"Preempted remote leaves no partial cache publication");
    var missing=Path.Combine(root,"second.jpg");File.Copy(path,missing);
    transport.Fail=true;
    var fallback=await thumbnails.GetOrCreateAsync("owner",missing,ThumbnailPriority.Visible,default,"contain-v1",true);
    Check((await Image.IdentifyAsync(fallback)).Width==1500,"remote failure falls back to local medium with correct dimensions");
    var backgroundPath=Path.Combine(root,"background.jpg");File.Copy(path,backgroundPath);
    Check((await Image.IdentifyAsync(await thumbnails.GetOrCreateAsync("owner",backgroundPath,ThumbnailPriority.Background,default,"contain-v1",true))).Width==1500,"background local medium works when remote is offline");
    await remote.StopAsync(default); await queue.StopAsync(default);
} finally {Directory.Delete(root,true);}

sealed class ResizerFixture : HttpMessageHandler {
    public int Posts,Reject; public bool Fail,Hold,Busy; public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
        if(request.Method==HttpMethod.Get)return new(HttpStatusCode.OK){Content=new StringContent("{\"status\":\"ok\"}")};
        Interlocked.Increment(ref Posts);
        if(Busy)return new(HttpStatusCode.TooManyRequests);
        if(Reject!=0)return new((HttpStatusCode)Reject);
        if(Hold){Started.TrySetResult();await Task.Delay(Timeout.Infinite,token);}
        if(Fail)return new(HttpStatusCode.ServiceUnavailable){Content=new StringContent("offline")};
        await Task.Delay(80,token);
        var width=request.RequestUri!.Query.Contains("width=1500")?1500:480;
        using var image=new Image<Rgb24>(width,width*2/3,new Rgb24(20,80,130));
        using var output=new MemoryStream();await image.SaveAsWebpAsync(output,token);
        var content=new ByteArrayContent(output.ToArray());content.Headers.ContentType=new("image/webp");return new(HttpStatusCode.OK){Content=content};
    }
}
sealed class Env(string root):IWebHostEnvironment {
    public string ApplicationName{get;set;}="Test";public string EnvironmentName{get;set;}="Test";
    public string ContentRootPath{get;set;}=root;public string WebRootPath{get;set;}=root;
    public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
}
