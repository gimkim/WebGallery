# Camera RAW previews

Gallery uses ExifTool (https://exiftool.org/) to extract the largest supported embedded JPEG among JpgFromRaw, PreviewImage and ThumbnailImage. It never demosaics RAW. When no embedded JPEG exists, the thumbnail request returns422; the source remains downloadable. Viewer uses the embedded JPEG, while Download always returns the untouched RAW.

Windows: install the official64-bit ExifTool archive outside the web application and keep exiftool_files alongside the renamed exiftool.exe. Set Gallery:ExifToolPath to its absolute executable path and grant the IIS identity read/execute access. Do not put tool files or production configuration in source control. Default is exiftool on PATH.

Docker: the image installs libimage-exiftool-perl and Docker config uses /usr/bin/exiftool. Rebuild the image to install it. Linux build was validated separately; Docker runtime needs its own deployment test.

Extraction is limited to two processes,40 seconds per process,96MiB JSON output and120MP embedded images. Cancellation kills the process tree. RAW originals are passed as literal arguments, never shell commands; no tool config is loaded and no metadata is written. Missing tools/preview failures do not mark ThumbService offline. Both local and remote queues extract only on cache misses; remote receives JPEG bytes. Current RAW fingerprint/owner-independent thumbnail cache rules remain unchanged. Index upgrades classify previously indexed RAW files as images; folder refresh also updates classification.
