using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

foreach(var path in args) {
    using var source=File.OpenRead(path); using var counted=new CountedStream(source);
    var watch=Stopwatch.StartNew();
    var info=await Image.IdentifyAsync(counted);
    string? identifiedDate=null;
    var hasDate=info.Metadata.ExifProfile?.TryGetValue(ExifTag.DateTimeOriginal,out var original)==true;
    if(hasDate) identifiedDate=info.Metadata.ExifProfile!.Values.First(x=>x.Tag.ToString()=="DateTimeOriginal").GetValue()?.ToString();
    Console.WriteLine($"{Path.GetFileName(path)} length={source.Length} Identify read={counted.Bytes} ({100d*counted.Bytes/source.Length:F2}%) calls={counted.Reads} seeks={counted.Seeks} elapsed={watch.ElapsedMilliseconds}ms date={hasDate}");
    source.Position=0; using var headers=new CountedStream(source);watch.Restart();
    var date=await WebGallery.Services.JpegDateTaken.ReadAsync(headers,default);
    if(date!=WebGallery.Services.DateTakenIndexer.Parse(identifiedDate))throw new Exception("Production header/Identify mismatch");
    Console.WriteLine($"Production JPEG header-only read={headers.Bytes} ({100d*headers.Bytes/source.Length:F4}%) calls={headers.Reads} seeks={headers.Seeks} elapsed={watch.ElapsedMilliseconds}ms date={date.HasValue}; matches Identify");
}

sealed class CountedStream(Stream inner):Stream {
 public long Bytes,Reads,Seeks;
 public override bool CanRead=>true;public override bool CanSeek=>true;public override bool CanWrite=>false;
 public override long Length=>inner.Length;public override long Position{get=>inner.Position;set{Seeks++;inner.Position=value;}}
 public override int Read(byte[] b,int o,int c){var n=inner.Read(b,o,c);Reads++;Bytes+=n;return n;}
 public override int Read(Span<byte> b){var n=inner.Read(b);Reads++;Bytes+=n;return n;}
 public override async ValueTask<int> ReadAsync(Memory<byte> b,CancellationToken t=default){var n=await inner.ReadAsync(b,t);Reads++;Bytes+=n;return n;}
 public override long Seek(long o,SeekOrigin s){Seeks++;return inner.Seek(o,s);}
 public override void Flush(){}public override void SetLength(long v)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
}
