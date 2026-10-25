# Changelog

What changed in each Quantos release. Dates are release dates; versions follow `MAJOR.MINOR.PATCH`.
The plan these releases implement is [BUILD-PLAN.md](BUILD-PLAN.md); the current state of the project
and how to work on it is [documentation/PROJECT-STATUS.md](documentation/PROJECT-STATUS.md).
Release downloads: <https://github.com/dave-patrick/Quantos/releases>.

## Unreleased

- Workspace folders now decide a note's displayed scope even when externally managed frontmatter
  names a different workspace. Derived indexes rebuild without rewriting that source metadata.

- Optional host folder bridge shares externally managed Markdown folders through the server with
  offline clients, exact transfers, conflict/recovery copies and unavailable-drive/deletion guards.
  Server sync changes now enter its live index for MCP without requiring a restart.

- Android phones: choose and order bottom-bar shortcuts in Settings › Phone navigation. More stays
  available; choices persist on the device and leave tablet navigation unchanged.

- Simplified Android phone navigation with Notes, Tasks, Today and More, a focused notes list,
  separate Details screen, task Options and a settings section picker; tablets retain the workspace.
- Fixed the last right-panel close button: it now hides the sidebar and preserves it for reopening.

- An Android host for the existing Quantos interface and core, with phone/tablet layouts, touch
  splitters, system Back, Android Keystore, file import/export, PDF rendering/printing, notifications
  and an Android shell. Test APKs are separate from the Windows release.
- An optional portable shared-vault server, initially deployed privately on T580, with durable
  offline device copies, conditional synchronisation and explicit conflict review.
- Server MCP over Streamable HTTP, with a separate agent key. Settings › MCP can enable or revoke
  note/task write access on the server and copy a remote connection configuration.

## 0.2.0 (2026-10-24)

- **A more usable split workspace.** The header and panes align, split views read as separate cards, and active panes are easier to identify. The right sidebar supports multiple resizable panels and remembers their layout automatically.
- **Notes list and preview controls.** List rows stay on one line, and the preview pane can be toggled on or off with the choice remembered.
- **Compact task cards.** My Day rows preserve the task title while showing key status and date details without letting metadata take over the card. Priority colors are distinct: red P1, orange P2, green P3 and blue P4.
- **Frameless window drawing and resizing.** The app removes the unwanted silver Windows edge, supports resizing on every edge and corner, and refreshes the native frame/layout after maximize, restore, hide and reopen so controls stay aligned.
- **Delete a note from its editor.** The note header has a *Move to trash* action; trashed notes remain recoverable until permanently deleted.
- **Version number no longer gets cut off** in the bottom-right corner of the window.


## 0.1.6.1

- **Red overdue pill for past-due tasks.** The due chip now reads red when a task's due date has passed, and overdue tasks roll forward into today's daily note until they're done or retargeted.
- **Title bar fix.** Dropping the system menu removed the duplicate min/max/close buttons, so only the app's caption bar shows, with Windows 11's rounded corners.
- **Version in the corner.** The version sits in the bottom-right corner of the window.

## 0.1.6

- **Collapsible headings as slash commands.** Type `/` and choose *Collapsible heading 1*, *2* or
  *3* to write a heading that starts folded (`## [-] Title`); it is still folded when you open the
  note again, because the mark is saved in the file. *Fold the heading here* folds the section under
  the nearest heading, without writing anything to the note.
- **The window has rounded corners and its own title bar.** The client draws its own caption
  buttons — a thin dash, a square (two squares when the window is maximised) and a cross, each with a
  rounded hover patch and a red close — and carries the app's icon in the corner. The window keeps
  the Windows frame underneath, so it has the system shadow and Windows 11's rounded corners.
- **The daily stream opens with only today expanded.** Every other day starts closed, including days
  with writing in them; click a day to open it and it stays open.
- **Headings fold by level, with an arrow on every heading.** Each heading now has a small arrow in
  front of it: click it and the section under that heading folds away, down to the next heading of
  the same level, and a deeper heading goes with it. The setting *Settings › Writing › Collapsible
  headings* now really turns this off. A heading with nothing under it has no arrow, because there is
  nothing to fold.
- **Select several blocks and move them.** `Ctrl`-click adds a block to the selection and
  `Ctrl`-`Shift`-click stretches it over the blocks between. The selected blocks are shaded, can be
  dragged to a new place (a line shows where they will land), and the right-click menu now works on
  all of them at once: copy, duplicate, move up or down, delete, or turn them into tasks, headings,
  bullets or quotes.

## 0.1.5.7

- **Tabs can sit on any side of a pane.** Right-click a tab and choose *Move to left/right/top/bottom*;
  the split is made as needed. The settings navigation moves the same way.
- **Split panes with a draggable splitter.** The divider between the two panes can be dragged to
  give one side more room.
- **The window draws its own title bar.** The minimise, maximise and close buttons are drawn by
  the app instead of the system ones, and the version now sits next to the wordmark.

## 0.1.5.6

- **Tab location via right-click.** Right-clicking a tab now offers Move to left/right/top/bottom to relocate it to that pane, creating the split as needed.
- **MCP client points at the single-exe build.** (config) Updated OpenCode MCP config to use the installed single-exe CLI.

## 0.1.5.5 — Single-exe build

- **Single executable.** The Windows client now runs the backend in-process instead of launching
  `Quantos.Backend.exe` as a separate service. Closing Quantos ends the whole application, so there's no
  orphaned backend left behind and the next launch starts clean. One downside: browser sessions end when
  the client closes (the backend no longer outlives it).

## 0.1.5.4

- **Collapsible headings.** Markdown headings (`#`, `##`, …) can now be folded in the editor. On by default; turn it off in Settings › Writing.
- **Spell check.** Unknown words are underlined with a red wavy line as you type. Off by default; turn it on in Settings › Writing. Skips code blocks, quotes, lists and tables.

## 0.1.5.3 (2026-10-20)

Fixes for three problems in 0.1.5.2.

- **Queries see every note again.** 0.1.5.2 read only the first few thousand notes when filtering, so
  on a big vault a filtered view left the rest out and the number of results was short. Every note in
  the vault is considered now.
- **The notes browser shows the first lines of each note again.** 0.1.5.2 asked the cache for a
  column that isn't in that table, so the excerpts never arrived and the list's text search found
  nothing. The list works again.
- **A note's fields are read fresh after a change.** 0.1.5.2 kept the parsed frontmatter and didn't
  put it back after a change, so reading a field could give the value from before the change.

## 0.1.5.2 (2026-10-20)

A patch release that makes Quantos quicker and stops it hanging.

- **Quantos no longer hangs when a file is locked.** Saving a note retried for ever if Windows would
  not let go of the file; it now gives up after a few tries and says what happened.
- **Opening and saving notes is faster.** A note's frontmatter is parsed once and kept, instead of
  being read and parsed again on every lookup, and saving a note no longer reads it back off disk
  straight away.
- **The editor stops rebuilding itself** on every keystroke, so typing stays smooth in long notes.
- **The cache stays within bounds.** A query that would have read the whole vault now stops at
  50,000 rows, the cache has an index for link aliases, and its schema moves to version 10.
- **Watching a folder is quicker**, and the settings the app keeps are written less often, so the
  interface stops stuttering.
- **Finding a note by title is faster** when you type the start of the title.

## 0.1.5.1 (2026-10-19)

A patch release with the work that came after 0.1.5.

- **Right-click a task** in the task list (Tasks, My Day, the timeline) to mark it done, set it to
  doing or waiting, give it a due date or a priority, open it, or move it to the trash. In a note,
  right-clicking a task line can move the task itself to the trash, or take just the line out of
  the note.
- **Delete a task without opening it:** hover the row in a task list and press the **×** at the end
  of it. It goes to the trash, and the toast offers an undo.
- **Trashing a task takes its subtasks with it**, and restoring it brings them back.
- **Right-click a file in the file tree** for what can be done with it: open it, open it in the
  other pane, rename it, export it as Markdown or HTML, or move it to the trash.
- **The quick capture window takes Ctrl+T** for a task, and the "send to" keys you set for the
  scratchpad work there too.
- **Vault diagnostics** say what they found: the check now reports how many files it looked at and
  how many problems it found, instead of leaving the button looking idle. It also names a linked
  folder that has gone, a folder that can't be read, two linked folders over the same files, and a
  task line bound to a note that no longer exists.
- **Backups cover linked folders.** Folders linked in from elsewhere on the computer hold notes
  too, so they are now backed up with the vault, under the same `linked/<name>/` names. A restored
  backup points at its own copies, and restoring a single note from a linked folder brings it into
  the vault instead of overwriting the other app's file.
- **Publish a folder as a website:** *Publish this folder…* in the workspace menu makes a small
  site from the notes of a workspace, or of a folder linked in from elsewhere: a page per note with
  its backlinks and outgoing links, an index you can search, and a graph of how the notes connect.
  It is written to the vault's `exports` folder and can be put on any web host.
- The agent settings page is called **MCP**, and the release checklist now covers writing, linked
  folders, agents and security, not just 1.0.

## 0.1.5 (2026-10-19)

- **Linked folders:** link a folder of Markdown from another app and Quantos reads and writes those
  files where they are, so both apps work on the same notes — nothing is moved, copied or deleted.
  *Link a folder…* in the workspace menu; linked folders are listed as their own group and can be
  unlinked from the same menu. Quantos adds only an `id` to a file, and that id comes from the
  file's path, so a note keeps its identity even when the other app rewrites it. Pictures and files
  that sit next to a note show up, links to the note next door work, and task lines in a linked
  folder become real tasks. If both apps write at once, the newest write wins and the other
  version is kept in history.
- **Settings › MCP:** set an AI agent up without hunting for paths. It shows the program the
  agent runs, the settings for Claude Desktop, Claude Code, Cursor, Codex and OpenCode ready to
  copy, and the nine tools an agent gets.
- **Slash commands:** type `/` for a list of things to put in the note — a task, a heading, a list,
  a quote, a callout, a code block, a table, a divider, today's date.
- **Right-click a block** for what can be done with it: copy, duplicate, move, delete, or turn it
  into a task, a heading, a bullet or a quote. Tasks offer more: mark done, set to doing or
  waiting, set a due date or a priority. `Shift+F10` opens the same menu.
- **Custom styles** in Settings › Appearance are applied with buttons: one style at a time, "None"
  for the built-in look. Vaults that kept a list of enabled styles are moved to the new setting
  the first time they open.
- Right sidebar splits into two panels, each with its own tabs, with a draggable divider.
- Development harness in `tools/dev/` and handover notes in `documentation/HANDOVER.md`.

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
