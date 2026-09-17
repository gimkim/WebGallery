# Thumbnail stage measurement preparation

- Request: measure thumbnail processing on the NAS with 1/2/4 workers.
- Added tests/ThumbnailBenchmark (standalone .NET 10 console, ImageSharp 3.1.12). Reports read, decode, orient/resize, WebP encode and write times, process CPU time, wall throughput and errors. Reads production thumbnail dimensions/quality when settings path is supplied. Uses at most 64 images, skips hidden/system/reparse entries and files above 128 MB. Writes temporary thumbnails only in its own timestamped result folder and deletes them after measurement; retains JSON report.
- Published to NAS at C:\Users\tatsa\web-tools\thumbnail-benchmark. No production application, database, settings or cache changes.
- Validation: publish passed; local one-image smoke run completed all worker settings. This is tool verification on GIMKIM-5950X, not NAS performance evidence. A single image cannot measure worker scaling.
- Limitation: NAS SSH/WinRM ports remain filtered. User must launch the benchmark on NAS. Memory buffering separates decode from I/O but differs from the production streaming pipeline. Windows file cache and JIT warmup affect run order; results are not cold-cache measurements or IIS queue timings. First matching images are a bounded convenience sample, not a random sample.
- Pending: real NAS execution and report analysis. No CPU bottleneck conclusion from local timings.
