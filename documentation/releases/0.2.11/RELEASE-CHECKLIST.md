# Quantos 0.2.11 release verification

Windows x64 and public Android ARM64 · build **2026.09.11.46** · November 9, 2026.

- [x] Version metadata aligned at 0.2.11, Android versionCode 211; build ID advanced independently.
- [x] Independent review of the complete pending integration found no substantive blockers.
- [x] Frontend: 178 tests pass; type checking has zero errors and warnings.
- [x] Backend/core: 373 tests pass, run sequentially to avoid the previously observed native sign-in timing failure under parallel execution.
- [x] Browser feature checks: note copy actions, native-picker frontend flow, tag scope/default persistence, Notes definition filtering, and folder-bridge stopped-save/cancel-start/remove flow. Bridge lifecycle/real transfer coverage is supplied by the coordinator tests; the browser bridge flow uses an isolated fixture.
- [ ] Native Windows tests, final package/ZIP/self-check, Android release metadata/signature checks.
- [ ] Public builds omit the owner's private Google client; private owner builds retain it.
- [ ] Windows owner copy staged/restarted; private debug Android update installed without uninstalling or changing its signing channel.
- [ ] Source/tag pushed; Windows/Linux/package CI checked; GitHub release and asset hashes verified.

General laptop acceptance, a full end-to-end in-app updater installation, and a new real-source folder-bridge setup are not recertified for this release. No real folder mapping or server credentials are changed by publication. The owner's one-time tag migration is local maintenance, not an automatic release migration.

Release: https://github.com/dave-patrick/Quantos/releases/tag/v0.2.11
