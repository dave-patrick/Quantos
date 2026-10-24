# Quantos project status and handoff

For anyone (person or AI agent) picking up work on Quantos. Read [HANDOVER.md](HANDOVER.md) (the owner,
open threads, the machine) and this, then
[BUILD-PLAN.md](../BUILD-PLAN.md) for the full plan and decision log, and [CHANGELOG.md](../CHANGELOG.md)
for what shipped when. Keep this file current when you finish a piece of work.

_Last updated: 2026-10-23, at version 0.1.6 (released as v0.1.6); local test build 0.1.6.1._

## 1. Where things stand

| | |
|---|---|
| Latest release | [0.1.6](https://github.com/dave-patrick/Quantos/releases/tag/v0.1.6) (Windows x64 zip, SHA-256 `969ec1b4131f7732bb65ee8da4ed8c6b80b03582387ae648c26092992715c53a`) |
| Branch | `main` (no long-lived branches; commits go straight to `main`) |
| Plan coverage | BUILD-PLAN Releases 1.0, 1.1 and 1.2 are built. Only the "Roadmap items without an assigned release" remain (section 4). |
| Tests | 151 .NET (xUnit, `tests/Quantos.Tests`) and 97 frontend (Vitest, `frontend/tests`); all pass. `svelte-check` 0 errors. |
| Current test build | Version 0.1.6.1; right sidebar panels (including My Day) resize and save their order and proportions. Notes separator resizes both side-by-side and stacked layouts; selected notes render in the lower preview card when stacked. Docked My Day task rows keep the title visible and show essential status/date without project and assignee chips. The native sizing frame is removed to eliminate the silver edge; custom hit testing keeps resizing available at each edge and corner. Titlebar move and restore now use form-level pointer capture to keep drags out of WebView2 text. Eighteen Demo records (two each for notes, daily notes, tasks, people, projects, events, goals, sprints and canvases) remain in the active vault; no separate demo vault is included. The latest titlebar correction is being built. No release published. |
| Accessibility | axe-core scans of every view show no serious or critical issues, in the built-in and custom themes. |

Version numbers are product versions (0.1.x) and are separate from the plan's "Release 1.x" names.
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
| Containerized backend | The backend is .NET and mostly portable; Windows-only parts (DPAPI key storage, the WebView2 client, PowerShell) need alternatives or to stay client-side. Precedes hosting. |
| Multiuser accounts and sign-in | Needed before any shared or hosted use. The permission system already has actors; there are no user accounts. BUILD-PLAN "Sign-in is deferred until multiuser". |
| Server hosting | Needs the two items above plus the rules in BUILD-PLAN section 15 (remote hosting). |
| Moving to the new deployment | Migrating a local vault to the hosted setup. |
| Offline caching and sync for remote use | The largest item; BUILD-PLAN recommends keeping device caches separate from authoritative storage, which the current design already does. |

Gaps noticed along the way (not in the plan): code signing of the executables (SmartScreen and
antivirus warnings, and a stronger update trust anchor than the checksum alone); touch and phone layout;
`RELEASE-CHECKLIST.md` needs the 1.1/1.2 features.

## 5. Verification status

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

1. Bump the version in six places: `build/build.ps1` (`$Version`), `frontend/package.json` and the top two
   `version` fields in `frontend/package-lock.json`, `<Version>` in the Backend, Cli and Client `.csproj`
   files, and the MCP `serverInfo` version in `src/Quantos.Cli/Mcp.cs`.
2. `pwsh build/build.ps1` (must pass all tests). A released build has no build mark: the mark that
   names the commit is only for a staging copy (`-Mark`), so the app tells them apart.
3. Commit "Version x.y.z", tag `vx.y.z`, push `main` and the tag.
4. `gh release create vx.y.z publish/Quantos-x.y.z-win-x64.zip --title "Quantos x.y.z" --notes-file <notes>`.
   The asset must be named `Quantos-x.y.z-win-x64.zip`: the in-app updater looks for that name and needs
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
