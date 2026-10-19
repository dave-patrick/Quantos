# Release checklist

Work through this for every release build. Tick items in a copy of this file kept with the release.

## Build

- [ ] `pwsh build/build.ps1` completes; all automated tests pass (it stops on a failure).
- [ ] `publish/Quantos/application/` holds exactly `Quantos.exe`, `Quantos.Backend.exe`, `quantos-cli.exe`.
- [ ] Version number set (`-Version`) and matches the user guide and release notes.
- [ ] The zip unpacks to `Quantos\` with the user guide, troubleshooting guide, CLI reference, plugin guide,
      agent guide, example vault and the three `.cmd` launchers.
- [ ] `publish/Quantos/application/Quantos.Backend.exe --selfcheck` reports no failures.

## Automated acceptance tests (BUILD-PLAN section 13)

Covered by `dotnet test` (see the test names):

- Storage and recovery 1–10: `StorageAcceptanceTests`.
- Editing and sync 2–6: `TaskAcceptanceTests` (capture, watcher, task lines), `StorageAcceptanceTests`
  (unknown YAML), `RelationTests` (advanced content after outside edits).
- Tasks 1–5: `TaskAcceptanceTests`.
- Permissions and security 1–8: `BackendSecurityTests`, `ExtensibilityTests`.
- Linked folders: `LinkedFolderTests` (in place, identity, conflicts, backups, tasks).
- Backups: `BackupTests` (completeness, damage, retention, restore, linked folders).
- Agent tools: `McpTests` (the tool list and the settings an agent needs).
- Editor: `frontend/tests/editor.test.ts` (blocks, slash commands).

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
- [ ] *Settings › Updates* offers the new release, downloads it, restarts, and the new version is running.

Interface
- [ ] Open an eleventh tab in a pane; the least recently used unpinned tab closes after saving.
- [ ] Pin all ten tabs and open another: you're asked to unpin one.
- [ ] Switch workspaces; tabs, splits and scroll positions come back.
- [ ] The right sidebar splits into two panels; the divider drags and the layout is remembered.
- [ ] Keep today's note open past midnight; the stream moves to the new day without losing text.
- [ ] Sleep the laptop with a reminder due; on wake the reminder shows once.
- [ ] Change the time zone; new entries use the new zone.
- [ ] Navigate a view's table with the keyboard only (arrows, Enter, Tab, Escape).
- [ ] Shift-click a link; it opens in the other pane.
- [ ] In the browser: no terminal; capture shortcuts, scratchpad and notifications labelled *Requires Windows client*.

Writing
- [ ] Type `/` on a line: the list appears; a word narrows it; Enter inserts; Escape closes it.
- [ ] Right-click a block: copy, duplicate, move, delete and "turn into" all work.
- [ ] Right-click a task in the task list: status, due date, priority, open and *Move to trash*.
- [ ] Hover a task: the × at the end of the row deletes it, and the toast can undo it.
- [ ] Type `- [ ] Something` in a note: it becomes a task, ticked in either place, in step.
- [ ] *Settings › Appearance*: a custom style applies on its own, *None* clears it, and a deleted
      style file can still be cleared.

Linked folders
- [ ] *Link a folder…* takes a path, and the folder appears in its own group in the workspace menu.
- [ ] A note in that folder opens and edits in place; the other app sees the change.
- [ ] Pictures next to a note show up; a link to the note next door opens it.
- [ ] The other app rewrites a note: Quantos notices, keeps the note's identity, and puts the id back.
- [ ] Both apps write at once: the newest wins and the other version is in history.
- [ ] *Unlink* leaves the files exactly as they are.
- [ ] *Settings › Backups*: linked folders are listed and can be included; a backup holds their notes;
      restoring a backup points at its own copies.
- [ ] *Vault diagnostics* names a linked folder that has gone, and a task bound to a note that is gone.

Agents
- [ ] *Settings › AI agents* shows the program and the settings for each agent; copying one works.
- [ ] `quantos-cli mcp --agent test` can read but not create until granted.
- [ ] `quantos-cli tools --format anthropic` prints the nine tools; `quantos-cli call search_notes` runs one.
- [ ] An agent writing to a linked folder writes to the other app's file, as it should.

Security
- [ ] Ask for a terminal; the native confirmation appears first.
- [ ] Click Run on a PowerShell block; the exact code and folder are shown before it runs.
- [ ] Turn on safe mode; plugins stop.
- [ ] Permanently delete from the trash; approval is required in the Windows client.

## Documents

- [ ] USER-GUIDE.md (including manual backup), TROUBLESHOOTING.md, CLI-REFERENCE.md, AGENT-ADAPTERS.md current.
- [ ] STORAGE-SPEC.md, SECURITY-MODEL.md, API.md current for any format or API change.
- [ ] PERFORMANCE.md re-measured if storage code changed.
- [ ] BUILD-PLAN.md decision log updated with any new decisions.
- [ ] PROJECT-STATUS.md and CHANGELOG.md say what shipped and what is still unverified.
