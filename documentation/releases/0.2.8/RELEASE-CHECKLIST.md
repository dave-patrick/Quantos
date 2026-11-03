# Quantos 0.2.8 release verification

Windows x64 · build **2026.03.11.36** · November 3, 2026

- [x] Advanced the release to 0.2.8 (numeric, for Windows updater compatibility); build revision 36.
- [x] Shared server can also listen on the local network (`install-windows-host.ps1 -LanAddress`, `-SkipTailscale`); docs no longer say Tailscale is required. The installer option was syntax-checked, not run.
- [x] Built without a built-in Google client: no executable in the package contains the owner's client ID.
- [x] Full build: 154 frontend, 336 backend and 6 client tests pass; package/ZIP checks pass. Two earlier attempts failed on timing-sensitive tests that pass alone.
- [x] Source commit `78b4a0a` pushed to main; numeric v0.2.8 tag points to it. All three executables report ProductVersion 0.2.8 plus that commit.
- [x] Windows ZIP and checksum published as a non-draft, non-prerelease latest release; GitHub asset digest matches local SHA-256. In-app updater install was not exercised.

Android and server delivery are outside this Windows-only release; Android downloads remain on 0.2.2.

Public release: https://github.com/dave-patrick/Quantos/releases/tag/v0.2.8

Windows ZIP SHA-256: `f7f38dbcc5b4eae6a6f09ef5a323dd8d94da1d5b5e98c2cc5ed05ac5c015c2c6`.
