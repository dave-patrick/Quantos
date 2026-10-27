# Quantos 0.2.3 release verification

Windows x64 · build **2026.27.10.15** · October 27, 2026

- [x] Source commit `05dada60d7fae47de8d291abe2f6565da81885d8`; v0.2.3 points to that commit.
- [x] 124 frontend / 242 backend-core / four native tests pass; typecheck has zero errors and nine
      existing warnings. An initial CLI pairing timeout under memory pressure passed its focused
      retry and the subsequent full 242-test run. Independent release review finished clean.
- [x] Unmarked package contains three self-contained executables, each FileVersion 0.2.3.0 and
      ProductVersion 0.2.3 plus the exact source commit. Build script verified required ZIP entries.
- [x] Packaged self-check passes with zero warnings. Final staging startup reports Page loaded /
      HTTP 200; served assets contain build 2026.27.10.15 and no staging marker.
- [x] Public GitHub release is non-draft/non-prerelease with Windows ZIP and checksum only.
- [x] Both GitHub asset digests match local SHA-256. Unauthenticated latest-release API returns
      v0.2.3 with the Windows filename/digest required by the updater. A full live updater download
      and install was not repeated for this release.
- [x] Owner staging copy refreshed/restarted; all three installed executable hashes match the package.

Windows ZIP SHA-256: `edcdfabd131de5a780a772f7e4b2f81cd52e9a4b19e8db6731104077c3035894`.
Public release: https://github.com/dave-patrick/Quantos/releases/tag/v0.2.3

Feature acceptance before the release version bump covered the mounted source at 2200px, popup
editing and source-label refresh, title/reminder save failures and retry, nested prompts/links,
conflict resolution/reopening, offline read refresh dismissal, a 390px browser and the native
Windows task popup. Release review additionally reproduced and corrected partial subtask retry.
No Android APK, Android device validation or shared-server deployment is part of this release.
Broader historical manual checks are not recertified by this patch.
