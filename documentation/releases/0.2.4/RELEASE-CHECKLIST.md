# Quantos 0.2.4 release verification

Windows x64 · build **2026.28.10.17** · October 28, 2026

- [x] Incremented the monotonic build revision from private build 16 to public build 17.
- [x] Advanced the release to 0.2.4 for numeric Windows updater compatibility.
- [x] Owner confirmed the appearance fix locally on private build 2026.28.10.16.
- [x] Independent implementation review finished clean; browser palette fixture passed dark/light
      runtime changes, light startup, custom CSS/backgrounds, alpha/modern CSS colors and teardown.
- [x] Independent final release review: no findings. Typecheck has zero errors and nine existing warnings.
- [ ] Unmarked full build: frontend, backend/core and native tests; package/ZIP checks.
- [ ] Packaged self-check and executable version/commit checks.
- [ ] Commit, numeric v0.2.4 tag and push.
- [ ] Publish Windows ZIP and checksum; verify GitHub digests and latest-release updater metadata.
- [ ] Stage/restart the owner's test copy; verify matching executables and HTTP 200 startup.

Native maximize/restore was not manually verified. Android and server delivery are outside this
Windows-only release. No new Windows signing certificate is introduced.
