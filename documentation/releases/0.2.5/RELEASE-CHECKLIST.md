# Quantos 0.2.5 release verification

Windows x64 · build **2026.02.11.31** · November 2, 2026

- [x] Advanced the release to 0.2.5 (numeric, for Windows updater compatibility); build revision 31.
- [x] Optional Google Tasks two-way sync (off by default). Verified on an Android phone with a real
      Google account: sign-in, list mapping, Google to Quantos and Quantos to Google. The Windows
      walkthrough was not run.
- [x] Built without a built-in Google client: the package contains no client ID or secret, so people
      bring their own. The owner's client stays in the git-ignored `build/google-client.json` and is
      used only for private builds.
- [x] Full build: 154 frontend and 335 backend/core tests pass; package/ZIP checks pass. The two
      `taskConflict` frontend tests need `NODE_OPTIONS=--no-experimental-webstorage` on Node 26.
- [x] Source commit `77ca449` pushed to main; numeric v0.2.5 tag points to it. All three executables report ProductVersion 0.2.5 plus that commit.
- [x] Windows ZIP and checksum published as a non-draft, non-prerelease latest release. Both GitHub asset digests match local SHA-256; the unauthenticated latest-release API returns v0.2.5. In-app updater install was not exercised.

Android and server delivery are outside this Windows-only release; Android downloads remain on 0.2.2.

Public release: https://github.com/dave-patrick/Quantos/releases/tag/v0.2.5

Windows ZIP SHA-256: `3e531e007d39e121961366a1804da2a2fec2cd4feca6a662c417ff93f56c51d4`.
