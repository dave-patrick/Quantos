# Quantos 0.2.4

Build **2026.28.10.17** · October 28, 2026 · Windows x64

The Windows resize border, title bar and window buttons now follow the app's light, dark or
custom appearance immediately. Previously, switching to light mode left these surfaces charcoal.
The frame also uses the selected appearance when reopening the app.

The release advances to 0.2.4 because existing Windows updaters compare numeric release versions,
not build IDs. This package contains the portable Windows ZIP and SHA-256 checksum. Download and
extract the ZIP on the other Windows machine, then run `Quantos/application/Quantos.exe`.
Existing installations can use Settings › Updates after publication.

The owner confirmed the appearance fix locally on private build 2026.28.10.16. Browser palette and
native regression checks passed. Manual native maximize/restore was not verified for this fix.
Windows executables retain the project's existing unsigned distribution; no Authenticode signing
is added. Public Android remains 0.2.2; no APK or server deployment is included.
