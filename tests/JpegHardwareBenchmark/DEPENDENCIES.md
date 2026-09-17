# Native dependency provenance

- Official libjpeg-turbo 3.2.0 Windows VC x64 installer, extracted with 7-Zip without installation.
- URL: https://github.com/libjpeg-turbo/libjpeg-turbo/releases/download/3.2.0/libjpeg-turbo-3.2.0-vc-x64.exe
- Installer SHA-256 verified against GitHub release asset digest: `662761d8ba8dae04aec74023ebaeceb856c2b56b9b59cfd180759d26300dda42`.
- Copied only bin/turbojpeg.dll, bin/jpegtran.exe, bin/jpeg62.dll, doc/LICENSE.md and doc/README.ijg into the standalone publish folder. No machine-wide installation, registry changes or driver updates.
- FFmpeg remains external, using the existing NAS executable; each run records version/build options, decoder/filter listings and GPU driver inventory.
- Source uses the documented stable TurboJPEG 2.x API. Native binaries and results are excluded from source control by the publish directory rule.
