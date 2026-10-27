# Quantos 0.2.3 release verification

Windows x64 · build **2026.27.10.15** · October 27, 2026

- [ ] Release source reviewed and committed; v0.2.3 tag matches the built source.
- [ ] Frontend typecheck/tests, sequential backend/core tests and native client tests pass.
- [ ] Unmarked Windows package built with three self-contained executables and required ZIP layout.
- [ ] Packaged self-check passes; final package startup and served release/build identities verified.
- [ ] Published GitHub release is non-draft/non-prerelease with Windows ZIP and checksum only.
- [ ] GitHub asset digest matches local SHA-256; latest-release metadata supports the updater.
- [ ] Local staging copy refreshed/restarted from the final release package.

Feature acceptance before the release version bump covered the mounted source at 2200px, popup
editing and source-label refresh, title/reminder save failures and retry, nested prompts/links,
conflict resolution/reopening, offline read refresh dismissal, a 390px browser and the native
Windows task popup. Release review additionally reproduced and corrected partial subtask retry.
No Android APK, Android device validation or shared-server deployment is part of this release.
Broader historical manual checks are not recertified by this patch.
