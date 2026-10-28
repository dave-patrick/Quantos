# Quantos project status and handoff

For anyone (person or AI agent) picking up work on Quantos. Read [HANDOVER.md](HANDOVER.md) (the owner,
open threads, the machine) and this, then
[BUILD-PLAN.md](../BUILD-PLAN.md) for the full plan and decision log, and [CHANGELOG.md](../CHANGELOG.md)
for what shipped when. Keep this file current when you finish a piece of work.

_Last updated: 2026-10-28. Windows 0.2.4, build 2026.28.10.17, is published and staged locally;
public Android remains 0.2.2. Optional server/folder bridge deployments remain separate._

The owner approved committing, pushing and publishing the Windows frame-theme fix after confirming
the appearance locally on private 0.2.3 build 2026.28.10.16. The numeric release advances to 0.2.4
because legacy updaters compare release versions rather than build IDs. See
[the release checklist](releases/0.2.4/RELEASE-CHECKLIST.md) for current delivery evidence.
Source/tag `960accd` is pushed. All 371 tests and package/self-check validations passed; public
download SHA-256 matches GitHub, latest-release updater metadata is correct, and the owner's
test copy restarted with matching executables and served build identity. Manual native
maximize/restore and the other machine's updater installation remain unverified.

### Earlier implementation and verification history

Windows edge resizing is fixed and staged (October 26): the frameless host reserves a DPI-scaled
outer strip so WebView2 cannot intercept its resize input. The strip disappears when maximized.
Package/archive validation and startup HTTP 200 passed. Native mouse checks passed all four edges,
all four corners, title-bar movement and resizing after maximize/restore. Existing automated suite
counts below are unchanged; this native-only change was verified through compile/package and UI checks.

Security remediation is now in progress in source/build set `2026.26.10.3` (release version remains
0.2.2). The first batch tightens Windows native origins, displays full scripts before execution,
and closes calendar/attachment path escapes. See [SECURITY-REMEDIATION.md](SECURITY-REMEDIATION.md)
for the implemented scope and open audit findings. Published packages and owner installations
remain unchanged by this batch.
Validation passed 204 backend/core tests with test collections run sequentially and three native
dialog tests. Default parallel runs hit existing discovery/SSE timeouts; no timeout/assertion was
relaxed. Independent review finished clean after preserving linked-folder calendar export.

Compact phone Notes now browses note folders with a breadcrumb trail and recursive folder counts;
note rows use tighter spacing with a 48px minimum tap target. Search remains scoped to titles and tags
in the open folder. Empty folders are not shown because the phone view uses the existing note summaries.

### Android and shared vault (owner-approved scope, 2026-10-24)

- `src/Quantos.FolderBridge/` adds an optional host worker for a folder managed by Google Drive or
  another app. It exposes only that folder's Markdown as one shared workspace using the existing
  conditional three-way sync and WriteService. Runtime state/recovery stay outside the source;
  unavailable/empty folders and large removal batches pause. Ordinary device links remain local.
  Five bridge tests and live server-index integration pass. The owner's Videos folder is deployed
  read/write on T580; Android now renders 618 notes in `videos` plus 24 in `default`. Live temporary-note
  tests verified outside edits, Quantos/Android write-back and MCP reads with zero conflicts, then
  removed the probe. Source and server backups are retained; see HANDOVER and SERVER.md.
  On October 26, desktop sign-in restored the Drive mount; the direct windowless logon task started
  automatically and passed controlled stop/restart (zero then one worker). Docker Desktop was
  started, server health returned HTTP 200, and the 07:16 sync completed with zero conflicts.
- Imported/shared notes use their physical workspace folder for indexing, preserving other apps'
  frontmatter workspace fields. Cache version 11 rebuilds existing derived assignments. This fixes
  Videos files that arrived on Android but appeared under their legacy `video-processing` metadata.
  The full .NET suite passes 175 tests, including metadata-preserving scope/restart cases.
- **Phone navigation** settings now select and order one to four bottom-bar shortcuts; More stays
  last. Defaults follow the owner's choice: Daily notes, Tasks, Capture and Today. Capture opens
  quick capture without navigating away. Choices persist locally in device preferences, leaving
  tablets unchanged. Preference validation is covered by four tests; the full build passes
  101 frontend / 175 .NET tests, archive validation and self-check (zero self-check warnings).
  Windows was staged/restarted with Page loaded / HTTP 200. Both APKs build without warnings/errors.
  Native 360px tests verified changing/resetting the bar, restart persistence, enlarged text and a
  real Capture tap retaining the active page; tablet splits stayed expanded. The owner's 411px phone
  has the updated ARM64 build and requested default order, with 24 nodes and no sync errors/conflicts;
  server MCP write access is preserved.
  Finish review scored its sole reorder-button spacing fix resolved (ship for that fix). A later
  missing-notes report was traced to the phone's empty `video-processing` workspace; switching to
  `default` restored its 24 notes. Transient VPN DNS failure cleared and authenticated sync returned
  HTTP 200 with zero transfers/conflicts. Videos folder sharing and the workspace indexing fix
  are deployed as described above; More has a permanent Notes entry for the compact list.
- `android/` is a native .NET Android host using the existing Core, linked backend source and
  embedded Svelte frontend. It preserves the actual app brand and features, with phone/tablet
  layouts, system/IME insets, Android Back, Keystore, native files/printing/PDF rendering,
  notifications and Android `sh`. Windows global shortcuts and PowerShell remain platform-specific.
- The phone revision uses Notes, Tasks, Today and More with one page at a time, a separate closeable
  Details screen and simpler note/task controls. Advanced views remain in More; the editor stays
  mounted behind More/Details. Header search searches note bodies. Tablets retain split panes and
  sidebars. The last sidebar panel's X now hides the sidebar and preserves its configuration.
  Native checks at 360px, enlarged text and real IME passed, as did tablet close/reopen; independent
  review scored the requested search/Capture fixes resolved, disposition ship. The updated ARM64 APK
  is installed on the owner's phone: 24 nodes, production sync with no conflicts/errors, enabled MCP
  writes preserved, and Details closed by a real native tap. The updated Windows package passed
  97 frontend / 167 .NET tests, archive checks and self-check (zero self-check warnings), and was
  staged/restarted with Page loaded / HTTP 200.
- `src/Quantos.Server/` is the optional portable Docker shared-vault server. Devices retain their
  local vaults and sync using persisted three-way hashes. Concurrent edits and edit-versus-delete
  preserve both copies for review. Keys, grants, caches, linked paths and device layouts stay local.
- The backend hosts nine MCP tools with a separate agent key. **Settings › MCP › Shared server MCP**
  controls create/edit permission on the actual server; it starts read-only. MCP cannot grant itself
  permissions or use shell/admin APIs. **Settings › Shared vault** connects, disconnects and reviews
  conflicts. See [ANDROID.md](ANDROID.md), [SERVER.md](SERVER.md) and the updated API/security/storage guides.
- T580 production service is healthy at `https://t580.tail7a64f4.ts.net:8445`, Docker `quantos-server`,
  named volume `quantos-data`, time zone `America/Phoenix`. Runtime keys are protected in the host's
  `~/Apps/QuantosServer/keys/`; never commit/copy them into documentation. Existing Tailscale services
  were preserved. Port 8446 and `quantos-test-data` are an isolated synthetic QA vault.
- Verified: all **167 .NET tests**, including server auth/MCP grant persistence, offline/restart sync,
  conflict preservation and disconnect cancellation. Android x64 and ARM64 test APKs compile with
  zero warnings. Installed x64 Android phone/tablet emulators opened the full app. Phone edits
  survived app restart; a real offline edit with the test server stopped reached Windows after
  reconnect. MCP write toggle and native Copy action worked against T580. Tablet sync and split
  panes worked. Native PDF renderer returned a valid page PNG. Native export produced a valid
  13-file ZIP without device secrets; import restored its notes in a separate vault and kept the
  original. An approved Android shell command returned its expected marker. Frontend check: 0 errors, 9 warnings;
  desktop Settings/MCP axe scan: no violations.
- Independent native review preserved branding/layout/MCP UI and required keeping the active line
  visible when the IME resizes the note viewport. The Android editor now explicitly scrolls its
  focused selection on viewport resize. The reviewer scored that fix resolved (ship for the scored
  fix); the documenter verified incumbent assets/tokens without changing the existing system.
- The final Windows package passed 97 frontend / 167 .NET tests, archive checks and self-check
  (zero self-check warnings). It was staged and restarted with Page loaded / HTTP 200. After a
  verified 29-file backup, the owner's Windows vault connected to production: 28 portable files
  uploaded, zero conflicts. MCP write access is enabled and persisted across a server restart;
  real authenticated MCP initialize/tools-list returned all nine tools. The synthetic QA server
  is stopped; its volume is retained.
- Physical ARM64 install/connection verified on the owner's SM-S948U phone (2026-10-25): 24 nodes,
  successful production sync, zero conflicts/errors, and saved connection/MCP write status confirmed
  after app restart. The phone's Tailscale included-app list includes Quantos; this is necessary for
  its private server DNS/routing.
- Test APKs are debug-signed, not a public release. Broader physical-device acceptance, real
  microphone/notification delivery, full backup restore or every inherited view has not been manually
  exercised. Android may stop background work; reminders/sync resume when reopened. Server migration
  is documented and preserves the server id/data, but a hosted migration has not been performed.

## 1. Where things stand

Latest Windows release: **0.2.3**, build **2026.27.10.15**, published October 27, 2026 from tag/source
`05dada6`. Windows/browser task popups, sidebar metadata and credential separation are included.
The final release passed 124 frontend, 242 backend-core and four native tests, archive validation,
zero-warning self-check, startup HTTP 200, served build identity and installed executable hash checks.
The owner's local staging copy was updated and restarted. GitHub latest-release metadata and both
asset digests were verified publicly. Android's latest public APK remains 0.2.2; no phone update or
shared-server deployment was performed. Full evidence and manual limits are in
[the 0.2.3 release record](releases/0.2.3/RELEASE-CHECKLIST.md).

| | |
|---|---|
| Latest release | [0.2.2](https://github.com/dave-patrick/Quantos/releases/tag/v0.2.2), build `2026.26.10.2`; Windows x64 ZIP and public signed Android ARM64 Release APK. SHA-256 and verification: [release checklist](releases/0.2.2/RELEASE-CHECKLIST.md). |
| Branch | `main` (no long-lived branches; commits go straight to `main`) |
| Plan coverage | BUILD-PLAN Releases 1.0, 1.1 and 1.2 are built. Only the "Roadmap items without an assigned release" remain (section 4). |
| Tests | 175 .NET (xUnit, `tests/Quantos.Tests`) and 101 frontend (Vitest, `frontend/tests`); all pass in the current phone-navigation test build. |
| Released build | Version 0.2.2, build 2026.26.10.2, version commit `5a619c5`, tag `v0.2.2`. About displays both identities. All 276 automated tests and backend self-check pass; final Windows sample-vault startup/resize and Android Release-mode emulator navigation passed. Uploaded digests match, and the live updater downloaded/verified/unpacked the published ZIP. Owner Windows 0.2.1 reports 0.2.2 available for their own install click. Their phone runs private Debug 0.2.2 with 24 default + 618 Videos notes and zero sync errors/conflicts. |
| Accessibility | axe-core scans of every view show no serious or critical issues, in the built-in and custom themes. |

Version numbers are product versions (0.x.y) and are separate from the plan's "Release 1.x" names.
0.1.1 was the first public release and contained Releases 1.0 to 1.2.

## 2. Documents

| File | Purpose |
|---|---|
| [BUILD-PLAN.md](../BUILD-PLAN.md) | The approved plan: requirements, architecture, stages, acceptance tests, roadmap (section 14), decision log (section 17). Items are marked *(Built …)* as they land. |
| [CHANGELOG.md](../CHANGELOG.md) | Per-release changes. |
| [PRODUCT.md](../PRODUCT.md), [DESIGN.md](../DESIGN.md) | Product intent and design system (brand, tokens, tone). |
| [STORAGE-SPEC.md](STORAGE-SPEC.md) | The vault file format: frontmatter keys, folders, history, every property Quantos reads or writes. Update it for any format change. |
| [SECURITY-MODEL.md](SECURITY-MODEL.md) | Actors, permissions, approvals, network use, the Windows bridge. Update it for anything that touches trust. |
| [API.md](API.md) | Backend HTTP API contracts. |
| [USER-GUIDE.md](USER-GUIDE.md) | The user-facing guide (shipped in the zip and shown in Help). Every user-visible feature is described here. |
| [TROUBLESHOOTING.md](TROUBLESHOOTING.md), [CLI-REFERENCE.md](CLI-REFERENCE.md), [PLUGINS.md](PLUGINS.md), [AGENT-ADAPTERS.md](AGENT-ADAPTERS.md) | Shipped guides, also shown in Help. |
| [RELEASE-CHECKLIST.md](RELEASE-CHECKLIST.md) | Manual checks per release (build, packaging, writing, linked folders, agents, security, documents). |
| [PERFORMANCE.md](PERFORMANCE.md) | Measured performance. |
| [HANDOVER.md](HANDOVER.md) | How the owner works, open threads, machine and tool quirks. |
| [tools/dev/README.md](../tools/dev/README.md) | The development harness: run the app, drive a headless browser, accessibility scans. |
| `.herald/context/project.md`, `AGENTS.md` | Short project context loaded by agent tools. |

## 3. What's built, and where

Paths are relative to the repository root. "Core" is `src/Quantos.Core`, "Backend" `src/Quantos.Backend`,
"Client" `src/Quantos.Client`, "views" `frontend/src/components/views`, "lib" `frontend/src/lib`.

### Foundation (Release 1.0)
- **Vault and writes:** Core `WriteService.cs` (every vault change goes through it), `Storage/` (vault
  layout, atomic files, journal, history, trash), `Reconciler.cs`, `Cache/CacheIndex.cs` (SQLite, derived,
  rebuildable; bump `Version` when the schema or indexing changes).
- **Backend:** Backend `Program.cs` (DI and endpoint registration), `Api/*Endpoints.cs`, `Hosting/`
  (vault host, auth tokens, presence). Listens on 127.0.0.1 only.
- **Windows client:** Client `MainForm.cs` (WebView2 window, tray, bridge messages, exit and update
  flows), `BackendConnection.cs`, `HotKeys.cs`, `TerminalHost.cs`, `Updater.cs`.
- **CLI and MCP:** `src/Quantos.Cli` (`Program.cs` commands, `Mcp.cs` tools and the `tools`/`call` export).
- **Frontend:** Svelte 5 + CodeMirror 6 in `frontend/`. Shell `components/Shell.svelte`, tabs
  `lib/layout.svelte.ts` (`TabKind`) and `components/TabView.svelte`, commands `lib/commands.svelte.ts`
  and `lib/editorCommands.ts`, editor extensions `lib/editor/`, document store `lib/docs.svelte.ts`.

### Release 1.1 (all built)
| Feature | Main code |
|---|---|
| Custom styling, density | lib `appearance.svelte.ts`; vault `themes/*.css` |
| CSV import/export | lib `query.ts` (`parseCsv`, `toCsv`), `csvexport.ts`; Core `Export/ExportService.cs` |
| In-app AI, meaning search | Core `Ai/` (providers, encrypted keys, Anthropic SDK for Claude, OpenAI-style HTTP for others, `SemanticIndex`); Backend `Api/AiEndpoints.cs`; lib `ai.svelte.ts`; views `AskView.svelte` |
| Rolling backups | Core `Backups/BackupService.cs`; Backend `Api/BackupEndpoints.cs`; `components/BackupSettings.svelte` |
| Math, Mermaid | lib `diagrams.ts`, `editor/diagrams.ts` (KaTeX and Mermaid bundled, loaded on first use) |
| Read-only mode | Core `WriteService.LockRefusal` (`locked: true`); prefs `reading` |
| Find and replace | Core `Structure/FindReplaceService.cs`; views `ReplaceView.svelte` |
| Revision comparison | lib `diff.ts`; views `HistoryView.svelte` |
| Presentation | lib `slides.ts`, `present.svelte.ts`; `components/Presentation.svelte` |
| Focus timer | Core `Tasks/FocusService.cs`; lib `focus.svelte.ts`; views `FocusView.svelte` |
| Habits and metrics | Core `Tasks/HabitService.cs`; lib `habits.ts`; views `HabitsView.svelte` |
| Sprints, goals, analytics | Core `Tasks/PlanningService.cs`; views `SprintsView.svelte`, `GoalsView.svelte` |
| Audio | lib `recorder.svelte.ts`, `editor/attachments.ts` (player); Client `MainForm` permission handler |

### Release 1.2 (all built)
| Feature | Main code |
|---|---|
| Dependency Gantt | lib `gantt.ts`; views `GanttView.svelte` |
| Whiteboards (JSON Canvas in a `type: canvas` note) | lib `canvas.ts`; `components/Whiteboard.svelte` |
| Mind maps with annotations (footnotes) | lib `mindmap.ts`; views `MindmapView.svelte` |
| Global knowledge graph | Core `Structure/GraphService.cs`; views `GraphView.svelte`, `components/Graph.svelte` |
| Map (offline, Natural Earth via `world-atlas`) | Core `Structure/MapService.cs`; lib `geo.ts`; views `MapView.svelte` |
| Agent framework adapters | Cli `Mcp.cs` (`Export`, `Invoke`); `documentation/AGENT-ADAPTERS.md`; `examples/agents/quantos_tools.py` |

### Added after 1.2 (from use)
| Feature | Main code |
|---|---|
| In-app updates | Core `Updates/UpdateService.cs`; Backend `Api/UpdateEndpoints.cs`; Client `Updater.cs`, `MainForm.ApplyUpdateAsync`; lib `updates.svelte.ts`; `components/UpdateSettings.svelte` |
| Theming (any colours, fonts, presets) | lib `look.ts`, `appearance.svelte.ts`; `components/ThemeEditor.svelte`; Backend `PUT /api/themes/{name}` |
| Notes browser (list and tree) | views `NotesView.svelte`; Backend `GET /api/nodes/excerpts` |
| Help and About | views `HelpView.svelte` (guides imported from `documentation/`), `AboutView.svelte` |
| Scratchpad send-to, docking | lib `scratch.ts`; `components/Scratchpad.svelte`, `ScratchTargets.svelte`; Backend `POST /api/nodes/{id}/append` |
| Sidebars | `components/LeftSidebar.svelte` (customise, drag to reorder), `RightSidebar.svelte` (split panels) |
| Convert to task, breadcrumbs, daily markers, subtask completion | lib `totask.ts`, views `NodeView.svelte`, `DailyStream.svelte`, Core `Tasks/TaskService.SetStatus` |
| Linked folders (Markdown from another app, read and written in place) | Core `Storage/Vault.cs` (linked roots, `linked/<name>/…` paths), `WriteService.cs` (adopt by id, last write wins), `Storage/VaultWatcher.cs`; Backend `Api/NodeEndpoints.cs` (`/api/linked-folders`); `components/WorkspaceSwitcher.svelte`; `documentation/STORAGE-SPEC.md` section 9 |
| Publishing a folder as a website | Core `Export/ExportService.cs` (`Publish`); Backend `POST /api/export/site`; `components/WorkspaceSwitcher.svelte` |
| Slash commands and the block menu | lib `editor/slash.ts` (the `/` menu), `editor/blockinfo.ts`, `editor/blockmenu.ts`; `components/BlockMenu.svelte` |
| MCP settings page | Core `Agents/McpTools.cs`, `Agents/McpService.cs`; Backend `GET/PUT /api/mcp`; `components/McpSettings.svelte` |
| Speed (0.1.5.2) | Core `Cache/CacheIndex.cs` (bounded queries, `aliases_id` index, schema v10), `Markdown/Frontmatter.cs` (parse once, cache `Get`/`Values`), `Storage/AtomicFile.cs` (retry capped at 7), `Storage/VaultWatcher.cs` (chunk size 50), `Structure/QueryService.cs` (bounded fetch, `FirstImages` scoped to results), `WriteService.cs` (no read-back after save); lib `docs.svelte.ts`, `state.svelte.ts`, `links.svelte.ts`, `editor/` (rebuild only on doc/viewport/line change) |
| Fixes after 0.1.5.2 | Core `Structure/QueryService.cs` (every note in the vault is considered, not just the first page), `Cache/CacheIndex.cs` (`Excerpts` reads the body from the full-text table), `Markdown/Frontmatter.cs` (the parsed values are dropped when the document changes) |
| Collapsible headings and spell check | Frontend `lib/editor/setup.ts` (heading fold compartment), `lib/editor/headings.ts` (level-aware folding and the arrow on each heading), `lib/editor/spellcheck.ts` (new), `lib/prefs.svelte.ts` (`collapsibleHeadings`, `spellCheck`), `components/views/SettingsView.svelte` (toggles), `lib/editor/theme.ts` (`cm-spell-error`, `cm-heading-toggle`) |
| Block selection, dragging blocks and the block menu on a selection | Frontend `lib/editor/blockselect.ts` (Ctrl-click selection, drag to move, the drop line), `lib/editor/blockinfo.ts` (`fencedLines`), `lib/editor/blockmenu.ts` and `components/BlockMenu.svelte` (the menu works on every selected block), `lib/editorMenu.svelte.ts` (`blocks`) |
| Single-exe build | Client hosts Kestrel in-process (`src/Quantos.Client/BackendConnection.cs` calls `Quantos.Backend.Program.Build`); no separate `Quantos.Backend.exe` to orphan |
| Header and toolbar test update (2026-10-23) | `components/Shell.svelte` (60px header row, including mobile), `components/Header.svelte` (23.75px wordmark); `lib/toolbar.svelte.ts` and `lib/customize.svelte.ts` remove three navigation actions from defaults and clean persisted toolbar settings; `components/ToolbarSettings.svelte` excludes them from editor options. Daily stream, My Day and Tasks remain commands and views. |
| Neutral dark background (2026-10-23) | `styles/tokens.css` uses a lighter neutral gray page background (`#2a2b2d`) with darker tinted gray cards (`#202123`); `lib/look.ts` uses the same neutral base when deriving custom accents. `Quantos.Client/MainForm.cs` matches the native window bar to the page background. |
| Unified sidebar card (2026-10-23) | `components/LeftSidebar.svelte` uses a subtly lighter single panel with unboxed navigation rows and quiet hover/active fills. |
| Window and scrollbar refinements (2026-10-23) | `CaptionButton.cs` matches the neutral gray chrome; `MainForm.cs` keeps a native sizing frame with working edge hit-tests and subtle DWM corners; shared CSS hides scrollbar visuals while keeping scrolling; `Header.svelte` shows directional sidebar toggle glyphs. |
| Split panes (2026-10-23) | `components/Pane.svelte` hides the visible tab-name strip, keeps a small close button at the active pane's upper-right, and highlights the focused pane with a quiet neutral outline and surface tint. `components/PaneArea.svelte` renders two separate neutral cards with an 8px gutter and keyboard-resizable divider; `commands.svelte.ts` and the command palette provide Next tab/Previous tab. `Shell.svelte` removes a redundant spacer row and outer pane frame; `RightSidebar.svelte` removes the full-height divider and matches its card surface. `Header.svelte` gives pressed sidebar toggles neutral gray backgrounds. `MainForm.cs` suppresses the bright DWM frame border after the window appears while retaining all-edge resizing. |
| Build package validation (2026-10-23) | `build/build.ps1` checks the Vite exit code immediately; it cannot package a stale `wwwroot/index.html` after a failed frontend build. |
| Right Tasks panel, header and window edge (2026-10-23) | `components/TasksPanel.svelte` removes a duplicate heading and nested task cards, and makes the quick-add field and task list compact. `RightSidebar.svelte` adds a consistent 8px right inset and puts Add a panel inside the last card. `Header.svelte` places New left with the workspace and Split and Quick Capture before Search; one Toolbar setting toggles labels on all four buttons. `Quantos.Client/MainForm.cs` hides DWM's border during non-client paint and after Windows theme/composition changes, with a blended-color fallback. |

## 4. Roadmap

From BUILD-PLAN section 14. Each needs a scope decision from the owner before it is assigned to a release.

| Item | Notes and dependencies |
|---|---|
| Broader content importing | Independent of the rest. Candidates: Obsidian (closest format), Notion export, Evernote `.enex`, OneNote, Apple Notes, plain Markdown folders. Imports should go through `WriteService` and preserve links. |
| Containerized shared-vault server | Built for the approved single-owner/device scope; see SERVER.md. The complete local backend remains in each app. |
| Multiuser accounts and sign-in | Still deferred. The current shared server is one owner's vault/devices with separate app and MCP keys, not multiuser collaboration. |
| Hosted migration | Portable container, persistent volume and migration procedure built; an actual hosted move remains unverified. |
| Offline editing and sync | Built with complete local vaults, persisted checkpoints and conservative conflict review. |

Gaps noticed along the way (not in the plan): code signing of the executables (SmartScreen and
antivirus warnings, and a stronger update trust anchor than the checksum alone); public Android signing/distribution;
`RELEASE-CHECKLIST.md` needs the 1.1/1.2 features.

## 5. Verification status

- Windows frame theme (2026-10-28): local private build `2026.28.10.16` retains release `0.2.3`.
  The main WebView sends resolved background/text/interaction colors through the existing trusted
  bridge on initial render and CSS changes. Native frame, caption controls and WebView background
  update together; malformed palettes and popup messages cannot recolor the main window.
  Five native client tests pass, including the regression that failed against the old code;
  124 frontend tests pass. Browser fixture `frontend/tests/windowAppearance.html` passed runtime
  dark/light, light startup, custom stylesheet/inline background, translucent and modern CSS color,
  deduplication and observer teardown checks. Svelte check has zero errors and nine existing warnings.
  Full package/ZIP validation passed with 242 backend/core and five native tests. Packaged self-check
  passed with zero warnings. The owner's test copy was replaced/restarted; all three executable
  hashes match and startup reports Page loaded / HTTP 200. Native visual and maximize/restore
  verification remains pending: Computer Use returned inaccessible control geometry and an unrelated
  desktop screenshot, so no successful installed-window theme interaction is claimed.

- Task popup (2026-10-27): Windows/browser clicks use the full editor over the retained workspace;
  Android stays on normal pages. Popup state is transient; API writes are observed for pending or
  failed changes without changing their return values or auth. Browser acceptance verified a mounted
  source at 2200px, editor focus return, title/source-label refresh, nested source links, title and
  reminder failure/retry, nested due-date prompt layering and Escape, disk-conflict resolution that
  remains resolved on reopening, and closing a saved popup when list refresh is offline. Automated
  tests cover platform routing, modified navigation, write completion ordering, failed-field retry,
  and clearing resolved body/raw conflict drafts. Native Android interaction was not exercised.
  Delivery: private Windows build `2026.27.10.14`, release `0.2.2`, passed 124 frontend / 242
  backend-core / four native tests, typecheck (zero errors, nine existing warnings), independent
  review, ZIP validation and packaged self-check (zero warnings). A 390px browser popup had no
  horizontal overflow. The owner's staging copy was installed/restarted; all three executable
  hashes match, Page loaded reports HTTP 200 and served assets report the new build ID. The native
  Windows accessibility tree showed the full task popup open. Close/scroll/failure acceptance was
  exercised in the browser; no Android APK or public release was produced.

- Sidebar task layout (2026-10-27, frontend build `2026.27.10.12`): Tasks and docked My Day use
  title-first rows with smaller metadata underneath, including assigned priority and distinct planned
  dates. Existing badges are preserved. Browser checks in the isolated development vault covered
  264–266px rows, absent metadata, long unbroken waiting text without horizontal overflow, task
  opening/completion, Alt+Down reorder with retained focus and unchanged full-page Tasks layout.
  All 111 frontend tests passed; svelte-check reported zero errors and nine existing warnings;
  Vite build and independent review passed. The subsequent Windows staging package is build
  `2026.27.10.13`, release `0.2.2`: all 111 frontend, 242 backend/core and four native tests passed;
  backend ran sequentially after one log-lock cleanup failure. ZIP validation and packaged self-check
  passed (zero self-check warnings). The owner's local staging copy was replaced and restarted;
  executable hashes match, startup reports Page loaded / HTTP 200 and served assets contain the build
  ID. No Android installation or public publication was performed.

Verified: everything listed in section 3 has unit or acceptance tests for its logic and was exercised
in a browser against the dev backend (screenshots and accessibility scans). The in-app update check and
download were tested against the real GitHub release.

Verified on the owner's machine: the update *install and restart* step in the Windows client. The
owner updated 0.1.5.1 → 0.1.5.2 through *Settings › Updates* and the new version started. (The
v0.1.5.2 asset first published was packed wrongly and the updater refused it; it was replaced with
one built by `build/build.ps1`.)

Not yet verified (needs the owner's Windows machine, or a real account):
- The pop-up scratchpad window's send keys (the docked scratchpad is tested).
- Real AI providers (tested only against a local fake OpenAI-style server); Claude via the Anthropic SDK.
- The manual items in `RELEASE-CHECKLIST.md` on the target laptop (no admin rights): packaging,
  tray, native dialogs, sleep and time-zone changes, global shortcuts.

Known: convert-to-task was reported not working in the pop-up scratchpad (fixed in 0.1.4: the window
had no shortcut handling).

## 6. Rules that hold the design together

- **Every vault write goes through `WriteService`.** Never write vault files directly. Derived data
  (cache, embeddings) lives in `client-data/cache` and must be rebuildable.
- **Files are the truth.** Notes are Markdown with YAML frontmatter and a ULID `id`. New features store
  their data as plain properties or Markdown in notes where possible (habits on daily notes, focus
  sessions under `## Focus`, mind-map annotations as footnotes, whiteboards as JSON Canvas). Document
  new keys in `STORAGE-SPEC.md`.
- **Actors and permissions.** Every request has an actor (user, CLI, agent, plugin, automation,
  system). Agents start read-only. Person-only actions (backups, updates, bulk replace, unlocking,
  theme files) check `ActorKind.User or Cli`.
- **Local and private by default.** The backend binds to 127.0.0.1. Nothing goes over the network
  unless the person asks (AI providers they configure, update checks); the map and fonts are bundled.
- **Accessible.** WCAG AA contrast, keyboard paths for every pointer action, labelled controls. Run axe
  on any new view.
- **User-facing text** is plain, specific British-leaning English with no jargon; the user guide is
  updated with every visible change.

## 7. Working on it

- **Build and test:** `pwsh build/build.ps1` (frontend build, all .NET tests, three self-contained
  single-file exes, zip in `publish/`). Quicker loops: `dotnet test tests/Quantos.Tests`,
  `cd frontend && npx vitest run`, `npx svelte-check --threshold error`.
- **Run for development:** `pwsh tools/dev/start.ps1` starts the backend against a copy of the example
  vault in `.dev/`, Vite and a headless Edge, and opens the app signed in; `tools/dev/edge.mjs` drives it
  and `tools/dev/axe.mjs` scans it. See [tools/dev/README.md](../tools/dev/README.md).
- **Frontend pitfalls seen in this codebase:**
  - `bind:view` to a component prop with a fallback can't start as `undefined`; start it as `null`.
  - Several `svelte-ignore` codes: one comment per code, stacked directly above the element.
  - The Vite dev server reloads modules with new URLs; a `import('/src/...')` from a test script can get
    a stale copy, so drive views through the UI when testing.
  - The editor binds `Mod-Enter`; window-level handlers that must win use the capture phase.
- **Windows specifics:** `build/build.ps1` has CRLF line endings. Single-file exes are deliberately
  uncompressed (startup); `IncludeAllContentForSelfExtract` stays on for the client (PowerShell).

## 8. Releasing

1. Follow [build/VERSIONING.md](../build/VERSIONING.md): release and build ID are separate. Advance
   `build/build-id.txt` once per new source/build set with `pwsh build/next-build.ps1`; variants share it.
   Bump the release version when requested: `build/build.ps1` (`$Version`), `frontend/package.json` and the top two
   `version` fields in `frontend/package-lock.json`, `<Version>` in the Backend, Cli and Client `.csproj`
   files, and the MCP `serverInfo` version in `src/Quantos.Cli/Mcp.cs`. Android's display/backend version
   follows the release; its versionCode is `major*10000 + minor*100 + patch`, like Lohe.
2. `pwsh build/build.ps1` (must pass all tests). A released build has no build mark: the mark that
   names the commit is only for a staging copy (`-Mark`), so the app tells them apart.
3. Commit "Version x.y.z", tag `vx.y.z`, push `main` and the tag.
4. Publish the versioned Windows ZIP and public release-signed ARM64 APK. Use numeric tag `vx.y.z`
   for legacy Windows updater compatibility, with the build ID in the release title and asset names.
   The ZIP is `Quantos-x.y.z-build-YYYY.DD.MM.revision-win-x64.zip`: the in-app updater accepts its suffix and needs
   the SHA-256 `digest` GitHub records for it.
5. Add the release to `CHANGELOG.md` and update this file.
6. **Stage the new test build into the owner's app copy and restart it.** The owner tests from
   `C:\Users\davem\Apps\Quantos\Quantos\application` (their vault is `C:\Users\davem\Quantos\Vault`), so a
   build that isn't put there isn't being used. `pwsh tools/dev/stage.ps1` closes the running client,
   replaces the three programs in that `application` folder with the ones from
   `publish\Quantos\application` — renaming the old files to `*.old` first, because a running `.exe`
   can be renamed but not overwritten — copies the updated documents across, and starts `Quantos.exe`
   again. Confirm it came up from the `Startup:` and `Page loaded` lines in
   `client-data\logs\client-<date>.log`.

### Task popup, navigation and editable #task schema (2026-10-26)

Private Windows build `2026.26.10.6`, semantic version `0.2.2`, is staged in the owner's test copy
and restarted (Page loaded / HTTP 200). Inline task text/chips open the full task editor in a native
HTML dialog while retaining the note's editor, cursor and viewport. Close/Escape saves and restores
focus without scrolling; source task labels refresh after a popup rename. Common task properties
now come from editable `#task` fields. The default definition is synthesized until explicitly saved
and applies to existing tasks, tag queries, counts, inheritance and migrations without rewriting
files. Field display-name edits retain stable keys; native task keys do not rekey unrelated schemas.
Header Back/Forward stays available on compact layouts and retains note scroll, including long
CodeMirror documents after their measured heights settle. Existing and pinned tab navigation is recorded.

Validated: 109 frontend / 242 backend-core / 4 native client tests; Svelte check has zero errors
and nine existing warnings. Full .NET tests passed sequentially after unrelated parallel native
sign-in and log-disposal flakes. Browser acceptance verified task values/custom fields, source title
refresh, a real #task form, keyboard focus/Escape, exact 3671px note return and navigation, and a
390px popup with no horizontal overflow. Package/archive validation and self-check passed with
zero self-check warnings. Native Android runtime/installation remains unverified for this change.

### Android note folders supersede task popup scope (2026-10-26)

The owner clarified that note folders on Android are the intended feature; task popups were removed.
Phone Notes browses folders/subfolders, shows direct notes and recursive folder counts, and uses
`folder:` tab arguments so Back/Forward and returning from a note retain the folder. Empty folders
are not listed. Private Debug ARM64 build `2026.26.10.10` keeps release version `0.2.2` and the phone's
existing signing certificate. The preceding Windows-only delivery did not fulfill this scope.

105 frontend tests passed; Svelte check has zero errors and nine existing warnings. The Android
build has zero warnings/errors. Native 411px SM-S948U touch testing verified opening nested folders
and a note, returning to its folder, parent navigation, Forward and New note saving in the current
folder; the installed build identity is confirmed and the task-popup bundle is absent.
Original active counts stayed 24 default / 614 Videos after test cleanup. Existing server DNS failure
was recorded before and after the update (zero sync conflicts); no sync-health claim is implied.
See ignored `.dev/note-folders-phone-results.json` and native screenshot. No public release occurred.

### Android icon and physical folder tree (2026-10-26)

Build `2026.26.10.11` is installed privately on the owner's phone. Android launcher and browser icon
use the Quantos Q rendered from the canonical SVG, replacing the mistaken publisher raster.
Phone Notes now uses the actual folder API, preserving containing directories and empty folders,
and includes Archive with labelled archived notes. A workspace picker and New folder action are
available directly in Notes. New note, breadcrumbs and history retain full physical directory paths;
older phone layouts migrate their previous Notes-relative paths.

111 frontend tests pass; Svelte check has zero errors / nine existing warnings. Android build has
zero warnings/errors. Actual phone taps verified a folder list matching the API (5/5 Default and
17/17 Videos), empty folders, Archive, creation in the selected folder and navigation. Active counts
were unchanged by acceptance cleanup. APK icon extraction and Android App info both confirmed the
correct Q icon. This supersedes the earlier active-note-only folder limitation; no Windows staging
or public release was performed.
