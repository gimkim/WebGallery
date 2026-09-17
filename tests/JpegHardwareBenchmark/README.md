# JPEG hardware/native benchmark (Windows x64)

Standalone tool; never modifies the Gallery application, settings, database, cache or originals. Results contain local image paths: do not post reports publicly without redacting them.

## Run on the NAS

Close Gallery tabs and other CPU-heavy tasks first. Use a normal local NAS PowerShell session (not an SMB-launched executable on another PC: that measures the other PC).

```powershell
& 'C:\Users\tatsa\web-tools\jpeg-hardware-benchmark\JpegHardwareBenchmark.exe' 'U:\' 24 'C:\Users\tatsa\web\imagegallery\appsettings.json' '1,2,4' 2
```

Capability-only (synthetic baseline/progressive JPEGs, no gallery scan):

```powershell
& 'C:\Users\tatsa\web-tools\jpeg-hardware-benchmark\JpegHardwareBenchmark.exe' 'U:\' 24 'C:\Users\tatsa\web\imagegallery\appsettings.json' '1' 1 --capability-only
```

Optional sixth positional argument is a previous thumbnail benchmark report.json, reusing its first run's File paths in the same order. Otherwise first N .jpg/.jpeg files found recursively are used, not a randomized representative corpus. Include large, progressive and rotated photos to assess support. Defaults: 24 images, workers 1/2/4, 2 rounds with reversed method order. At most 128 files, 4 workers, 128 MiB/file and 96 megapixels/image. Each FFmpeg child has a 60-second timeout and bounded RGB output. This can consume substantial RAM at 4 workers with large originals.

## What is measured

| Method | Meaning |
|---|---|
| imagesharp-idct | Existing reduced IDCT approach, in process |
| turbo-full | libjpeg-turbo native full-resolution RGB decode, in process |
| turbo-scaled | Native JPEG DCT scaling using 1/2, 1/4 or 1/8 when enough pixels remain |
| qsv-decode-only | Forced hardware decode to null; diagnostic only, NOT a completed thumbnail |
| qsv-decode | Forced QSV decode, download full frame and CPU RGB conversion; common CPU resize/WebP |
| qsv-vpp | Forced QSV decode + GPU downscale, download and CPU RGB conversion; common final orient/resize/WebP |

Rows separate read, metadata/header identification, CPU decode, GPU combined pipeline, RGB import/copy, orient/final resize, WebP encoding, and disk write. GPU-combined is **not pure decode time**: FFmpeg process launch, device initialization, pipes, decode, optional VPP, GPU-to-CPU transfer, RGB conversion and shutdown are included. Decode-only is a separate experiment; never subtract independent runs to claim exact GPU stage times. A persistent native oneVPL implementation might be faster than this subprocess prototype. CPU timings exclude native-handle setup only insofar as it is inside Decode (included); parent and FFmpeg child CPU times are separate, neither is GPU utilization. No silent software decoder fallback: unavailable/unsupported QSV is a failure row with log, not a CPU success.

The synthetic capability pass records GPU name/driver, FFmpeg version/decoder/filter/hwaccel listings, actual forced device/decoder execution, returned pixel size, baseline and progressive success/failure. A decoder appearing in the list alone is NOT capability proof. A failure applies to this driver/runtime/format/path, not proof the silicon lacks JPEG decoding. Default device is `qsv=hw:hw,child_device_type=d3d11va`; optional `JPEG_BENCH_QSV_DEVICE` can select another Intel adapter/runtime configuration. `JPEG_BENCH_FFMPEG` overrides the FFmpeg path. No driver installation is performed.

All thumbnail methods use identical configured bounds, original EXIF orientation and ImageSharp Lanczos3 final resize/WebP quality. VPP uses even intermediate dimensions, so rounding/aspect differences can occur. This is not a color-managed equivalence test: CMYK, ICC profiles, YUV range/chroma conversions and resize kernels can produce differences. Inspect paired output WebPs before considering a production switch. Failed files are retained, not silently omitted. Compare common successful files and total coverage, not just a faster successful-only subset.

Results: capabilities.json, GPU inventory, FFmpeg diagnostic logs, sample.json, incremental report.json with per-file stages/errors and run wall/CPU times, plus per-method WebPs. Read timing includes Windows file cache; no OS cache flushing. Repeated rounds are warmer. Wall time includes scheduling and diagnostic log writes. There is no server request/IIS queue overhead measurement. The tool does not automatically alter website settings.

## Build/package

```powershell
dotnet publish tests/JpegHardwareBenchmark -c Release -r win-x64 --self-contained false -o publish/jpeg-hardware-benchmark
```

Requires .NET 10 runtime, existing NAS FFmpeg with mjpeg_qsv/vpp_qsv, and the official Windows x64 libjpeg-turbo native runtime. Package turbojpeg.dll, jpegtran.exe and jpeg62.dll beside the benchmark (jpegtran generates the progressive fixture). These come from official libjpeg-turbo 3.2.0 vc-x64 release, extracted without installation. Include upstream LICENSE.md and README.ijg. Do not commit native binaries or generated result directories.

Sources: https://github.com/libjpeg-turbo/libjpeg-turbo/releases/tag/3.2.0 and its include/turbojpeg.h (stable 2.x API); https://ffmpeg.org/ffmpeg.html; https://github.com/FFmpeg/FFmpeg/blob/master/libavfilter/vf_vpp_qsv.c
