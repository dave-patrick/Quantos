# Quantos 0.2.7 release verification

Windows x64 · build **2026.03.11.35** · November 3, 2026

- [x] Advanced the release to 0.2.7 (numeric, for Windows updater compatibility); build revision 35.
- [x] Help gains Connect an AI remotely, HTTP API, Security model and Storage format pages; links between guides open in Help; the guides ship in the package. Fixes the missing-file link in 0.2.6. Not opened in the real app before release.
- [x] Built without a built-in Google client: no executable in the package contains the owner's client ID.
- [x] Full build: automated tests pass; package/ZIP checks pass (the package requires CONNECTING-AI.md and API.md).
- [x] Source commit `62326da` pushed to main; numeric v0.2.7 tag points to it. All three executables report ProductVersion 0.2.7 plus that commit.
- [x] Windows ZIP and checksum published as a non-draft, non-prerelease latest release; GitHub asset digest matches local SHA-256. The first upload attempt timed out and was repeated. In-app updater install was not exercised.

Android and server delivery are outside this Windows-only release; Android downloads remain on 0.2.2.

Public release: https://github.com/dave-patrick/Quantos/releases/tag/v0.2.7

Windows ZIP SHA-256: `3d963a32d7e718ee31c97118dbcd7ebffe1e28efa1aaf52fe4003cc37b6af7bd`.
