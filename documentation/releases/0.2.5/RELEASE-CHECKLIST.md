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
- [ ] Source commit pushed to main; numeric v0.2.5 tag points to the exact source commit.
- [ ] Windows ZIP and checksum published as a non-draft, non-prerelease latest release.

Android and server delivery are outside this Windows-only release; Android downloads remain on 0.2.2.
