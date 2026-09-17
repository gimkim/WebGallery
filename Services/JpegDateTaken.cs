using System.Buffers.Binary;
using System.Text;

namespace WebGallery.Services;

// Read APP1 only, seek over other segments, and never enter compressed image scans.
public static class JpegDateTaken
{
    public static async Task<DateTime?> ReadAsync(Stream stream, CancellationToken token)
    {
        if (!stream.CanSeek) throw new ArgumentException("A seekable JPEG stream is required.", nameof(stream));
        var pair = new byte[2];
        await stream.ReadExactlyAsync(pair, token);
        if (pair[0] != 0xff || pair[1] != 0xd8) throw new InvalidDataException("Not a JPEG.");
        var metadataBytes = 0;
        for (var segments = 0; segments < 4096; segments++) {
            token.ThrowIfCancellationRequested();
            await stream.ReadExactlyAsync(pair.AsMemory(0, 1), token);
            if (pair[0] != 0xff) throw new InvalidDataException("Invalid JPEG marker.");
            var fill = 0;
            do {
                if (++fill > 4096) throw new InvalidDataException("Excessive JPEG marker padding.");
                await stream.ReadExactlyAsync(pair.AsMemory(0, 1), token);
            } while (pair[0] == 0xff);
            var marker = pair[0];
            if (marker is 0xda or 0xd9) return null; // SOS / EOI: do not read any image data.
            if (marker == 0 || marker == 0xd8) throw new InvalidDataException("Invalid JPEG marker.");
            if (marker == 1 || marker is >= 0xd0 and <= 0xd7) continue;
            await stream.ReadExactlyAsync(pair, token);
            var length = BinaryPrimitives.ReadUInt16BigEndian(pair) - 2;
            if (length < 0 || length > stream.Length - stream.Position) throw new InvalidDataException("Truncated JPEG segment.");
            if (marker != 0xe1) { stream.Seek(length, SeekOrigin.Current); continue; }
            metadataBytes += length;
            if (metadataBytes > 4 * 1024 * 1024) throw new InvalidDataException("JPEG metadata exceeds safety limit.");
            var data = new byte[length];
            await stream.ReadExactlyAsync(data, token);
            if (data.AsSpan().StartsWith("Exif\0\0"u8)) {
                var date = ReadExif(data.AsSpan(6));
                if (date.HasValue) return date;
            }
        }
        throw new InvalidDataException("Too many JPEG header segments.");
    }

    public static DateTime? ReadExif(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8) return null;
        var little = tiff[0] == 'I' && tiff[1] == 'I';
        if (!little && !(tiff[0] == 'M' && tiff[1] == 'M')) return null;
        if (U16(tiff[2..], little) != 42) return null;
        var ifd = U32(tiff[4..], little);
        // IFD0 -> Exif IFD only. No arbitrary TIFF graph traversal, allocation from
        // untrusted counts, thumbnail decoding or maker-note interpretation.
        for (var level = 0; level < 2; level++) {
            if (ifd < 8 || ifd > tiff.Length - 2) return null;
            var offset = (int)ifd;
            var count = U16(tiff[offset..], little);
            if (count > (tiff.Length - offset - 2) / 12) return null;
            uint next = 0;
            for (var i = 0; i < count; i++) {
                var entry = tiff.Slice(offset + 2 + i * 12, 12);
                var tag = U16(entry, little); var type = U16(entry[2..], little);
                var size = U32(entry[4..], little); var value = U32(entry[8..], little);
                if (tag == 0x8769 && type == 4 && size == 1) next = value;
                if (tag != 0x9003 || type != 2 || size < 19 || size > 64) continue;
                if (value > tiff.Length || size > tiff.Length - value) continue;
                var date = DateTakenIndexer.Parse(Encoding.ASCII.GetString(tiff.Slice((int)value, (int)size)));
                if (date.HasValue) return date;
            }
            if (next == 0 || next == ifd) return null;
            ifd = next;
        }
        return null;
    }
    private static ushort U16(ReadOnlySpan<byte> bytes, bool little) => little ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes);
    private static uint U32(ReadOnlySpan<byte> bytes, bool little) => little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);
}
