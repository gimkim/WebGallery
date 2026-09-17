using System.Runtime.InteropServices;

internal static class Turbo
{
    // TurboJPEG's stable 2.x API. unsigned long is 32-bit on Windows x64.
    [DllImport("turbojpeg", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr tjInitDecompress();
    [DllImport("turbojpeg", CallingConvention = CallingConvention.Cdecl)] static extern int tjDecompressHeader3(IntPtr h, byte[] jpeg, uint size, out int w, out int hgt, out int subsamp, out int color);
    [DllImport("turbojpeg", CallingConvention = CallingConvention.Cdecl)] static extern int tjDecompress2(IntPtr h, byte[] jpeg, uint size, byte[] dst, int width, int pitch, int height, int pixelFormat, int flags);
    [DllImport("turbojpeg", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr tjGetErrorStr2(IntPtr h);
    [DllImport("turbojpeg", CallingConvention = CallingConvention.Cdecl)] static extern int tjDestroy(IntPtr h);
    public static (byte[] Pixels, int Width, int Height) Decode(byte[] jpeg, int bound, bool reduced)
    {
        var handle = tjInitDecompress();
        if (handle == IntPtr.Zero) throw new InvalidOperationException("TurboJPEG init failed");
        try
        {
            void Check(int code) { if (code != 0) throw new InvalidOperationException(Marshal.PtrToStringAnsi(tjGetErrorStr2(handle))); }
            Check(tjDecompressHeader3(handle, jpeg, checked((uint)jpeg.Length), out int w, out int h, out _, out _));
            if (w <= 0 || h <= 0 || (long)w*h > 96_000_000) throw new InvalidOperationException("Dimensions exceed benchmark safety limit");
            int divisor = 1;
            if (reduced) foreach (var candidate in new[] { 2, 4, 8 })
                if (Math.Max((w+candidate-1)/candidate, (h+candidate-1)/candidate) >= bound) divisor = candidate;
            w = (w+divisor-1)/divisor; h = (h+divisor-1)/divisor;
            var pixels = new byte[checked(w*h*3)];
            Check(tjDecompress2(handle, jpeg, checked((uint)jpeg.Length), pixels, w, 0, h, 0, 0));
            return (pixels, w, h);
        }
        finally { tjDestroy(handle); }
    }
}
