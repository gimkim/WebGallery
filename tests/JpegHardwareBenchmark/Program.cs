using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;

if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new PlatformNotSupportedException("Windows x64 benchmark only");
if (args.Length == 0) { Console.WriteLine("JpegHardwareBenchmark <folder> [count=24] [settings.json] [workers=1,2,4] [rounds=2] [previous-report.json] [--capability-only]"); return; }
var folder = Path.GetFullPath(args[0]);
var count = args.Length > 1 ? Math.Clamp(int.Parse(args[1]),1,128) : 24;
int width=480, height=360, quality=78;
var ffmpeg = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","ffmpeg.exe"));
if (args.Length>2 && File.Exists(args[2]))
{
    using var settings=JsonDocument.Parse(File.ReadAllText(args[2]));
    var g=settings.RootElement.GetProperty("Gallery");
    width=g.GetProperty("ThumbnailWidth").GetInt32(); height=g.GetProperty("ThumbnailHeight").GetInt32(); quality=g.GetProperty("ThumbnailQuality").GetInt32();
    if (g.TryGetProperty("FfmpegPath",out var configured) && File.Exists(configured.GetString())) ffmpeg=configured.GetString()!;
}
ffmpeg=Environment.GetEnvironmentVariable("JPEG_BENCH_FFMPEG") ?? ffmpeg;
var device=Environment.GetEnvironmentVariable("JPEG_BENCH_QSV_DEVICE") ?? "qsv=hw:hw,child_device_type=d3d11va";
var workers=(args.Length>3 ? args[3] : "1,2,4").Split(',').Select(int.Parse).Distinct().ToArray();
if (workers.Any(w=>w<1 || w>4) || width<1 || height<1 || width>4096 || height>4096) throw new ArgumentException("Workers 1..4, dimensions 1..4096 only");
var rounds=args.Length>4 ? Math.Clamp(int.Parse(args[4]),1,5) : 2;
var capabilityOnly=args.Contains("--capability-only");
var output=Path.Combine(AppContext.BaseDirectory,"results-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);
Directory.CreateDirectory(output);
var options=new JsonSerializerOptions { WriteIndented=true };
async Task Save(string name,object data)=>await File.WriteAllTextAsync(Path.Combine(output,name),JsonSerializer.Serialize(data,options));
Console.WriteLine($"Host={Environment.MachineName}, CPUs={Environment.ProcessorCount}, output={output}");
Console.WriteLine("Close gallery tabs; wait for idle CPU. Originals/settings are READ ONLY. No cache flush. QSV times include subprocess startup, device init and transfers; NOT pure GPU kernel times.");
var inventory=await Runner.Run("powershell.exe",["-NoProfile","-NonInteractive","-Command","Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,PNPDeviceID | ConvertTo-Json"],maxOutput:100000);
await File.WriteAllTextAsync(Path.Combine(output,"gpu-inventory.json"),System.Text.Encoding.UTF8.GetString(inventory.Output));
var caps=new List<object>();
foreach(var probe in new[] { "-version", "-decoders", "-filters", "-hwaccels" })
{
    try { var r=await Runner.Run(ffmpeg,["-hide_banner",probe],maxOutput:1_000_000); await File.WriteAllTextAsync(Path.Combine(output,"ffmpeg"+probe+".txt"),System.Text.Encoding.UTF8.GetString(r.Output)+r.Log); }
    catch(Exception ex) { caps.Add(new { probe,error=ex.Message }); }
}
// Synthetic baseline and progressive JPEGs test actual codec initialization independently of users' images.
var fixtures=new List<(string Name,byte[] Bytes)>();
foreach(var progressive in new[] {false,true})
{
    using var im=new Image<Rgb24>(2400,1600);
    im.Metadata.ExifProfile=new ExifProfile();
    im.Metadata.ExifProfile.SetValue(ExifTag.Orientation,(ushort)6);
    im.ProcessPixelRows(rows=> { for(int y=0;y<rows.Height;y++) { var row=rows.GetRowSpan(y); for(int x=0;x<row.Length;x++) row[x]=new Rgb24((byte)x,(byte)y,(byte)(x+y)); } });
    using var ms=new MemoryStream(); im.SaveAsJpeg(ms,new JpegEncoder { Quality=90 });
    var fixtureBytes=ms.ToArray();
    if(progressive)
    {
        try {
            var converted=await Runner.Run(Path.Combine(AppContext.BaseDirectory,"jpegtran.exe"),["-progressive","-copy","all"],fixtureBytes,8_000_000);
            if(converted.ExitCode!=0) throw new IOException(converted.Log);
            fixtureBytes=converted.Output;
        } catch(Exception ex) { caps.Add(new {fixture="progressive",ok=false,error="Fixture generation failed: "+ex.Message}); continue; }
    }
    fixtures.Add((progressive ? "progressive" : "baseline",fixtureBytes));
    await File.WriteAllBytesAsync(Path.Combine(output,progressive ? "fixture-progressive.jpg" : "fixture-baseline.jpg"),fixtureBytes);
}
foreach(var f in fixtures)
{
    try {
        var d=Turbo.Decode(f.Bytes,480,false); var scaled=Turbo.Decode(f.Bytes,480,true);
        var ok=d.Width==2400 && d.Height==1600 && scaled.Width==600 && scaled.Height==400;
        caps.Add(new { backend="turbojpeg",fixture=f.Name,ok,full=$"{d.Width}x{d.Height}",scaled=$"{scaled.Width}x{scaled.Height}" });
        Console.WriteLine($"Capability {f.Name}/turbojpeg: {(ok?"PASS":"FAIL")}");
    }
    catch(Exception ex) { caps.Add(new { backend="turbojpeg",fixture=f.Name,ok=false,error=ex.Message }); }
    foreach(var stage in new[] {"decode-only","qsv-decode","qsv-vpp"})
    {
        try {
            var w=stage=="qsv-vpp" ? 480 : 2400; var h=stage=="qsv-vpp" ? 320 : 1600;
            var r=await Runner.Run(ffmpeg,Runner.QsvArgs(w,h,stage,device),f.Bytes,stage=="decode-only" ? 4096 : w*h*3);
            var valid=r.ExitCode==0 && (stage=="decode-only" ? System.Text.RegularExpressions.Regex.IsMatch(r.Log,@"frame=\s*1\b|1 frames successfully decoded") : r.Output.Length==w*h*3);
            await File.WriteAllTextAsync(Path.Combine(output,$"cap-{f.Name}-{stage}.log"),r.Log);
            caps.Add(new {backend=stage,fixture=f.Name,ok=valid,r.ExitCode,r.WallMs,r.CpuMs,bytes=r.Output.Length,forcedHardware=true});
            Console.WriteLine($"Capability {f.Name}/{stage}: {(valid?"PASS":"FAIL (see log)")}");
        } catch(Exception ex) { caps.Add(new {backend=stage,fixture=f.Name,ok=false,error=ex.Message}); }
    }
}
await Save("capabilities.json",new {host=Environment.MachineName,ffmpeg,device,caps,turboDll=File.Exists(Path.Combine(AppContext.BaseDirectory,"turbojpeg.dll")) ? Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,"turbojpeg.dll")))) : "missing"});
if(capabilityOnly) { Console.WriteLine("Capabilities saved. No user images processed."); return; }
var files=Directory.EnumerateFiles(folder,"*",new EnumerationOptions { RecurseSubdirectories=true,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.Hidden|FileAttributes.System|FileAttributes.ReparsePoint })
    .Where(p=>Path.GetExtension(p).Equals(".jpg",StringComparison.OrdinalIgnoreCase)||Path.GetExtension(p).Equals(".jpeg",StringComparison.OrdinalIgnoreCase)).Take(count).ToArray();
if(args.Length>5 && File.Exists(args[5]))
{
    using var previous=JsonDocument.Parse(File.ReadAllText(args[5]));
    files=previous.RootElement.GetProperty("runs")[0].GetProperty("rows").EnumerateArray().Select(r=>r.GetProperty("File").GetString()!).Distinct().Take(count).ToArray();
}
await Save("sample.json",files);
var methods=new[] {"imagesharp-idct","turbo-full","turbo-scaled","qsv-decode-only","qsv-decode","qsv-vpp"};
var runs=new List<object>();
// Warm native/ImageSharp codecs on a synthetic image only; FFmpeg creates a fresh process per image by design.
for(int i=0;i<3;i++)
{
    using var stream=new MemoryStream(fixtures[0].Bytes,false);
    using var warm=JpegDecoder.Instance.Decode(new JpegDecoderOptions {GeneralOptions=new DecoderOptions {TargetSize=new Size(Math.Max(width,height),Math.Max(width,height))},ResizeMode=JpegDecoderResizeMode.IdctOnly},stream);
    warm.Mutate(x=>x.AutoOrient().Resize(new ResizeOptions {Size=new Size(width,height),Mode=ResizeMode.Max,Sampler=KnownResamplers.Lanczos3}));
    using var m=new MemoryStream(); warm.SaveAsWebp(m,new WebpEncoder {Quality=quality});
}
try { Turbo.Decode(fixtures[0].Bytes,480,true); } catch { }
for(int round=1;round<=rounds;round++) foreach(int worker in workers)
foreach(var method in round%2==1 ? methods : methods.Reverse().ToArray())
{
    var rows=new ConcurrentBag<Row>(); var processCpu=Process.GetCurrentProcess().TotalProcessorTime; var wall=Stopwatch.StartNew();
    await Parallel.ForEachAsync(files.Select((file,index)=>(file,index)),new ParallelOptions {MaxDegreeOfParallelism=worker},async (item,ct)=>
    {
        var row=new Row {File=item.file}; var total=Stopwatch.StartNew(); var sw=Stopwatch.StartNew();
        try {
            var fi=new FileInfo(item.file); if(fi.Length>128L*1024*1024) throw new InvalidOperationException("File exceeds 128 MiB limit");
            var bytes=await File.ReadAllBytesAsync(item.file,ct); row.ReadMs=sw.Elapsed.TotalMilliseconds; row.Bytes=bytes.Length;
            sw.Restart(); var info=Image.Identify(bytes); if((long)info.Width*info.Height>96_000_000) throw new InvalidOperationException("Image exceeds 96 megapixel limit");
            var orientation=info.Metadata.ExifProfile; row.MetadataMs=sw.Elapsed.TotalMilliseconds;
            row.SourceDimensions=$"{info.Width}x{info.Height}";
            Image image;
            sw.Restart();
            if(method=="imagesharp-idct")
            {
                using var stream=new MemoryStream(bytes,false);
                image=JpegDecoder.Instance.Decode(new JpegDecoderOptions {GeneralOptions=new DecoderOptions {TargetSize=new Size(Math.Max(width,height),Math.Max(width,height))},ResizeMode=JpegDecoderResizeMode.IdctOnly},stream);
                row.DecodeMs=sw.Elapsed.TotalMilliseconds;
            }
            else if(method.StartsWith("turbo"))
            {
                var pixels=Turbo.Decode(bytes,Math.Max(width,height),method=="turbo-scaled"); row.DecodeMs=sw.Elapsed.TotalMilliseconds;
                sw.Restart(); image=Image.LoadPixelData<Rgb24>(pixels.Pixels,pixels.Width,pixels.Height); row.PixelImportMs=sw.Elapsed.TotalMilliseconds;
            }
            else
            {
                var scale=method=="qsv-vpp" ? Math.Min(1,(double)Math.Max(width,height)/Math.Max(info.Width,info.Height)) : 1;
                int w=method=="qsv-vpp" ? Math.Max(2,(int)(info.Width*scale)/2*2) : info.Width;
                int h=method=="qsv-vpp" ? Math.Max(2,(int)(info.Height*scale)/2*2) : info.Height;
                var decodeOnly=method=="qsv-decode-only";
                var child=await Runner.Run(ffmpeg,Runner.QsvArgs(w,h,decodeOnly ? "decode-only" : method,device),bytes,decodeOnly ? 4096 : checked(w*h*3));
                row.GpuPipelineMs=child.WallMs; row.ChildCpuMs=child.CpuMs;
                var logName=$"r{round}-w{worker}-{method}-{item.index:D3}.log"; await File.WriteAllTextAsync(Path.Combine(output,logName),child.Log,ct);
                if(child.ExitCode!=0 || (decodeOnly ? !System.Text.RegularExpressions.Regex.IsMatch(child.Log,@"frame=\s*1\b|1 frames successfully decoded") : child.Output.Length!=w*h*3)) throw new InvalidOperationException($"Forced QSV failed (exit {child.ExitCode}, {child.Output.Length} bytes); see {logName}. NO CPU fallback.");
                if(decodeOnly) { row.TotalMs=total.Elapsed.TotalMilliseconds; rows.Add(row); return; }
                sw.Restart(); image=Image.LoadPixelData<Rgb24>(child.Output,w,h); row.PixelImportMs=sw.Elapsed.TotalMilliseconds;
            }
            using(image)
            {
                row.DecodedDimensions=$"{image.Width}x{image.Height}";
                // Same original EXIF orientation and final CPU Lanczos/WebP for all paths.
                image.Metadata.ExifProfile=orientation?.DeepClone();
                sw.Restart(); image.Mutate(x=>x.AutoOrient().Resize(new ResizeOptions {Size=new Size(width,height),Mode=ResizeMode.Max,Sampler=KnownResamplers.Lanczos3})); row.OrientResizeMs=sw.Elapsed.TotalMilliseconds;
                row.OutputDimensions=$"{image.Width}x{image.Height}";
                sw.Restart(); using var encoded=new MemoryStream(); await image.SaveAsWebpAsync(encoded,new WebpEncoder {Quality=quality},ct); row.WebpMs=sw.Elapsed.TotalMilliseconds;
                sw.Restart(); await File.WriteAllBytesAsync(Path.Combine(output,$"r{round}-w{worker}-{method}-{item.index:D3}.webp"),encoded.ToArray(),ct); row.WriteMs=sw.Elapsed.TotalMilliseconds;
            }
        } catch(Exception ex) {row.Error=ex.GetType().Name+": "+ex.Message;}
        row.TotalMs=total.Elapsed.TotalMilliseconds; rows.Add(row);
    });
    wall.Stop(); var good=rows.Where(r=>r.Error==null).ToArray();
    var parentCpuMs=(Process.GetCurrentProcess().TotalProcessorTime-processCpu).TotalMilliseconds;
    Console.WriteLine($"{method} r{round} w{worker}: {good.Length}/{files.Length} OK, {wall.Elapsed.TotalSeconds:F2}s, {good.Length/wall.Elapsed.TotalSeconds:F2} images/s");
    if(good.Length>0) Console.WriteLine($"Mean ms: read={good.Average(r=>r.ReadMs):F1}, metadata={good.Average(r=>r.MetadataMs):F1}, CPU-decode={good.Average(r=>r.DecodeMs??0):F1}, GPU-combined={good.Average(r=>r.GpuPipelineMs??0):F1}, import={good.Average(r=>r.PixelImportMs):F1}, orient/resize={good.Average(r=>r.OrientResizeMs):F1}, WebP={good.Average(r=>r.WebpMs):F1}, write={good.Average(r=>r.WriteMs):F1}");
    runs.Add(new {method,round,workers=worker,wallSeconds=wall.Elapsed.TotalSeconds,parentCpuMs,childCpuMs=rows.Sum(r=>r.ChildCpuMs),success=good.Length,failed=rows.Count-good.Length,rows=rows.OrderBy(r=>r.File).ToArray()});
    await Save("report.json",new {host=Environment.MachineName,processors=Environment.ProcessorCount,width,height,quality,ffmpeg,device,note="No fallback. Warm OS cache, first-N sample. QSV combined includes per-image process/device setup, decode, optional VPP, GPU download and CPU RGB conversion; cannot isolate those stages or subtract independent probes. CPU native timings are in-process. Total includes diagnostic logging. Compare success coverage and common successful files, not successful-only means when failures differ. No ICC color-management equivalence guaranteed; inspect saved thumbnails.",runs});
}
Console.WriteLine("Done: "+Path.Combine(output,"report.json"));

internal sealed class Row
{
    public string File {get;set;}=""; public long Bytes {get;set;}
    public string SourceDimensions {get;set;}=""; public string DecodedDimensions {get;set;}=""; public string OutputDimensions {get;set;}="";
    public double ReadMs {get;set;} public double MetadataMs {get;set;} public double? DecodeMs {get;set;} public double? GpuPipelineMs {get;set;}
    public double ChildCpuMs {get;set;} public double PixelImportMs {get;set;} public double OrientResizeMs {get;set;} public double WebpMs {get;set;} public double WriteMs {get;set;} public double TotalMs {get;set;}
    public string? Error {get;set;}
}
