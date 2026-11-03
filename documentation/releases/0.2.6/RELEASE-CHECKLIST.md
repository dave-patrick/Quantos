# Quantos 0.2.6 release verification

Windows x64 · build **2026.03.11.33** · November 3, 2026

- [x] Advanced the release to 0.2.6 (numeric, for Windows updater compatibility); build revision 33.
- [x] Every tag used in a note is a supertag (listed with no fields until edited); server MCP errors say why; new CONNECTING-AI.md guide. The supertag change was not exercised in the real UI before release.
- [x] Built without a built-in Google client: no executable in the package contains the owner's client ID. The client stays in the git-ignored `build/google-client.json`, used only for private builds.
- [x] Full build: automated tests pass; package/ZIP checks pass.
- [x] Source commit `6106369` pushed to main; numeric v0.2.6 tag points to it. All three executables report ProductVersion 0.2.6 plus that commit.
- [x] Windows ZIP and checksum published as a non-draft, non-prerelease latest release; the GitHub asset digest matches the local SHA-256. In-app updater install was not exercised.

Android and server delivery are outside this Windows-only release; Android downloads remain on 0.2.2.

Public release: https://github.com/dave-patrick/Quantos/releases/tag/v0.2.6

Windows ZIP SHA-256: `47ac6c3dec67435fa9d6096ce0f5be425ccd10301461524444078ac45ac1509b`.
