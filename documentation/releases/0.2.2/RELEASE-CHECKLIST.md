# Quantos 0.2.2 release verification

Windows x64 portable and Android ARM64 Release APK, October 26, 2026. Server artifacts are not published here.
Build ID: **2026.26.10.2**, shared by this source/build set's platform/channel variants.

- [x] Unmarked release build: 101 frontend and 175 .NET tests pass.
- [x] Three self-contained executables (0.2.2.0) and required archive layout verified by build script.
- [x] Backend self-check: zero warnings/failures.
- [x] Isolated final Windows package started with sample vault, Page loaded / HTTP 200 and version 0.2.2.
- [x] Native resize smoke: 960→920 logical pixels; About shows 0.2.2 and 2026.26.10.2.
- [ ] Version commit and v0.2.2 tag pushed.
- [ ] GitHub release and uploaded package digest verified against local SHA-256.
- [x] Public ARM64 APK: v2/v3 signature verifies, certificate CN=Quantos / Atomic 22 Industries;
      versionName 0.2.2, versionCode 202, ARM64 ABI, no debuggable/test-only flag.
- [x] Matching x64 Release-mode build started in a fresh QA emulator; More › Notes and New note render.
      This is an x64 runtime smoke, not a physical install of the public ARM64 APK.
- [x] Owner phone updated in place to private Debug 0.2.2; certificate matches its previous debug APK.
      DEBUGGABLE remains set; app reports 24 default + 618 Videos notes, no sync error or conflict.

Owner Windows files deliberately remain at 0.2.1 by latest request so the final 0.2.2 can be offered
through Settings › Updates. Its client was reopened after isolated release validation.

SHA-256:

- Windows: `3cee1554487fdef0ccc15018e609c0483634331efc1e87f6a29dda12c24fc05c`
- Public ARM64 Release APK: `2be3e212548e55b854db0112b8cd40b69f546b3cb7688b42e473e2f48e4bd0ec`
- Private ARM64 Debug APK (not uploaded): `3be1f05be472bc66fb2e079de2240a2039bdfea0b4a6a5fa8aa31cfe3ff0f9e8`

Before the version bump, native input checks passed all four edges, all four corners, title-bar
movement and resizing after maximize/restore. Full historical manual checks in
../../RELEASE-CHECKLIST.md are not all repeated for this patch; in-app download/install from an older
version, overnight reminders and all multi-monitor DPI transitions remain unverified in this run.
