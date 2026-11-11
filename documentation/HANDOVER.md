# Handover: working on Quantos with its owner

## Published 0.3.1 (2026-11-11)

Release 0.3.1, build 2026.11.11.54, is merged into main through PR #5 (7261c8c) and published at https://github.com/dave-patrick/Quantos/releases/tag/v0.3.1. Windows x64 ZIP, public release-signed Android ARM64 APK (versionCode 301), and SHA256SUMS.txt are uploaded; GitHub asset digests match local files. Android build: zero warnings/errors, signature and non-debug metadata verified. Public Windows package/self-check passed with zero self-check warnings. Private Google Tasks client configuration was excluded from public binaries and restored with unchanged hash. PR and merged-main Windows/Linux/package CI passed. Optional Claude review action failed before completing; independent local pending-change review returned no findings. Frontend 184 tests and seven native tests passed locally; backend 372/373 initially, discovery health-check passed isolated retry, and the full Windows suite passed CI. Browser colour fixture passed 38 checks; spacing checks passed desktop/800px (dynamic narrow-page harness was not recertified). Owner private Windows build 0.3.1 / 2026.11.11.54 was packaged, staged and restarted; startup Page loaded succeeded with HTTP 200 at 10:53:22. Installed executable and managed assembly hashes match the private package. No Android installation was requested or performed.

## Code improvements pass (2026-11-10)

Comprehensive improvements reviewed and implemented on the dedicated branch `code-improvements`.
Includes granular interface element theming (*Settings › Appearance*) allowing colour customisation across 21 UI elements with live miniature preview, CSS export, and element auto-resets; editor widget DOM position stability, single-dispatch block conversions, one-time heading fold indexing, active tab migration & pane flush handling, 3-way typing merge on raw saves, plugin proxy directory traversal defense, nested batch scopes in `CacheIndex`, single-query graph resolution (`AllResolvedLinks`), directory separator boundary checks on workspace moves/creates, thread-safe permission lookups, atomic append adoption in CLI and MCP, and non-blocking PDF print pipes.
Frontend tests (179/179), typecheck (0 errors, 0 warnings), and backend tests (343/343) all pass.

## Code scan and five rebuilds (2026-11-06/07)

The owner asked for a code scan, then "fix everything", then all five proposed rebuilds. Committed and pushed to
`main` (not released). Things to know: the Windows package is now a shared runtime folder (437 files) rather
than three single-file executables; installed updaters copy only top-level files, so any subfolder must go in
`quantos-folders.zip` (the build script refuses nested files). Version history keeps 10 versions per note by
default (owner's choice; *Settings › Vault › Version history*). Open decisions offered to the owner: the
TypeScript 7 / Vite 8 / Vitest 5 upgrade. The owner's test copy runs the new layout; the first install of it
through the in-app updater happens with the next release.

## Google Tasks sync (2026-10-30)

The owner asked for Google Tasks as an optional replacement/counterpart for Quantos tasks: two-way, desktop and Android, they will register the
Google Cloud project, and they are the only user. It is built and tested locally (see PROJECT-STATUS "Google Tasks sync") but **not committed,
published or tried against a real Google account**. Open items for the owner: (1) create the Google Cloud Desktop-app client, set the consent screen to
In production, and run Windows end to end (checklist in the design's "Verification criteria › Manual"); (2) Android loopback sign-in is verified on the phone (2026-11-02, build .30: connected, two-way sync confirmed). Android blocks a background app's network, so the code exchange is deferred until the app returns to the front (see ANDROID.md); the 60 s-wait run was not repeated; (3) check the refresh token still works on or after 2026-11-09. They chose local-wins over last-writer-wins for conflicts, and on 2026-11-02 approved all the design's flagged decisions (D1–D8, the Android loopback sign-in and the delete guard) as written. The design doc
records six review rounds; the owner twice allowed a revision past the 3-revision cap and then chose to fold in the last findings and implement. Run `pwsh build/next-build.ps1` for the first build that contains it.

## Local feature-gap fixes (2026-10-28)

User approved fixing the four audited gaps and explicitly allowed editing before theme publication finished. These changes must not be pushed or published without a further request. Independent diagnosis confirmed all four causes. The source now uses lazy offline English spelling dictionaries, saved-rule automation grant controls through the existing native approval flow, current local date/time insertion, and a searchable synced-block picker without the eight-result truncation. No vault schema or backend permission policy changed. Browser acceptance is currently blocked by browser tooling; do not claim native/device verification from unit checks. Release 0.2.4/theme publication belongs to the other chat.

Private Windows build `2026.28.10.19` (release `0.2.4`, dirty source mark) was packaged and installed/restarted in the owner's usual test copy. All 144 frontend, 242 backend/core and five native tests passed, as did typecheck (zero errors, nine existing warnings), ZIP checks, Herald and packaged self-check (zero warnings). Two independent review passes ended clean; the final pass includes chooser shortcut/Android Back handling. All three installed executable hashes match; startup and served shell/licences returned HTTP 200, and served assets contain build 19. No Android APK or phone update was made. Browser fixture execution remains unverified: automatic review blocked headless launch with “blocked by policy”, and in-app browser/panel attachment failed. Build 18 was interrupted before packaging to include the final keyboard fix; build 19 is the delivered set.

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

- **Windows light-mode frame (2026-10-28):** local fix sends resolved CSS palette colors through
  the origin-checked bridge to repaint the custom resize edge, title bar and caption buttons.
  Popup pages cannot recolor the main window. Release remains 0.2.3; private build 2026.28.10.16.
  Native regression reproduced the old dark edge and passes after the fix; browser fixture covers
  runtime switches, light startup and custom colors. Full package/ZIP checks passed with 124 frontend,
  242 backend/core and five native tests; packaged self-check has zero warnings. Installed/restarted
  the owner's test copy with matching executable hashes and Page loaded / HTTP 200. Native visual
  and maximize/restore checks were not completed by Computer Use. The owner subsequently confirmed
  the appearance locally and authorized commit/push/publication. Windows 0.2.4 build 2026.28.10.17
  is now public; source/tag `960accd` is pushed. All 371 tests and package/self-check pass; public
  download and both asset digests are verified. Owner test copy restarted with matching executable
  hashes, HTTP 200 and served build 17. Numeric version advances for updater compatibility.
  Maximize/restore and the other machine's updater installation remain unverified.

- **Published Windows 0.2.3 (2026-10-27), build `2026.27.10.15`.** Source/tag `05dada6` includes
  the Windows/browser popup, sidebar metadata layout and pending credential-separation work.
  Release review found and fixed partial subtask retries: creation is retained and metadata is
  retried on that task, with no duplicate and successful popup close in the browser acceptance check.
  All 370 tests passed in the final checks; package/archive/self-check passed. The owner's staging
  copy was replaced/restarted with matching executable hashes, HTTP 200 and the final served build ID.
  GitHub latest is public v0.2.3 with the Windows ZIP and SHA256SUMS.txt only; both digests match.
  Public Android stays on 0.2.2 and the phone was not changed. See releases/0.2.3/RELEASE-CHECKLIST.md.

- **Task popup restored for Windows/browser (2026-10-27).** The owner explicitly wants task clicks
  in the Windows app and browser to edit in a popup while keeping their place; Android is excluded.
  The original workspace remains mounted and inert beneath the editor; Close/Escape waits for body,
  title, metadata and submitted subtask writes, then restores scroll/focus. Modified clicks retain
  tabs/panes. Nested links stay in the popup; existing prompts appear above it. Failed writes retain
  the popup until retry; disk conflict choices clear obsolete drafts and failure markers. Browser
  acceptance covered source identity at 2200px, task/source links, title source-label refresh,
  failed title/reminder retry, nested due-date prompts, conflict resolution/reopen and read-offline
  dismissal. See PROJECT-STATUS for build/staging validation.
  Delivered private Windows `0.2.2`, build `2026.27.10.14`, to the owner's local staging copy;
  restarted with Page loaded / HTTP 200, matching executable hashes and served build ID. All 370
  tests passed, package/self-check passed, independent review finished clean, and the native Windows
  UI exposed the full task editor popup. Browser acceptance supplies close/scroll/failure evidence.

- **Sidebar task titles (2026-10-27, source/frontend build 2026.27.10.12).** Tasks and docked My Day
  now put dates, assigned priority and other existing metadata below the title in smaller text.
  Empty metadata takes no extra line; long values wrap. Full-size Tasks keeps its existing layout.
  Isolated browser checks covered 264–266px rows, long/unbroken metadata, missing metadata,
  opening, completion and Alt+Down queue reorder with focus retained. Frontend checks/tests/build
  and independent review passed. Windows staging follow-up: package `2026.27.10.13` retains release
  `0.2.2` and includes the current checkout. All 111 frontend, 242 backend/core and four native tests
  passed (backend rerun sequentially after one log-lock cleanup failure). ZIP validation and packaged
  self-check passed with zero warnings. Installed into the owner's local staging copy and restarted;
  all three executable hashes match, Page loaded reports HTTP 200 and served assets contain the new
  build ID. No Android installation or public publication was performed.

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

## Android note folders — clarified scope, 2026-10-26

The owner clarified that the request is **folders for notes on Android**, and explicitly said task
popups are not wanted. This supersedes the task-popup surface above. The Android update removes
that popup behavior; task chips use normal task pages. Do not stage Windows for this correction.
Phone Notes now browses folders/subfolders through More → Notes. Folder paths live in the Notes
tab argument, so Back/Forward retraces them and Back from a note returns to its originating folder.
Existing folder counts are recursive; the note list/search stay within the current folder. Empty
folders are not shown. The folder browser source initially existed as pending work in the checkout;
it is included in this Android delivery and its navigation is now integrated with workspace history.

Private ARM64 Debug build `2026.26.10.10`, semantic version `0.2.2`, retains the installed certificate.
Native SM-S948U touch acceptance confirmed the installed build and checked folder → subfolder → note, Back to
folder, Back to parent, Forward to the same folder, and New note saving in that folder at the actual
411px viewport. The final phone state was restored to Videos notes. Test notes were
moved to Trash afterwards; original active counts stayed 24 default / 614 Videos. A server DNS
resolution error was present before installation and remains; it was not introduced by this change.
Frontend check: zero errors and nine existing warnings; 105 frontend tests passed. Android build:
zero warnings/errors. Evidence is in ignored `.dev/note-folders-*`. No public release was made.

## Android icon and complete folder hierarchy — 2026-10-26

Private Debug ARM64 build `2026.26.10.11` is installed on the SM-S948U with its existing certificate.
The previous raster launcher/browser icon was the Atomic 22 publisher image. Both are now PNG
renders of the existing `brand/quantos-icon.svg`; the manifest still uses `@drawable/quantos_icon`.
Packaged-resource inspection and the phone's native App info screen show the actual Quantos Q.

Phone Notes previously stripped the physical Notes/Daily/Tasks/Archive parent and derived folders
only from active note summaries. It now loads `/api/tree?archived=true`, merges summaries without
flattening paths, includes empty directories and archive contents, and labels archived notes.
`noteFolders.ts` owns path validation, counts, display labels and migration from old `folder:` tab
arguments to `folderpath:` physical locations. Notes includes its workspace picker and New folder;
New note saves to the selected physical directory. Folder actions still use the existing APIs.

Validation: 111 frontend tests passed; Svelte check has zero errors / nine existing warnings. Android
build has zero warnings/errors. On-device touch checks matched all 5 Default and all 17 Videos
folders against the real folder API, including empty folders/Archive. Opening a folder, New folder,
New note in the selected directory, Back and Forward passed. Before/after active counts matched
(24 Default / 613 Videos); only the owned test note was trashed. Native evidence is under ignored
`.dev/folders-icon-*`. Windows was not staged and no public release was made.


### Interface refinements and native scan (2026-10-28)

Private Windows and Android release 0.2.4/build 2026.28.10.21 is installed. All six review findings are fixed, plus native Android Capture naming/target size, the theme disclosure touch target, and embedded backend version identity. All 391 automated tests passed during the change; final adjustments passed typecheck (zero errors/nine existing warnings), 15 browser assertions and axe (no violations). Two independent review rounds ended clean. Windows ZIP/self-check passed (zero self-check warnings), all installed executable hashes match, and the client log confirms HTTP 200 at 14:24:24. Android built without warnings/errors, passed signature verification and install-r, and the physical SM-S948U confirms version/build, 48px controls, Notes/Back, real IME visibility, Tasks Options and Appearance reflow. Note/workspace counts are unchanged. Windows GUI automation remains unavailable; three source-supported host findings remain in UI-DESIGN-SCAN-2026-10-28.md. The owner confirmed that the phone theme change was intentional and requested keeping the current theme. No restoration or recovery preference writes were made; this item is resolved. No commit, push or public release. Preserve unrelated local feature-gap changes.


Task priority is Critical, High, Medium, or Low (stored as 1–4 in that order). Task effort is Low, Medium, or High; quick add accepts `~low`, `~medium`, or `~high`. Older minute estimates remain until changed and continue to support duration-based planning; selecting an effort level replaces the old estimate.

Task level changes are uncommitted and require a browser walkthrough: the development sign-in failed against the existing service. My Day no longer shows a duration total because effort levels cannot be summed as minutes.

Sidebar section expansion and custom navigation choices are retained on this device. The Windows client remembers its active browser profile when it must use a fallback profile.

## Folder selection (2026-11-09)

The Windows Link a folder dialog now offers Browse alongside manual path entry. The owned native picker returns a correlated selection without linking until Next; cancellation preserves the entry. Other clients retain path entry. Drive and UNC roots are preserved. Private Windows build 2026.09.11.41 retains release 0.2.10. Validation and staging results are recorded in PROJECT-STATUS.md.

## Note location copy commands (2026-11-09)

Owner requested three separate actions: filename including extension, containing folder path, full path plus filename. Added to Notes list/tree and Files menus; linked locations use their external root. Private Windows build 2026.09.11.42 retains release 0.2.10. Design/readiness and independent review passed; browser checks exercised both menus and clipboard denial. Existing unrelated native folder-picker changes were preserved and included in the build.

Validation complete: 172 frontend, 353 backend/core and seven native tests passed; initial backend run hit a temporary test-log access crash, isolated retry passed. Package/ZIP and self-check passed (zero self-check warnings). Owner copy staged/restarted with HTTP 200 and matching executable/managed assembly hashes. No commit, push or public release. Browser verified both menus using intercepted clipboard writes; native clipboard interaction was not separately exercised.


## Tag scope and bridge setup (2026-11-09)

Owner chose workspace-specific new tags by default and migration of existing tags to videos. Scope editor uses separate Save scope; schema saves materialize implicit/task fallbacks by returned ID to avoid editing another workspace. Active workspace and document workspace schema caches are separate. Folder bridge settings use existing Core transfer engine with app-hosted workers and explicit Start. Original folders, external bridge installations and credentials remain untouched. Source reviews passed after scope/archived graph/synthetic ID fixes and bridge physical-path/lifecycle fixes. Completion checks appended below.

Migration completed and verified: 836 formerly implicit tags now have Videos-specific definitions; all 1,278 preexisting note files have unchanged hashes. Existing names, including spaces, are preserved. Default setting saved as workspace-specific. Backup and manifest: C:\Users\davem\Projects\Quantos-maintenance\scope-migration-2026-11-09-3. Initial preflight attempts made no source changes. Inline-only hashtag text was left unchanged, matching existing app tag semantics. Validation: 175 frontend, 373 backend/core (sequential retry after native sign-in timeout), seven native tests; zero typecheck errors/warnings. Browser covers default persistence, both scope directions, explicit default-workspace selection under shared preference and bridge stopped-save/cancel-start/remove fixture. No actual folder bridge configured or started.


Delivery: private Windows release 0.2.10 build 2026.09.11.44 packaged/ZIP verified, self-check zero warnings; staged/restarted owner's copy with matching executable and managed-assembly hashes and startup HTTP 200. All 555 automated tests passed. Existing folder picker and clipboard work was preserved. No commit, push, public release, Android APK install, or real folder-bridge transfer. Owner vault migration verified separately above.


## Notes excludes schema definitions (2026-11-09)

Fixed the Notes content filter on desktop and phone, including folder counts and the managed Supertags directory. Notes carrying tags remain visible; Files/Manage supertags retain definitions. Independent diagnosis confirmed broad title-index filtering caused the regression after materializing schemas. Browser before-fix reproduced three definition rows, after-fix none with 20 content rows retained. No vault data or schema changes. Frontend regression checks and final staging recorded below.

Delivery: Windows 0.2.10 build 2026.09.11.45 installed/restarted with HTTP 200 and matching executable/managed assembly hashes. All 178 frontend tests passed; svelte-check zero errors/warnings; independent review no findings. Browser before/after verified definition rows 3 to 0 with 20 content rows retained, tree schema rows absent, Manage supertags still retained all three test definitions. Package/ZIP and self-check passed (zero self-check warnings). Backend unchanged; its previously passing suites were not rerun for this frontend-only fix. No vault data changed, no commit/push or Android installation.

## Branch review and local build (2026-11-10)

Reviewed code-improvements independently in two rounds. Fixed encoded plugin queries being rejected while retaining endpoint and traversal restrictions; added a message-bridge regression test. Release remains 0.2.11; private build 2026.10.11.47. Merged through PR #4 (0e27aea). All GitHub Windows/Linux tests and Windows package checks passed. Local: 179 frontend and 7 native tests passed; svelte-check zero errors/warnings; package/ZIP checks and self-check passed (zero warnings). Initial local backend run: 371/373 passed; discovery and Edge PDF checks failed under severe memory pressure; extended sequential retry was interrupted after stalling, so those local failures remain unresolved. Claude CI review action failed during execution; two local independent review rounds completed clean after the plugin fix. Staged/restarted the owner copy with matching executable/assembly hashes and Page loaded HTTP 200 at 12:02:28. No public release or Android install. Broader interactive editor acceptance was not repeated.


## Antigravity theme preview test build (2026-11-10)

Built origin/code-improvements commit 5402b4c into private Windows release 0.2.11 build 2026.10.11.48. All 180 frontend tests passed; svelte-check zero errors/warnings; package/ZIP checks and packaged self-check passed (zero warnings). Staged/restarted the owner's usual copy; executable and managed assembly hashes match and current Page loaded reports HTTP 200. Backend code unchanged; backend/native suites and broader interactive theme acceptance were not repeated. No merge, push, public release or Android install for this build.

## Primary text picker and rounded status bar (2026-11-10)

Private Windows 0.2.11 build 2026.10.11.50 fixes primary text overrides being ignored without a custom background and silently contrast-adjusted with one. Exact selected text colours now apply independently; explicit secondary overrides retain precedence. StatusBar has a full rounded 12px border. Two regression tests failed before and pass after. Browser fixture textColour.html verifies live input events and exact computed text on light/dark bases plus rounded corners. Independent diagnosis agreed; review no findings. Delivery verified: 182 frontend tests passed, svelte-check zero errors/warnings, browser fixture 14 checks passed. Package and self-check passed with zero self-check warnings. Installed hashes match; restarted owner copy Page loaded HTTP 200 at 13:58:48. Backend/native suites not rerun for this frontend-only change. No commit, push, merge, public release or Android install.


## Main and secondary text groups (2026-11-10)

Private Windows 0.2.11 build 2026.10.11.52 replaces Soft/Muted/Faint text controls with Main text (content and primary labels) and Secondary text (metadata, hints, placeholders). Selected colours are exact; legacy shades remain until group editing. Automatic resets the group. Design gates and independent review clear. All 184 frontend tests passed after retrying an unrelated spellcheck timeout; svelte-check zero errors/warnings. Browser fixture verified 38 checks including actual labels/metadata, repeated input, legacy clearing and both group resets on light/dark bases. No backend/native suites rerun. Package/ZIP checks passed; self-check zero warnings. Staged/restarted owner copy with matching executable/managed assembly hashes and Page loaded HTTP 200 at 14:20:18. No commit, push, merge, public release or Android install.


## Interface spacing pass (2026-11-10)

Private Windows 0.2.11 build 2026.10.11.53 tightens shell/header, removes doubled panel-to-status margins and phantom sidebar grid gutters, aligns outer insets/gaps at8px, and reduces settings/editor padding. Android expanded header touch height retained; narrow overlays account for shell insets; right-panel resizing matches new padding. Browser spacing fixture passed1440/800/640px widths (outer/status gaps8px, no document overflow, both-sidebars-closed gutters removed). Actual appearance screenshot checked. Independent layout review no findings. All184 frontend tests pass; typecheck0 errors/warnings; Impeccable scans clean; Herald valid. Native CUA window inspection aborted; Android device not tested. Package/ZIP checks and self-check passed (zero self-check warnings). Staged/restarted owner copy with matching executable/managed assembly hashes and startup HTTP200 at14:38:06. No commit/push/public release or Android install.


## Version update and GitHub push (2026-11-11)

Owner requested release version 0.3.1; separate build ID is 2026.11.11.54. Includes the pending main/secondary text controls, exact text colours, rounded status bar and interface spacing work. Android versionCode is 301. Independent pending-change review: no findings; typecheck zero errors/warnings; browser text fixture 38 checks and desktop spacing fixture passed. Windows build and staging validation recorded below when complete. Owner subsequently requested merging all work to main and publishing v0.3.1 with Windows and release-signed Android artifacts. Validation: 184 frontend tests and seven native tests passed; backend suite 372/373 initially, discovery health-check passed on isolated retry.
