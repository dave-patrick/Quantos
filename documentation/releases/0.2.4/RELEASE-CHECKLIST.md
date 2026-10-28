# Quantos 0.2.4 release verification

Windows x64 · build **2026.28.10.17** · October 28, 2026

- [x] Incremented the monotonic build revision from private build 16 to public build 17.
- [x] Advanced the release to 0.2.4 for numeric Windows updater compatibility.
- [x] Owner confirmed the appearance fix locally on private build 2026.28.10.16.
- [x] Independent implementation review finished clean; browser palette fixture passed dark/light
      runtime changes, light startup, custom CSS/backgrounds, alpha/modern CSS colors and teardown.
- [x] Independent final release review: no findings. Typecheck has zero errors and nine existing warnings.
- [x] Unmarked full build: 124 frontend, 242 backend/core and five native tests pass; package/ZIP checks pass.
- [x] Packaged self-check passes with zero warnings. All three executables report FileVersion 0.2.4.0,
      ProductVersion 0.2.4 plus source commit `960accd87cf058432cb7a80f93ec9a43d40b2738`.
- [x] Source commit pushed to main; numeric v0.2.4 tag points to the exact source commit.
- [x] Windows ZIP and checksum published as a non-draft/non-prerelease latest release. Both GitHub
      asset digests match local SHA-256. Unauthenticated latest-release API returns v0.2.4 with the
      expected Windows asset/digest; a full unauthenticated ZIP download matches its published digest.
      In-app updater download/install on the other machine was not exercised.
- [x] Owner test copy replaced/restarted; all three installed executable hashes match the package.
      Startup reports Page loaded / HTTP 200, and the actual served entry bundle contains build
      2026.28.10.17 without a staging mark.

Public release: https://github.com/dave-patrick/Quantos/releases/tag/v0.2.4

Windows ZIP SHA-256: `99223235f6932dfa033c74b259d8e2dd5566d3f220bb8a371581521adfe3ea62`.

Native maximize/restore was not manually verified. Android and server delivery are outside this
Windows-only release. No new Windows signing certificate is introduced.
