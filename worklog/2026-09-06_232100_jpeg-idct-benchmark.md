# JPEG reduced-decode comparison

- Request: benchmark faster thumbnail decoding for NAS Intel N100.
- Changed tests/ThumbnailBenchmark/Program.cs to compare full ImageSharp decode with ImageSharp 3.1.12 JpegDecoder IdctOnly TargetSize. Non-JPEG falls back to full decode. Both use AutoOrient, Lanczos3 Max and identical WebP quality. Square target protects rotated portrait bounds. This tests existing ImageSharp reduced IDCT, not libjpeg-turbo.
- Both paths warm up on the first real image; four workers run full/reduced/reduced/full, retaining paired WebPs for quality inspection and JSON timing reports. Optional fourth argument reuses exactly the previous report's sample paths. OS caches are not flushed and memory buffering differs from production streaming. Samples remain convenience samples, not random.
- Published standalone tool to NAS C:\Users\tatsa\web-tools\thumbnail-compare; DLL hash matched. Production website/cache/database unchanged.
- Build and local two-image smoke test passed all four rounds. Local full-decode took 0.78/0.67 seconds versus reduced 0.45/0.39 seconds; these are workstation smoke results, not N100 performance or sufficient quality evidence.
- Pending: user execution on NAS (no remote shell available), analysis of NAS report and visual comparison of paired outputs. No production optimization deployed.
