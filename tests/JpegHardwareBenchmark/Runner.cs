using System.Diagnostics;

internal sealed record ChildResult(int ExitCode, double WallMs, double CpuMs, byte[] Output, string Log);
internal static class Runner
{
    public static async Task<ChildResult> Run(string exe, IEnumerable<string> args, byte[]? input = null, int maxOutput = 4096, int timeoutSeconds = 60)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo=start };
        var timer = Stopwatch.StartNew();
        process.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var registration = timeout.Token.Register(() => { try { process.Kill(true); } catch { } });
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = Task.Run(async () => {
            using var output = new MemoryStream();
            var buffer = new byte[65536]; int read;
            while ((read = await process.StandardOutput.BaseStream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (output.Length + read > maxOutput) { process.Kill(true); throw new IOException("Child output exceeded bounded buffer"); }
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        });
        try
        {
            if (input is not null) { try { await process.StandardInput.BaseStream.WriteAsync(input, timeout.Token); } catch (IOException) { } }
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var bytes = await stdout;
            var log = await stderr;
            return new(process.ExitCode, timer.Elapsed.TotalMilliseconds, process.TotalProcessorTime.TotalMilliseconds, bytes, log);
        }
        catch
        {
            try { process.Kill(true); } catch { }
            try { await process.WaitForExitAsync(); await stdout; await stderr; } catch { }
            throw;
        }
    }

    public static string[] QsvArgs(int width, int height, string stage, string device)
    {
        var a = new List<string> { "-hide_banner", "-loglevel", "verbose", "-nostdin", "-init_hw_device", device, "-hwaccel", "qsv", "-hwaccel_device", "hw", "-hwaccel_output_format", "qsv", "-c:v", "mjpeg_qsv", "-noautorotate", "-f", "mjpeg", "-i", "pipe:0", "-frames:v", "1", "-an" };
        if (stage == "decode-only") a.AddRange(["-f", "null", "-"]);
        else
        {
            var filter = stage == "qsv-vpp" ? $"vpp_qsv=w={width}:h={height}:format=nv12," : "";
            a.AddRange(["-vf", filter+"hwdownload,format=nv12,format=rgb24", "-threads", "1", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1"]);
        }
        return a.ToArray();
    }
}
