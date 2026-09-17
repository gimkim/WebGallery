using System.Buffers.Binary;
using System.Text;
using WebGallery.Services;

void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
byte[] Tiff(bool little) {
 var b=new byte[64];b[0]=b[1]=(byte)(little?'I':'M');
 void W16(int o,ushort n){if(little)BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o),n);else BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(o),n);}
 void W32(int o,uint n){if(little)BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o),n);else BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o),n);}
 W16(2,42);W32(4,8);W16(8,1);W16(10,0x8769);W16(12,4);W32(14,1);W32(18,26);
 W16(26,1);W16(28,0x9003);W16(30,2);W32(32,20);W32(36,44);
 Encoding.ASCII.GetBytes("2024:12:31 23:59:58\0").CopyTo(b,44);return b;
}
byte[] Jpeg(byte[]? tiff) {
 var bytes=new List<byte>{255,216,255,226,0,10,1,2,3,4,5,6,7,8};
 if(tiff!=null){var len=tiff.Length+8;bytes.AddRange(new byte[]{255,225,(byte)(len>>8),(byte)len});bytes.AddRange("Exif\0\0"u8.ToArray());bytes.AddRange(tiff);}
 bytes.AddRange(new byte[]{255,218});return bytes.ToArray();
}
foreach(var little in new[]{true,false}) {
 var header=Jpeg(Tiff(little));using var stream=new Guard(header);
 Check(await JpegDateTaken.ReadAsync(stream,default)==new DateTime(2024,12,31,23,59,58),$"{(little?"Little":"Big")}-endian EXIF without reading image body");
}
using(var stream=new Guard(Jpeg(null)))Check(await JpegDateTaken.ReadAsync(stream,default)==null,"No EXIF stops at SOS before image data");
var bad=Tiff(true);BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(36),uint.MaxValue);
using(var stream=new Guard(Jpeg(bad)))Check(await JpegDateTaken.ReadAsync(stream,default)==null,"Invalid TIFF offset bounded safely");
bad=Tiff(true);BinaryPrimitives.WriteUInt16LittleEndian(bad.AsSpan(26),ushort.MaxValue);
Check(JpegDateTaken.ReadExif(bad)==null,"Oversized IFD count rejected");
using(var stream=new MemoryStream(new byte[]{255,216,255,225,0,100,0})) {
 try{await JpegDateTaken.ReadAsync(stream,default);throw new Exception("Expected malformed segment failure");}catch(InvalidDataException){Console.WriteLine("PASS truncated segment rejected");}
}
using(var stop=new CancellationTokenSource()){stop.Cancel();using var stream=new Guard(Jpeg(Tiff(true)));try{await JpegDateTaken.ReadAsync(stream,stop.Token);throw new Exception("Expected cancellation");}catch(OperationCanceledException){Console.WriteLine("PASS cancellation");}}
sealed class Guard(byte[] header):MemoryStream(header.Concat(new byte[1024*1024]).ToArray()) {
 public override ValueTask<int> ReadAsync(Memory<byte> b,CancellationToken t=default){if(Position+b.Length>header.Length)throw new Exception("Attempted compressed image read");return base.ReadAsync(b,t);}
}
