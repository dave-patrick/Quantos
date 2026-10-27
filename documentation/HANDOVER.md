# Handover: working on Quantos with its owner

What the previous agent learned about working on this project that isn't in the code or the plan.
Read [PROJECT-STATUS.md](PROJECT-STATUS.md) for the technical picture; this file is about the owner, the
open threads and the machine.

## The owner and how they work

- **The owner uses Quantos daily on Windows** and reports what they find in short messages
  ("convert to task doesnt seem to do anything", "startup is pretty slow"). Reproduce first, find the
  cause, fix it, then explain the cause in a sentence or two. Messages are informal and often have typos;
  read for intent.
- **Requests arrive while you're working.** Acknowledge them in a line, finish (and commit) the current
  piece, then take them in order. Don't drop any; keep a running list.
- **Keep going without being told.** The standing instruction is to carry on building and not stop
  for confirmation on ordinary work. Commit and push to `main` as each piece is done, with a clear
  commit message describing the user-visible change. No attribution lines in commits.
- **Ask before:** cutting a release, publishing anything, anything costing money, or starting a roadmap
  item without a scope decision. When a release is warranted, ask ("Do you want this as 0.1.5?"); when the
  owner says yes, follow PROJECT-STATUS.md section 8.
- **Reports:** short and concrete. What changed (by what the owner will see), how it was checked, what
  couldn't be checked, and the one question if there is one. Use plain English, no jargon. When telling
  the owner how to do something in the app, give the exact place (*Settings › Shortcuts*) and keys.
- **Explain cause and effect** when something was broken: what was actually wrong, not just that it's
  fixed. Admit when something couldn't be reproduced or tested.
- **Every new test build is staged into the owner's test copy of the app, and the app is restarted.**
  The owner currently tests from `C:\Users\davem\Apps\Quantos\Quantos\application`; the vault stays
  at `C:\Users\davem\Quantos\Vault`. `pwsh tools/dev/stage.ps1` closes the
  running client, replaces the three programs in that `application` folder with the ones from
  `publish\Quantos\application`, refreshes the documents, and starts `Quantos.exe` again. The owner
  has asked for this to be done for each new release without asking again. Two things to know:
  rename the old files to `*.old` before copying the new ones over, because a running `.exe` can be
  renamed but not overwritten (this is what the updater does); and a hard-killed client leaves its
  WebView2 processes behind, which lock the browser profile and make the next start fail with
  `COMException 0x800700AA` ("The requested resource is in use") — the script closes the client
  properly and, if that happens anyway, moves `client-data\webview2` aside and starts again on a
  fresh profile. The client also does this for itself: when the browser engine reports that the
  profile is in use, it starts on a new folder (`client-data\webview2-<time>`) and carries on, so a
  leftover browser process can never stop the app from opening. Don't kill the `quantos-cli.exe`
  processes; they are the MCP server. Afterwards read the `Startup:` line in
  `client-data\logs\client-<date>.log` to confirm it came up.
- **Workspace locations:** git repositories live in `C:\repos\` (this one is `C:\repos\Quantos`).
  Working files that aren't repository code (notes, drafts, exports, research) go in
  `D:\My Drive\Dev Projects\<project>\`.
- **Website:** only `site/` is published (GitHub Pages). Never publish private vault or workspace data.

## Open threads (as of 2026-10-26; latest release 0.2.2, build 2026.26.10.2)

- **Security review remediation (2026-10-26, source/build set 2026.26.10.3).** First batch fixes
  Windows native origin checks/full script review, calendar traversal and attachment boundaries
  for serving/trashing/adding/export. See SECURITY-REMEDIATION.md for regression coverage and the
  remaining priorities. Agent/native credential separation and the other audited findings remain
  open. No release or replacement of the owner's installed copy is implied by these source changes.
  Validation: 204 backend/core tests passed sequentially, three native dialog tests passed, and
  independent review finished clean. Parallel runs hit existing discovery/SSE timeout checks;
  see the remediation record for the exact limits and deferred WebView2/device acceptance.

- **Public and private release channels (2026-10-26).** The owner explicitly authorized a public
  production Android APK while keeping their phone on private debug builds. This release set is
  Windows/public Android 0.2.2, build `2026.26.10.2`. Android versionCode is 202 (Lohe's release-only
  formula). The stable key is private under `C:\Users\davem\Projects\Quantos-signing`; preserve it for
  future public updates and never commit/upload its files. Release builds require distinct store/key
  password file paths because apksigner consumes each line; the build script forces signing and
  verifies the final APK before export. ASP.NET managed runtime pin is now 10.0.14.
  The owner requested the Lohe naming convention on all projects: global and C:\repos AGENTS guidance
  now apply it on each project's next build. See `build/VERSIONING.md` and `build/next-build.ps1`.
  Earlier 0.2.1 candidates were never public. The owner Windows copy stays on 0.2.1 so they can use
  Settings › Updates for 0.2.2; this latest request supersedes automatic owner staging for this release.
  Final 0.2.2 was launched against a separate example vault and passed startup HTTP 200, About identity
  and a real right-edge drag (960→920 logical pixels), then the owner candidate was reopened.
  Public APK and private phone validation/publication proofs are recorded in the release checklist.
  Release `v0.2.2` is public at version commit `5a619c5`, with Windows ZIP, ARM64 Release APK and
  checksums only. GitHub digests match local files. The live updater test fetched/verified/unpacked
  the published ZIP; the owner's installed 0.2.1 reports 0.2.2 available with no error. Leave their
  final install/restart click to them. Phone private Debug 0.2.2 retains 24 default + 618 Videos notes,
  with no sync error/conflict; the public APK was not installed on it.

- **Windows edge resize (2026-10-26, staged).** Reproduced a right-edge drag leaving the window
  unchanged: WebView2/child controls covered the form's custom hit zones. MainForm now reserves
  an 8-logical-pixel dark outer strip for the form, updates it on DPI/state changes, and removes
  the inset when maximized. Existing custom capture/resize handling owns all eight hit zones.
  Windows package/archive validation passed and the owner's copy was staged/restarted (Page loaded,
  HTTP 200). Real mouse drags passed left/right/top/bottom, all four corners, title-bar movement,
  and another right-edge resize after maximize/restore. Move a window clear of the taskbar before
  testing a covered bottom edge; use the painted corner rather than its rounded transparent tip.
  Evidence: ignored `.dev/resize-native-results.json`, `resize-package-build.log`, `resize-stage.log`.
  No Core/frontend changes; existing automated suites were not rerun for this native-only fix.

- **Google Drive server folder bridge (2026-10-25, private deployment).** The owner's
  `G:\My Drive\Thymer\Videos` on T580 feeds shared workspace `videos` read/write through
  `Quantos.FolderBridge`. Other systems continue using the originals. Only Markdown transfers;
  source JSON/text/application configuration stays local. Three-way hashes, conditional writes,
  conflicts/recovery outside the source, empty/unavailable-folder guards and large-removal guards
  protect outside edits. Clean renames preserve note identities. The existing local Windows `videos`
  link was removed through the API, leaving `D:\My Drive\Thymer\Videos` intact; Windows now uses the
  shared copy. A source ZIP backup covered all 617 files present when taken; subsequent live import
  uploaded 618 Markdown files as outside systems remained active. Windows backup: 690 files,
  verification reported zero problems. Server volume snapshots are protected under the server
  runtime backups folder. Server image is `quantos-server:workspace-scope-440a8ed`; all 175 .NET tests
  pass. Its watcher keeps the live index updated; MCP grants and server identity survive the update.
  The phone is updated and renders **618 Videos notes plus 24 default notes**, with zero sync conflicts.
  A temporary probe verified Quantos→original, outside edit→Quantos, Android→original and MCP read;
  it was moved to Trash and removed from the active source by the bridge.
  Cache v11 fixes imported notes being indexed under their old `video-processing` frontmatter rather
  than the physical `videos` folder; original frontmatter remains unchanged. More has a permanent
  first-row Notes action for the compact list, independent of pinned shortcuts. Native 360/411 review
  returned ship for that narrow access change. The directly tracked windowless logon task is installed.
  On October 26, desktop sign-in restored the Google Drive mount and automatically started the task.
  Controlled stop/restart verified zero workers after stop and exactly one after restart. Docker
  Desktop needed starting after sign-in (its auto-start setting is already enabled). Server health
  returned HTTP 200; the fresh 07:16 sync completed with zero transfers and zero conflicts across
  all 618 source files. Missing source folders are never recreated.
  Windows staging uses Shell.Application so the client survives the executor job ending.
  T580 source is canonical at `C:\repos\Quantos`. Its Scoop Git launcher is broken: deployment Git
  used the SDK container and verified Git bundles kept in `C:\repos\_checkout-recovery`. Public MCR
  pulls used an isolated Docker config with an empty MCR auth entry; normal credentials were preserved.

- **Custom phone navigation (2026-10-25, unreleased).** The owner wants Daily notes, Tasks, quick
  Capture and Today first; these are now the defaults, followed by permanent More. In More › Settings,
  choose Phone navigation to select one to four shortcuts, remove them, reorder with left/right arrows
  or restore defaults. Capture is an action that leaves the current page in place. Choices live in
  device `quantos.prefs`, never in shared vault content; tablet navigation is unchanged. Stored
  preferences are validated for known destinations, duplicates, maximum count and empty/invalid
  fallback. The full build passed 101 frontend / 167 .NET tests, archive validation and self-check
  (zero self-check warnings); Windows was staged/restarted with Page loaded / HTTP 200. Both APKs
  built with zero warnings/errors. Native 360px testing covered add/remove/reorder/reset, restart
  persistence, 1.3 text scale, 48px controls and a real tap on bottom Capture retaining Tasks.
  Tablet split workspace remained expanded. The ARM64 update is installed on the owner's SM-S948U
  with their chosen default order, 24 nodes, no sync errors/conflicts and server MCP writes preserved.
  Native evidence is in ignored `.impeccable/review/phone-navigation/` (including the actual 411px phone).
  Independent review's sole 8px touch-spacing fix was scored resolved, disposition ship for that fix.
  After the user reported missing notes, the phone was found in empty `video-processing`; selecting
  `default` restored the existing 24-note browser. A transient Tailscale DNS error cleared; authenticated
  sync returned HTTP 200, zero transfers/conflicts. Follow-up requested work: link T580's
  `G:\My Drive\Thymer\Videos` (618 Markdown files) read/write through the shared server. Its contents
  are not shared yet; the Windows `videos` link currently uses this PC's `D:\My Drive\Thymer\Videos`.

- **Phone navigation and panel close (2026-10-25, unreleased).** The last right-sidebar panel's X
  previously returned without doing anything; it now hides the sidebar while preserving its panel
  configuration. Compact Android uses Notes, Tasks, Today and More, one visible page, and a separate
  Details screen. More retains advanced views, open pages, workspace switching and Settings. The editor
  stays mounted behind More/Details. Phone task options and secondary note actions are tucked away;
  full-text search and Capture remain in the header. Tablets keep the expanded split workspace.
  Native x64 checks covered 360px width, real IME/caret visibility, 1.3 text scale, task checkbox touch,
  More/Back, Details close, body-only search and the tablet sidebar close/reopen. Independent review
  passed the surface, then scored full-text search and touch Capture copy resolved (ship).
  The ARM64 update was installed over the owner's SM-S948U app, preserving 24 nodes, production sync
  with zero errors/conflicts, and enabled MCP writes. A real native tap closed Details successfully.
  Both APK builds had zero warnings/errors; the Windows build passed 97 frontend / 167 .NET tests,
  archive checks and self-check (zero warnings), and was staged/restarted with Page loaded / HTTP 200.
  Native revision evidence is in ignored
  `.impeccable/review/phone-revision/`. This remains a private test build, not a public release.

- **Android / shared server / frontend MCP permission (2026-10-25, unreleased).** The owner's approved
  scope is recorded at the top of BUILD-PLAN. The Android app shares the full existing backend and
  frontend rather than implementing a reduced note app. Build with `pwsh build/android.ps1`; ARM64
  and x64 APKs are in ignored `publish/android/`, debug-signed for testing. Server setup and migration
  are in SERVER.md. T580 production is `https://t580.tail7a64f4.ts.net:8445`; isolated QA is 8446.
  The production volume is `quantos-data`; test data is `quantos-test-data`. Keys remain in protected
  runtime files, never in the repo. All 167 .NET tests pass. Android phone/tablet x64 runtime tests
  verified local edits/restart, actual offline edit/reconnect to Windows, server MCP write on/off and
  native Copy, tablet splits, native PDF page rendering, a valid 13-file portable ZIP export, import
  into a separate vault preserving the original, and approved Android shell execution. On 2026-10-25,
  the ARM64 APK was installed on the owner's SM-S948U phone and connected to production. All 24 nodes
  synced with zero conflicts/errors; app restart preserved the connection and the phone confirmed
  enabled server MCP write access. Tailscale's included-app VPN list must include Quantos; the new
  app initially could not resolve the private server hostname until it was included. Microphone/
  notification delivery, native printing and broader physical-device acceptance remain separate checks.
  No public APK release was made.
  Emulator 37.2.12 software GPU modes crashed the whole VM on this Windows/Arc host; `-gpu host`
  works. Run one emulator at a time to avoid memory pressure. Native evidence is in ignored
  `.impeccable/review/`, not browser emulation. Independent review requested an IME caret-visibility
  fix; the reviewer scored it resolved, with disposition **ship for the scored fix**. The documenter
  verified the extension against incumbent assets/tokens; marketing-artifact drift was preserved.
  The final Windows build passed 97 frontend / 167 .NET tests, archive checks and self-check with
  zero self-check warnings, and was staged/restarted successfully (Page loaded, HTTP 200). Its first
  production connection followed a verified 29-file backup: 28 portable files uploaded, zero
  conflicts. Production MCP write access is enabled and persisted across a real server restart;
  authenticated MCP initialize/tools-list returned all nine tools. The owner can revoke writes in
  Settings › MCP. The synthetic QA container is stopped; its separate volume is retained.

- **Released 0.2.0** (tag `v0.2.0`, Windows x64 zip, SHA-256
  `3fafdba7f3b0f2606855155e22814b771e333baf2479809578b381f95771ce1c`). The release includes the
  split workspace, persistent multi-panel sidebar, single-line Notes list with remembered Preview
  toggle, compact My Day cards and task priority colors, frameless resizing and restore/reopen drawing
  fixes, editor trash action, and version label correction. The release build passed 97 frontend and
  151 .NET tests plus the package self-check. The package was published, staged into the owner's
  application folder, and the relaunched client loaded with HTTP 200.
- **Top-bar layout and window refinements (included in 0.2.0).** The header row is 60px on desktop and mobile; the wordmark is
  23.75px. Daily stream, My Day and Tasks are removed from the default toolbar and filtered from
  older `toolbar.json` settings and *Settings › Toolbar*. Their commands and views remain available
  elsewhere. The dark background is a lighter neutral gray (`#2a2b2d`), with darker tinted gray
  cards (`#202123`); the native window bar matches the page background. Frontend check and all 97
  frontend tests pass; all 151 .NET tests pass.
  The latest adjustment restores an 8px gap below the header to match card spacing, aligns the right
  sidebar card to the same gutter as the center pane, and uses custom edge and corner resizing with
  no native sizing frame. The updated source passed
  `svelte-check` (0 errors, 14 warnings), all 97 frontend tests and 151 .NET tests; the Windows x64
  build and package validation succeeded. Its three application files were staged to
  `C:\Users\davem\Apps\Quantos\Quantos\application`; previous executables are preserved
  as backups. The prior process was closed, the updated copy was relaunched, and its log confirms the
  page loaded successfully with HTTP 200. The sidebar is now one subtly lighter card with unboxed
  rows and a wider left gutter. Sidebar toggles now use directional panel icons, scrollbars are hidden
  without disabling scrolling, and the titlebar chrome matches the neutral gray app background.
  Border resizing is available along every edge, including the top edge, and at all four corners.
  Split panes now have no visible tab-name strip; the active view has only a small close button at
  the upper-right. Use `Ctrl+Shift+P`, then *Next tab* or *Previous tab* to switch tabs. The shell no
  longer has a redundant spacer row; the main view and navigation align vertically. Split panes are
  separate neutral cards with an 8px gutter and no resting divider line; the active pane uses a quiet
  neutral outline and surface tint. The right sidebar has no full-height divider and uses the same
  neutral card surface. Header sidebar toggle icons stay unboxed in active and hover states, and the extra blank space below the header was removed.
  The native sizing frame is removed to prevent DWM from drawing a silver edge; custom code preserves
  all-edge resizing. The package
  script now stops if Vite fails, preventing an old frontend from slipping into a new package.
  `svelte-check` reports 0 errors, all 97 frontend tests pass, and all 151 .NET tests pass. The latest
  Windows package was rebuilt and staged; the old client closed, the updated copy relaunched, and its
  page loaded successfully (HTTP 200). At that point, no release had been published.
  Maximize/restore and tray hide/reopen could leave the frameless window's drawn client area out of
  alignment with edge hit targets. Build `fe96398` keeps the OS-proposed client rectangle through
  `WM_NCCALCSIZE` and forces a deferred native frame/layout refresh after state changes and re-show.
  The Windows x64 package validated and was staged; the old process closed and the new client loaded
  successfully (HTTP 200). At that point, no release had been published.
  In build `9fcce4c`, the Notes List uses single-line rows and a Preview toggle that remembers the
  user's choice. Task priority strips are red for P1, orange for P2, green for P3 and blue for P4.
  Frontend check reports 0 errors and 10 warnings; all 97 frontend tests and 151 .NET tests pass.
  The Windows x64 package validated, the old app closed, and the new client relaunched successfully
  with page load HTTP 200.
  The silver top edge appeared again because the client re-applied a DWM border color on activation
  and window-position changes. Build `86f7a94` sets DWM border and caption colors to `DWMWA_COLOR_NONE`
  and continues to suppress non-client painting. The Windows x64 package passed validation; 97
  frontend and 151 .NET tests passed; the old client closed, and the updated app relaunched with HTTP 200.
  The latest UI polish removes the duplicate heading from the right Tasks panel, uses a single compact
  quick-add field and flat task rows, puts Add a panel inside the last card, restores an 8px inset at
  the far right edge, and rearranges the header with New beside the workspace, Split and Quick Capture
  before Search. One Toolbar setting toggles labels on New, Split, Quick Capture and Search together.
  The right sidebar resize grip is now a hover/focus-only 8px divider with pointer capture and
  adjacent-card height adjustment; keyboard arrows also resize the focused pair. Its toggle glyph
  direction is reversed. The silver top edge persisted with DWM frame color overrides; the client
  now omits the native sizing style and resizes through its own edge hit zones.

- **Window chrome and build mark (included in 0.2.0).** The client draws its
  own caption buttons (a thin dash, a square, a cross, with rounded hover patches and a red close)
  and the app's icon; edge resizing is handled by the client to avoid the silver native frame, so
  the Windows shadow may differ from the framed version. The daily stream now opens with only today
  expanded. A staging copy carries a build mark next to the version on the splash and setup screens
  (`build/build.ps1 -Mark`); a released build has none. The arrows on headings are easier to find now
  (bigger, brighter, and brighter still on the hovered line). Worth asking the owner to look at: the
  rounded corners, the caption buttons, the daily stream opening with only today, and the mark next to
  the version. **A heading with nothing under it gets no arrow**, because there is nothing to fold —
  that is why the owner's daily note (`# this is a test`, the last line) shows none.
- **Editor: collapsible headings and block selection (released in 0.1.6).** Headings now carry
  their own arrow and fold by level, and blocks can be selected together (`Ctrl`-click,
  `Ctrl`-`Shift`-click), dragged to a new place, and worked on from the right-click menu. The
  *Collapsible headings* setting now really turns the arrows off; the Markdown language's own
  heading folding is dropped in `lib/editor/setup.ts` so that ours decides. The slash menu has
  *Collapsible heading 1/2/3* (writes `## [-] Title`, folded when the note opens, because the
  mark is in the file) and *Fold the heading here* (folds the nearest heading, writes nothing).
  A heading with nothing under it gets no arrow, because there is nothing to fold. Worth asking
  the owner to try: folding a section, dragging two blocks, and the right-click menu on a
  selection.
- **Released 0.1.5.3** (tag `v0.1.5.3`): fixes three faults in the 0.1.5.2 speed work — a query read
  only the first few thousand notes, so a filtered view left the rest out and the count of results was
  short; the notes browser asked the cache for a column that isn't there, so it showed no excerpts
  and its text search found nothing; and a note's parsed frontmatter was kept after a change, so
  reading a field could give the value from before the change. All three are fixed and covered by
  tests. **Ask the owner to update to 0.1.5.3** and to check that the notes browser shows the first
  lines of each note again.
- **Released 0.1.5.2** (tag `v0.1.5.2`): a patch release of speed fixes — a note's frontmatter is
  parsed once instead of on every lookup, saving no longer reads the note back off disk, the editor
  stops rebuilding on every keystroke, the cache's queries are bounded, and saving a file another
  program has locked gives up instead of retrying for ever.
- **The first published asset for v0.1.5.2 was packed by hand and was the wrong shape** (no
  `Quantos\` wrapper, the programs split into loose DLLs, and no documents or example vault), so the
  updater refused it and nobody could update to it. It has been replaced with one built by
  `build/build.ps1`, which now checks its own package before it is published. **The owner updated
  0.1.5.1 → 0.1.5.2 through *Settings › Updates* and the new version started**, so the updater's
  install-and-restart step has now worked on a real machine.
- **Released 0.1.5** (tag `v0.1.5`, zip `Quantos-0.1.5-win-x64.zip`, SHA-256
  `ac7c131a6af2974f71db4d82f913c6059469c05ce7e2db8783cce065a40525fe`): the split right sidebar,
  linked folders, the MCP settings page, slash commands, the block right-click menu, and
  custom styles applied with buttons.
- **Linked folders (released, not yet used by the owner):** the owner keeps notes in a separate
  Markdown editor and wants both to work on the same files. *Link a folder…* in the workspace menu
  does it. Ask which editor they use and whether the two behave as expected on their real notes.
- **First in-app update:** done — the owner updated 0.1.5.1 → 0.1.5.2 on their machine and the new
  version started. The Windows client's install-and-restart step (`Updater.cs`,
  `MainForm.ApplyUpdateAsync`) has now worked for real. If a later update fails, the client log
  `client-data\logs\client-<date>.log` says why; the old program files are kept as `*.old` until a new
  version starts. The updater only accepts a package laid out as `Quantos\application\...`
  (`UpdateService.Unpack`), so the package must come from `build/build.ps1` and never be zipped by
  hand.
- **Convert to task (`Ctrl+Alt+T`):** reported not working; the cause found was the pop-up scratchpad
  window not handling app shortcuts (fixed in 0.1.4). If it still fails in the main window on the owner's
  PC, another program may own `Ctrl+Alt+T`; the command palette entry would still work.
- **Quick capture into the open daily note:** fixed in 0.1.2 and confirmed by the owner.
- **Startup speed:** improved in 0.1.3 (uncompressed executables, parallel start). The client logs a
  `Startup:` line with phase timings; ask for it if the owner still finds starting slow.
- **Not yet tried by the owner:** real AI providers, the pop-up scratchpad send keys, the release
  checklist's laptop items.

Next candidates the owner has been offered: content importing (which apps their notes are in decides the
first importers), updating RELEASE-CHECKLIST.md for 1.1/1.2, code signing (needs a certificate from them).

## This machine and its tools

- **Windows 11**, shells: PowerShell 7 (`pwsh`) and Git Bash. .NET 10 SDK, Node 22+, Microsoft Edge
  (used for PDF export and headless testing). **Python isn't installed**; use Node for scripts.
- **GitHub:** `gh` is signed in as `dave-patrick`; the remote is `github.com/dave-patrick/Quantos`.
- **Network from an agent's sandbox:** package installs (`npm install`, `dotnet restore` of new
  packages) and GitHub API calls may fail with DNS errors inside a sandbox; they need to run outside it.
- **Editing files from a shell:** `sed`, `printf` and inline `node -e` mangle backslashes and `$`. For
  regexes, `\n` in strings and C# verbatim strings, use a proper file edit, or write a small script file
  and run it with `node`.
- `build/build.ps1` has CRLF line endings (exact-text edits must match them).
- **Dev harness:** `tools/dev/` (see its README) starts everything and drives a headless browser. An
  older, git-ignored `.devharness/` folder from the previous agent is obsolete and can be deleted.
- **The Windows client can't be clicked through headlessly**, and the owner prefers not to have agents
  control their desktop. Verify client changes by building, by a short launch that reads the startup log,
  and by asking the owner to try them.

- **Active-vault samples and Notes preview (2026-10-23).** The owner asked to put the feature samples in their active vault, not ship a separate demo vault. Eighteen `Demo —` records now live in `C:\Users\davem\Quantos\Vault` (two per supported type); they were created and linked through the authenticated API/WriteService. The separate `Feature Demo Vault` was removed from the source, package and app folder. Notes' list/preview separator is intended to drag horizontally in side-by-side mode and vertically when cards stack; selecting a row fetches and renders its Markdown in the preview card. The My Day task rows in the docked sidebar use a dense single-row variant.
  The panel layout save effect was also corrected: it now snapshots reactive state before the debounce timer,
  so changes to panel membership, ordering and height weights are saved instead of resetting on relaunch.

- **Latest task row and frame correction (2026-10-23).** Dense My Day rows no longer show project or assignee chips; their title receives the available width and status/date chips are constrained. The client removes WS_THICKFRAME and uses custom drag resizing on all four edges and corners to avoid the silver DWM border. The Windows build was staged and relaunched successfully; HTTP 200 is confirmed in the client log. Ask the owner to visually confirm the edge and resize behavior.
- **Restore drag selecting the page (2026-10-23).** When the owner dragged a maximized window to restore it, the browser page ended up with text selected across the whole view. The native `WM_NCLBUTTONDOWN` caption move loop could continue into WebView2 because the form no longer has `WS_CAPTION`. Titlebar and icon drags now use form-level capture and move the form directly; a drag from maximized restores under the cursor and continues. The Windows x64 build and package validation passed, and the test copy was staged with the prior client closed and the new client relaunched (HTTP 200). Ask the owner to retry maximize, restore by drag, and ordinary window movement.

## Task editing and navigation — 2026-10-26

Private Windows build `2026.26.10.6` (release version remains `0.2.2`) adds full task popups
from inline task text/chips, preserving the mounted note and its scroll position. Header navigation
includes narrow/phone Back and Forward; history retains note scroll and existing/pinned tabs.
`#task` is an editable supertag: its default is virtual until explicitly saved; existing task files
inherit the schema without a bulk rewrite. Common fields use the schema editor, and reminders,
dependencies and subtasks retain dedicated controls. Renamed tasks update their bound note lines.

Validation: 109 frontend, 242 backend/core and 4 native client tests passed. The full .NET suite
passed with `.dev/task.runsettings` disabling collection parallelism; initial parallel runs hit
existing native sign-in timing and credential-test log teardown locks. No assertion was relaxed.
Browser acceptance covered title/body/due/custom-field persistence, keyboard focus/Escape, unchanged
editor identity and scroll at 3671px, Back/Forward restoration, #task forms, and 390px popup layout.
Evidence is in ignored `.dev/task-*` logs and screenshots. The Windows archive and self-check passed
with zero self-check warnings; the owner's test copy was staged/restarted with HTTP 200.
Android binaries/device interaction were not validated for this change. No public release was made.
