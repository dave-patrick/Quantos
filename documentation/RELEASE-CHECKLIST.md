# Release 1.0 checklist

Work through this for every release build. Tick items in a copy of this file kept with the release.

## Build

- [ ] `pwsh build/build.ps1` completes; all automated tests pass (it stops on a failure).
- [ ] `publish/Quantos/application/` holds exactly `Quantos.exe`, `Quantos.Backend.exe`, `quantos-cli.exe`.
- [ ] Version number set (`-Version`) and matches the user guide and release notes.
- [ ] The zip unpacks to `Quantos\` with the user guide, troubleshooting guide, CLI reference, plugin guide,
      example vault and the three `.cmd` launchers.

## Automated acceptance tests (BUILD-PLAN section 13)

Covered by `dotnet test` (see the test names):

- Storage and recovery 1–10: `StorageAcceptanceTests`.
- Editing and sync 2–6: `TaskAcceptanceTests` (capture, watcher, task lines), `StorageAcceptanceTests`
  (unknown YAML), `RelationTests` (advanced content after outside edits).
- Tasks 1–5: `TaskAcceptanceTests`.
- Permissions and security 1–8: `BackendSecurityTests`, `ExtensibilityTests`.

## Manual checks on the target laptop (no admin rights)

Packaging
- [ ] Unzip into Documents; run `Self-check.cmd`. Record the report. No `[FAIL]` lines, or a
      documented fallback (browser-only, or a signed build from IT).
- [ ] `Start Quantos.cmd` launches without any install prompt.
- [ ] Choose a vault outside the application folder; then try one inside OneDrive and confirm the warning.
- [ ] Move the whole `Quantos\` folder elsewhere; settings and the vault still open.
- [ ] Close the window: it minimizes. Reopen from the taskbar or tray.
- [ ] Exit from the tray: open notes save first; the backend process ends.
- [ ] End `Quantos.Backend.exe` in Task Manager while typing; within seconds the window is back and the text is still there.
- [ ] Upgrade: unzip the new version over `application\` only; settings, vault and history are kept.

Interface
- [ ] Open an eleventh tab in a pane; the least recently used unpinned tab closes after saving.
- [ ] Pin all ten tabs and open another: you're asked to unpin one.
- [ ] Switch workspaces; tabs, splits and scroll positions come back.
- [ ] Keep today's note open past midnight; the stream moves to the new day without losing text.
- [ ] Sleep the laptop with a reminder due; on wake the reminder shows once.
- [ ] Change the time zone; new entries use the new zone.
- [ ] Navigate a view's table with the keyboard only (arrows, Enter, Tab, Escape).
- [ ] Shift-click a link; it opens in the other pane.
- [ ] In the browser: no terminal; capture shortcuts, scratchpad and notifications labelled *Requires Windows client*.

Editing and sync
- [ ] Edit the same note in the browser and the Windows client; both stay correct.
- [ ] Edit a note in Notepad while it has unsaved changes in Quantos; the comparison appears and both
      versions are in history.

Security
- [ ] Ask for a terminal; the native confirmation appears first.
- [ ] Click Run on a PowerShell block; the exact code and folder are shown before it runs.
- [ ] `quantos-cli mcp --agent test` can read but not create until granted.
- [ ] Turn on safe mode; plugins stop.
- [ ] Permanently delete from the trash; approval is required in the Windows client.

## Documents

- [ ] USER-GUIDE.md (including manual backup), TROUBLESHOOTING.md, CLI-REFERENCE.md current.
- [ ] STORAGE-SPEC.md, SECURITY-MODEL.md, API.md current for any format or API change.
- [ ] PERFORMANCE.md re-measured if storage code changed.
- [ ] BUILD-PLAN.md decision log updated with any new decisions.
