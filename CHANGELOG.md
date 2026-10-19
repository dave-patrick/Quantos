# Changelog

What changed in each Quantos release. Dates are release dates; versions follow `MAJOR.MINOR.PATCH`.
The plan these releases implement is [BUILD-PLAN.md](BUILD-PLAN.md); the current state of the project
and how to work on it is [documentation/PROJECT-STATUS.md](documentation/PROJECT-STATUS.md).
Release downloads: <https://github.com/dave-patrick/Quantos/releases>.

## Unreleased

- Right sidebar splits into two panels, each with its own tabs, with a draggable divider.

## 0.1.4 (2026-10-19)

- Scratchpad send-to keys move the selection (or the paragraph at the cursor) to today's daily note,
  a new note, a new task or a chosen note (optionally under a heading); destinations and keys are
  user-defined in Settings › Shortcuts.
- Scratchpad docks in the right sidebar (same note as the pop-up).
- Editing commands (Convert to task, AI) work in the scratchpad window.
- Settings › Shortcuts shows the global shortcuts actually registered (it showed a never-registered
  placeholder, `Ctrl+Shift+Space`), warns when another program owns one, and records new ones by key press.

## 0.1.3 (2026-10-19)

- In-app updates: check GitHub, download and verify against the release's SHA-256 digest, and (Windows
  client) install and restart, keeping the old program files until the new version starts.
- Help (`F1`) with the guides and live keyboard shortcuts, searchable and offline; About Quantos.
- Notes browser Tree layout with drag-to-move; daily stream content markers and *Hide empty days*;
  drag-and-drop sidebar ordering; completing a task completes its open subtasks.
- Faster start: single-file executables are no longer compressed; the client starts WebView2 while
  the backend starts; startup timings are logged.
- Interface size applies on slider release.

## 0.1.2 (2026-10-19)

- Theming: presets, any background/text/accent colour (kept readable), fonts per role, interface size,
  save a theme as a vault style.
- Notes browser (`Ctrl+Shift+B`), clickable breadcrumbs, Convert to task (`Ctrl+Alt+T`), customisable
  left sidebar, the wordmark in the header.
- Open notes re-check their files when the window regains focus (quick capture from the pop-up shows
  in the open daily note).

## 0.1.1 (2026-10-19): first public release

Everything in BUILD-PLAN Releases 1.0, 1.1 and 1.2.

**Release 1.0 foundation:** Markdown vault with YAML frontmatter and ULID ids, one write service with
journal, history, trash and reconciliation; derived SQLite cache; localhost backend with token and
cookie handoff; Svelte + CodeMirror 6 interface in a WebView2 Windows client and the browser; tasks as
first-class nodes with inline checkbox sync, My Day, timeline, boards, recurrence and reminders;
supertags, templates, queries and saved views; links, embeds, synced blocks, graph; capture,
scratchpad, PowerShell terminal, plugins sandbox, automations, safe mode; CLI and MCP server for
agents; export to Markdown, HTML and PDF; accessibility to WCAG AA.

**Release 1.1:** custom styling and density; CSV import/export; in-app AI (local and remote providers,
Ask your notes, meaning search, AI commands); rolling backups; math and Mermaid; read-only notes and
reading mode; find and replace across notes; detailed revision comparison; presentation mode; focus
timer; habits and metrics; sprints; goal rollups and task analytics; audio recording.

**Release 1.2:** dependency Gantt; whiteboards (JSON Canvas); mind maps with annotations; global
knowledge graph; offline map of located notes; agent framework adapters (`quantos-cli tools` / `call`).

## 0.1.0

Internal builds during the Release 1.0 work; not published.
