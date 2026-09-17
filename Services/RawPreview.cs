using System.Diagnostics;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace WebGallery.Services;

public sealed class RawPreviewException(string message) : Exception(message);

public static class RawPreview {
    public static readonly string[] Extensions = [".arw", ".srf", ".sr2", ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".raf", ".rw2", ".orf", ".ori", ".dng", ".pef", ".rwl", ".srw", ".3fr", ".fff", ".iiq", ".kdc", ".dcr", ".mos", ".mrw", ".x3f"];
    public static bool IsRaw(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Slots = new(2,2);
    public static async Task<Stream> OpenAsync(string path, string executable, CancellationToken token) {
        if (!IsRaw(path)) return File.OpenRead(path);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        await Slots.WaitAsync(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(40));
        try {
            using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true } };
            foreach(var argument in new[]{"-config", "", "-j", "-b", "-n", "-JpgFromRaw", "-PreviewImage", "-ThumbnailImage", "-Orientation", "--", Path.GetFullPath(path)}) process.StartInfo.ArgumentList.Add(argument);
            process.StartInfo.Environment["LC_ALL"]="C";
            try { process.Start(); } catch(System.ComponentModel.Win32Exception) { throw new RawPreviewException("RAW preview reader is unavailable. Configure Gallery:ExifToolPath."); }
            using var cancel=deadline.Token.Register(()=>{try{process.Kill(true);}catch(InvalidOperationException){} });
            var errors=process.StandardError.ReadToEndAsync(deadline.Token);
            using var output=new MemoryStream();
            try {
                var buffer=new byte[65536];int count;
                while((count=await process.StandardOutput.BaseStream.ReadAsync(buffer,deadline.Token))>0) {
                    if(output.Length+count>96L*1024*1024) throw new RawPreviewException("Embedded RAW previews exceed the size limit.");
                    await output.WriteAsync(buffer.AsMemory(0,count),deadline.Token);
                }
                await process.WaitForExitAsync(deadline.Token);await errors;
                deadline.Token.ThrowIfCancellationRequested();
                if(process.ExitCode!=0) throw new RawPreviewException("Unable to read embedded RAW preview.");
                using var json=JsonDocument.Parse(output.ToArray());
                var metadata=json.RootElement[0];byte[]? best=null;long pixels=0;
                foreach(var tag in new[]{"JpgFromRaw","PreviewImage","ThumbnailImage"}) {
                    if(!metadata.TryGetProperty(tag,out var value) || value.ValueKind!=JsonValueKind.String)continue;
                    var encoded=value.GetString()!;if(!encoded.StartsWith("base64:"))continue;
                    var bytes=Convert.FromBase64String(encoded[7..]);
                    if(bytes.Length<2 || bytes[0]!=255 || bytes[1]!=216)continue;
                    using var stream=new MemoryStream(bytes,false);
                    var info=await Image.IdentifyAsync(stream,deadline.Token);
                    var area=(long)info.Width*info.Height;
                    if(area>120_000_000 || area<=pixels)continue;
                    // Preview JPEGs often omit the camera's orientation. Inject a tiny EXIF
                    // orientation block only when their own orientation is absent.
                    if((info.Metadata.ExifProfile is null || !info.Metadata.ExifProfile.TryGetValue(ExifTag.Orientation,out _))
                       && metadata.TryGetProperty("Orientation",out var orientation) && orientation.TryGetInt32(out var o) && o is >=2 and <=8) {
                        var exif=new ExifProfile();exif.SetValue(ExifTag.Orientation,(ushort)o);
                        var payload=exif.ToByteArray()!;var length=payload.Length+2;
                        using var oriented=new MemoryStream();oriented.Write(bytes,0,2);
                        oriented.Write(new byte[]{255,225,(byte)(length>>8),(byte)length});oriented.Write(payload);oriented.Write(bytes,2,bytes.Length-2);bytes=oriented.ToArray();
                    }
                    best=bytes;pixels=area;
                }
                if(best is null) throw new RawPreviewException("This RAW file has no supported embedded JPEG preview.");
                return new MemoryStream(best,false);
            } finally {
                if(!process.HasExited) { process.Kill(true);await process.WaitForExitAsync(CancellationToken.None); }
                try{await errors;}catch(OperationCanceledException){}
            }
        } catch(OperationCanceledException) when(!token.IsCancellationRequested) { throw new RawPreviewException("RAW preview extraction timed out."); }
          catch(JsonException) { throw new RawPreviewException("Invalid RAW preview metadata."); }
          catch(FormatException) { throw new RawPreviewException("Invalid embedded RAW preview."); }
          catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or UnknownImageFormatException or InvalidImageContentException) { throw new RawPreviewException("Unable to read a valid embedded RAW preview."); }
        finally { Slots.Release(); }
    }
}
