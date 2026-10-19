# Quantos: Complete Build Plan (Revised)

Every decision in this plan is confirmed. Anything marked **Recommended design** is a proposed technical approach that has not been separately approved.

---

## 1. Product objective

Build a personal productivity app where:

1. Every note, task, project, person, calendar entry, and custom object is a first-class node.
2. Nodes are stored as Markdown files with YAML frontmatter.
3. The interface supports both freeform writing and structured views.
4. Tasks and references stay live across notes and dashboards.
5. Files stay readable and recoverable outside the app.
6. The same interface works in a browser and in the Windows client.
7. Later deployment changes don't require rebuilding the product.

---

## 2. Release boundaries

### Release 1.0 (confirmed)

1. Local backend, not containerized.
2. The authoritative vault lives on your laptop.
3. Windows client that needs no installation. Creating local folders and files is acceptable.
4. Browser access to the same local backend.
5. Single user, no sign-in.
6. PowerShell runs locally.
7. Closing the Windows client window minimizes it.
8. Agent and plugin access is governed by permissions.
9. Importing is minimal at first.
10. No built-in AI engine.
11. Notes and tasks are always available offline, because the vault is local.

### Roadmap (confirmed)

1. Containerized deployment.
2. Server hosting and moving the vault to the server.
3. Offline caching and sync for remote hosting.
4. Multiuser support and sign-in.
5. Broader content importing.
6. Configurable backups in Release 1.1.
7. The existing Release 1.1 and 1.2 features are kept.

> **Important:** Release 1.0 doesn't need remote sync. Browser and Windows client changes both go to the same local backend and vault.

---

## 3. Application architecture

### A. Shared frontend

**Recommended design:** One frontend codebase, written in TypeScript, used by both clients.

**Confirmed:** Built with Vite and Svelte. The editor engine is CodeMirror 6, because it edits the Markdown source directly and leaves untouched text exactly as written, instead of rebuilding the Markdown from a document model on save.

It handles:

- Editing and reading.
- Daily stream and timeline.
- Tasks and structured views.
- Navigation, tabs, and split panes.
- Search and inspectors.
- Settings and diagnostics.
- Save, connection, and conflict status.

The frontend never writes vault files directly.

### B. Local backend

**Confirmed:** A .NET 10 web service, published as a self-contained single file.

It handles:

- Creating and updating nodes.
- Reading and writing Markdown.
- All vault changes.
- Search indexing.
- Task sync.
- Supertag and template processing.
- Watching files and detecting conflicts.
- History, trash, and restore.
- Reminder scheduling.
- Export generation.
- Permission enforcement.
- The command-line (CLI) and agent interfaces.

### C. Windows client (no install)

**Confirmed:** A .NET 10 app, published as a self-contained single file, that shows the frontend through WebView2.

It handles:

- Showing the shared frontend.
- Starting the local backend or connecting to one that's already running.
- Registering global capture shortcuts.
- Capture and scratchpad windows.
- Hosting local PowerShell, using the PowerShell engine built into .NET.
- Native desktop integration.
- Minimizing when the main window is closed.
- An explicit Exit command.

**Recommended design:** Native functions sit behind a narrow bridge that checks permissions. Document content never gets unrestricted access to Windows functions.

### D. Browser client

It handles:

- Showing the same frontend.
- Connecting to the running local backend.
- Core productivity features.

These features need the Windows client:

- Global capture shortcuts.
- The always-on-top scratchpad.
- Native notifications.
- The local PowerShell terminal.

In the browser, the terminal drawer is hidden. The other three features show a "Requires Windows client" label.

**Recommended behavior:** Browser-only use requires the backend to be running. Closing the browser doesn't stop a backend that the Windows client owns.

### E. Process ownership

**Recommended design:**

1. One backend process owns each vault.
2. Extra windows, browser sessions, CLI commands, and capture windows all connect to that process.
3. A second client never becomes a second independent writer.
4. Exit warns you about pending saves and stops the backend only after a safe shutdown.
5. The backend holds an exclusive lock file in the vault (`Vault/.state/backend.lock`) for as long as it runs. A second backend that can't get the lock refuses to start.
6. The running backend writes a discovery file to `client-data/settings/backend.json` with its port, process ID, and access token. Only the current user can read it. Clients, the CLI, and capture windows read this file to find and connect to the running backend, and treat a file whose process ID is no longer running as stale.

### F. Technology stack (confirmed)

| Component | Technology |
|---|---|
| Windows client | .NET 10 self-contained single file with WebView2 |
| Backend | .NET 10 web service, self-contained single file |
| PowerShell | Engine built into .NET |
| Derived data cache | SQLite |
| Frontend | TypeScript |

Why this stack:

- One language covers the client, the backend, and PowerShell.
- No runtime install is needed.
- It doesn't need admin rights.

---

## 4. Storage and data ownership

### Authoritative content (stored in the vault)

1. Markdown node files.
2. Task Markdown files.
3. Supertag definitions.
4. Templates.
5. Canonical synced-block files.
6. App configuration.
7. Notification records.
8. History snapshots.
9. Trash contents and restore manifests.

### Derived data (stored outside the vault)

1. Search index.
2. Backlink and forward-link indexes.
3. Task dashboard views.
4. Property usage counts.
5. Attachment reference counts.
6. Graph relationships.
7. Checklist and effort rollups.

Rules:

- Derived data lives only in `client-data/cache/cache.sqlite`.
- It can be deleted and rebuilt at any time.
- No irreplaceable record exists only in the cache.

### Folder structure

```
Quantos/
  application/
  client-data/
    settings/
    cache/
      cache.sqlite
    logs/
Vault/
  workspaces/
    default/
      daily/
      nodes/
      tasks/
      archive/
      supertags/
      workspace-state.json
  attachments/
  supertags/
  templates/
  synced_blocks/
  .trash/
  .history/
  .state/
    notifications.jsonl
    journal/
  plugins/
  themes/
  exports/
  workspaces.json
  settings.json
  automations.json
  shortcuts.json
  toolbar.json
  views.json
  task-dashboard.json
  dashboard.json
```

### Vault rules

- The vault holds authoritative content only.
- `Vault/supertags/` holds global supertag definitions.
- `Vault/workspaces/<name>/supertags/` holds definitions scoped to one workspace.
- The vault location can be chosen separately from the application folder.
- The default vault location is `C:\Users\<you>\Quantos\Vault`.
- If the chosen vault is inside OneDrive or another synced folder, the app shows a warning that names the risks: file locks, conflict copies, and corruption.

### Naming

1. Filenames for nodes you create use lowercase with underscores.
2. Readable titles are kept in the frontmatter.
3. Daily files use the date-based naming convention.
4. System files and attachments follow their own naming rules.
5. Renaming keeps the node's identity and updates every reference to it.

---

## 5. Node and relationship model

### Universal node fields

1. Permanent identifier that never changes. **Confirmed:** a ULID, stored in the frontmatter `id` field.
2. Title.
3. Workspace.
4. Supertag assignments.
5. Properties.
6. Markdown body.
7. Created and modified details.
8. Archive and trash state.
9. Schema version number, used for future upgrades.

### Relationships

**Recommended design:** Links resolve through the permanent identifiers but display readable titles.

Supported relationship types:

1. Node links and aliases.
2. References to people and projects.
3. Parent and child tasks.
4. Task dependencies.
5. Block references.
6. Synced-block references.
7. Attachment references.
8. References across workspaces.

### Duplicate names

**Recommended design:**

1. Look in the active workspace first.
2. If the match is still ambiguous, show a selection prompt.
3. Never silently pick between duplicate titles or aliases.

### Supertags

Supported:

1. Global definitions by default.
2. Workspace-specific scope.
3. Inheriting fields from a parent supertag.
4. Namespaced properties when names conflict.
5. Linked templates.
6. Layouts specific to each type.
7. Metadata dictionary and schema explorer.

**Recommended safeguards:**

1. Block inheritance loops.
2. Keep existing values when a schema change is incompatible.
3. Require an explicit choice before migrating data.
4. When renaming a tag, update real tag references only. Leave code examples and history snapshots unchanged.

---

## 6. Safe editing and saving

### One write path

Every write in the app goes through one service:

1. Editor saves.
2. Table cell commits.
3. Task changes.
4. Capture.
5. CLI operations.
6. Automations.
7. Agent requests.
8. Renames and moves.
9. Restore and rollback.

### Recommended write steps

1. Identify the target and the version it's expected to be at.
2. Check permissions and the operation's settings.
3. Check whether the file changed in the meantime.
4. Save recoverable history.
5. Write the change safely.
6. Update the derived indexes.
7. Notify connected clients.
8. Report success only after the save has finished.

**Confirmed for step 5:** Write to a temporary file in the same folder, flush it to disk, then swap it into place with `File.Replace` (or a move, for new files). A save that is interrupted leaves either the old file or the new one, never a partial file.

**Confirmed for step 2:** The permission check exists from the first version of the write service. The local interface is allowed everything, but every write still passes through the check, so agents, plugins, and the CLI can be restricted later without adding new write paths.

### Startup reconciliation

**Confirmed:** On every start, before accepting writes, the backend scans the vault for files that were added, changed, or removed while it was stopped (by comparing modification times and content hashes against the cache). It finishes or rolls back any interrupted journal operations, records history for files that were changed outside the app, and updates the derived indexes. The file watcher (Stage 2) covers changes made while the backend is running.

### Changes that touch several files

These use a recoverable operation journal:

1. Renames that update links.
2. Moves between workspaces.
3. Extracting and merging nodes.
4. Syncing a task with its source note.
5. Batch moves.
6. Supertag renames.

**Recommended design:** Before finishing or undoing an interrupted operation, recovery checks the current version of every affected file.

### Editing outside the app

1. Watch for file changes made outside the app.
2. Group change events with a 300ms sliding debounce (confirmed).
3. Process outside changes through the same indexing and conflict checks.
4. Keep any edits in the app that haven't been saved yet.
5. When a conflict happens:
   - Show a side-by-side comparison of the app version and the disk version.
   - You choose which version becomes the main copy.
   - The other version is always saved as a recoverable copy.
   - No choice ever silently throws content away.

> **Note:** The debounce doesn't guarantee safe writes. The write steps and conflict checks do.

### Keeping Markdown intact

**Recommended design:**

1. Leave untouched parts of the file exactly as they were.
2. Keep unknown YAML properties and unsupported syntax.
3. Write down the formatting cleanup rules explicitly.
4. Offer raw-source editing when the visual editor can't safely preserve something.
5. Test load and save behavior before turning on advanced editor features.

---

## 7. Interface and workspace behavior

### Left sidebar

1. Navigation and pinned views.
2. Saved queries and virtual folders.
3. Folder navigation.
4. Supertag hierarchy.
5. Templates.
6. Metadata dictionary.
7. Vault diagnostics.
8. Attachment auditor.
9. Trash.

### Right sidebar

1. File tree for the active workspace.
2. Archive.
3. File filtering and multi-select.
4. Docked property inspector.
5. Forward links and backlinks.
6. Notifications.
7. Local graph two hops out.

### Center area

1. Quick-actions toolbar.
2. Back and forward history.
3. Ten tabs per pane (confirmed).
4. Side-by-side and top/bottom splits.
5. Daily stream.
6. Editors and structured views.
7. PDF viewer.
8. Bottom terminal drawer (Windows client only).
9. Status indicators.

### Behavior

1. Properties appear after the title and before the body.
2. Shift-click opens a link in the other pane (confirmed).
3. Switching workspaces restores the layout and scroll positions.
4. Empty daily entries collapse (confirmed).
5. Date indicators use the device's current time zone.
6. If every tab is pinned when you hit the tab limit, the app asks you to unpin one.
7. **Recommended:** Unsaved content is saved before a tab is closed to make room.
8. **Recommended:** Once remote hosting arrives, layout state is stored per device.

---

## 8. Feature workstreams (Release 1.0)

### A. Writing and navigation

1. Markdown editor and raw-source mode.
2. Folding for headings and nested lists.
3. Toggle headings and collapsible callouts.
4. Multi-column layouts.
5. Outline zoom and breadcrumbs.
6. Typewriter scrolling.
7. Word count, character count, and reading time.
8. Writing targets.
9. Floating table of contents.
10. Footnote previews on hover.
11. Multiple cursors and column selection.
12. Aliases and namespace navigation.
13. Hover previews.
14. Document cards.
15. Extracting and merging nodes.
16. Recent-document navigation.
17. Copying as rich text or Markdown.

### B. Tasks and daily planning

1. Create tasks from any entry point, as first-class nodes.
2. Inline checkbox sync.
3. Completed-task drawer.
4. Priorities and colored accent strips.
5. Due dates and times.
6. Planned dates.
7. Effort estimates and rollups.
8. Assigning people and projects.
9. Waiting-on tracking.
10. Subtasks with their own dates and assignees.
11. Dependencies.
12. Custom project status stages.
13. My Day queue.
14. Review of stale tasks.
15. Time blocking and daily timeline.
16. Recurrence.
17. Reminders.
18. Calendar file generation.

### C. Structured data and discovery

1. Supertags with inherited fields.
2. Templates, including automatic application.
3. Structured paste and quick-add parsing.
4. Forms with strict validation.
5. Freeform editing that warns instead of blocking.
6. Spreadsheet-style keyboard navigation.
7. Table, list, Kanban, grid, and gallery views.
8. Multi-level sorting and grouping.
9. Saved queries and virtual folders.
10. Embedded query blocks.
11. Editable data views that span notes.
12. Synced blocks and embedding blocks from other notes.
13. Global search grouped by workspace.
14. Backlink filtering and forward-link inspection.
15. Graph two hops out.
16. Metadata dictionary.

### D. Files and recovery

1. File trees per workspace.
2. Drag-and-drop organization.
3. Batch actions on multiple selections.
4. Prompts when names collide.
5. Archive and unarchive.
6. Global attachments with duplicates removed.
7. Cover images on cards.
8. PDF viewing and citations.
9. Auditing for orphan attachments.
10. Trash and restore. Trash never expires automatically (confirmed).
11. Version history and rollback.
12. Integrity diagnostics.
13. Exporting documents and folders.
14. Query results frozen as static content during PDF export.

### E. Windows integration and extensibility

1. Global quick capture.
2. Routing captures to a workspace.
3. Always-on-top scratchpad.
4. Native notifications.
5. Notification history.
6. Syntax highlighting and code copying.
7. Local PowerShell terminal.
8. Command palette.
9. Shortcut configuration with conflict warnings.
10. Toolbar customization.
11. Themes and typography.
12. CLI with readable tables and optional JSON output.
13. Agent interface controlled by permissions.
14. Plugins and safe mode.
15. Automations triggered by property changes.

---

## 9. Task rules

### Deletion (confirmed)

1. Deleting a task deletes the live task itself.
2. The task goes to trash.
3. Every other place that showed the task displays a deleted-task placeholder with a Restore option.
4. Restoring reconnects the same task.

**Recommended design:** Treat moving a task line differently from deleting it, so reorganizing never trashes a task by accident.

### Rollover (confirmed)

1. Changes the planned date.
2. Doesn't change the due date automatically.
3. Keeps the context of the original daily note.

### Recurrence (confirmed)

1. Each occurrence is a separate task.
2. The next occurrence is calculated from the scheduled date.
3. Each occurrence keeps its own completion history.

**Recommended design:** Store a series ID and a scheduled occurrence ID, so the same occurrence is never generated twice.

### Reminders

**Recommended design:**

1. Track reminders for each occurrence.
2. Record snooze times explicitly.
3. Re-check reminders after sleep and after a restart.
4. Don't show duplicate alerts when several clients are open.
5. Record the time zone used for scheduled dates and captures.

---

## 10. Permissions and local execution

### Confirmed

1. What agents and plugins can do depends on permissions.
2. PowerShell runs locally.
3. Sign-in is deferred until multiuser.

### Recommended permission categories

1. Read content.
2. Create and edit content.
3. Move and archive.
4. Trash and restore.
5. Permanently delete.
6. Run local commands.
7. Access files outside the vault.
8. Make outbound network requests.

### Safeguards (confirmed)

1. Code found in notes never runs automatically.
2. Before approval, show exactly what will run and where.
3. Keep local shell access separate from normal editing.
4. Enforce permissions in the backend and the native bridge.
5. Don't give the frontend unrestricted native messaging.
6. The backend accepts connections only from the local machine.
7. Content inside the vault is treated as data. It can never grant itself permissions.
8. The backend generates a new random access token at every launch, and every request must include it.
9. The backend rejects requests from web pages it doesn't expect (origin checks).
10. Shell execution and permanent deletion always require approval in the Windows client. A browser request alone can never trigger them.

### Access token handoff and request checks

**Confirmed:**

1. The backend listens on 127.0.0.1 only.
2. The Windows client passes the access token to its WebView2 frontend directly through the native bridge.
3. To open the app in a browser, the Windows client or the CLI opens a URL containing a one-time code that expires after a short time. The backend exchanges the code for an `HttpOnly`, `SameSite=Strict` session cookie and then redirects to a clean URL, so the token never stays in the address bar or history.
4. The backend rejects any request whose `Host` header isn't the expected local address and port. This blocks DNS-rebinding attacks that would otherwise get around origin checks.
5. The backend rejects any request whose `Origin` header is present and isn't the backend's own origin or the Windows client's origin.

---

## 11. Implementation stages

These stages set the build order for Release 1.0. They don't cut any features.

### Stage 1: Foundation

1. Build a portable "hello world" version and test it on your KPMG laptop without admin rights. It is a .NET 10 Windows client showing a page through WebView2, a .NET 10 backend on 127.0.0.1 that reads and writes a SQLite file, and a PowerShell engine check. Check each of these:
   - The executables run after being copied or downloaded onto the laptop (Mark-of-the-Web and SmartScreen), unsigned.
   - AppLocker or WDAC doesn't block the executables or the native libraries that single-file builds unpack, including SQLite's `e_sqlite3.dll`.
   - The WebView2 Evergreen runtime is installed, and its user-data folder under `client-data/` is writable.
   - The backend can listen on 127.0.0.1 with no firewall prompt, and the corporate proxy doesn't interfere with local requests.
   - Which language mode the built-in PowerShell engine runs in. WDAC forces ConstrainedLanguage mode, which limits the Stage 6 terminal.
   - Antivirus doesn't quarantine either executable.

   Choose a fallback before running the test, in case something is blocked: browser-only use, a framework-dependent build, or code signing.
2. Write the storage and schema document: node frontmatter keys, ULID identifiers, the schema version, file naming, and the folder layout. Changing these is cheap only before any vault exists.
3. Project structure: a solution for the backend, the Windows client, the shared library, and the tests, plus the Vite and Svelte frontend.
4. Automated test harness, running the storage and recovery acceptance tests against a temporary vault.
5. Shared frontend shell.
6. Starting and stopping the local backend: vault lock file, discovery file, connecting to a backend that's already running, close-to-minimize, and a safe Exit.
7. Security: a random access token at each launch, the token handoff to WebView2 and the browser, and `Host` and `Origin` checks.
8. Vault selection, including the warning for synced folders.
9. Node identity, the schema version, and the Markdown reader.
10. The single write service, with atomic writes, the permission check in place, and the operation journal.
11. History and trash.
12. Startup reconciliation and cache rebuild.
13. Communication between client and backend, including a push channel that notifies connected clients of changes.

**Done when:**

1. The portable build runs on the KPMG laptop without admin rights, or a fallback is chosen and documented.
2. You can create, edit, restart, restore, and rebuild without losing any authoritative content.
3. Edits made outside the app while the backend is stopped are picked up at the next start.
4. Requests without the access token, from an unexpected origin, or with an unexpected `Host` header are rejected.
5. The Storage and recovery acceptance tests in Section 13 pass automatically.

### Stage 2: Core writing workflow

1. Editor.
2. Daily notes and daily stream.
3. Workspace switching.
4. File trees.
5. Tabs and split panes.
6. Capture.
7. Search.
8. Conflicts from editing outside the app, with the side-by-side comparison.

**Done when:** The browser and the Windows client can both safely edit the same local vault.

### Stage 3: Tasks and planning

1. Sync between inline tasks and full tasks.
2. Deleted-task placeholders.
3. Planned dates and rollover.
4. Recurrence.
5. Subtasks and dependencies.
6. My Day.
7. Reminders and notification history.
8. Timeline and calendar export.

**Done when:** A task's identity and state match everywhere it appears.

### Daily-use checkpoint

1. Use the app for your real daily work for at least one week.
2. Log every problem you hit.
3. Fix the design issues before starting Stage 4.

### Stage 4: Structured organization

1. Supertags.
2. Inheritance.
3. Templates.
4. Forms.
5. Properties.
6. Tables and saved views.
7. Query blocks.
8. Metadata dictionary.
9. Structured paste and quick add.

**Done when:** Structured edits save correctly and freeform Markdown editing still works without being blocked.

### Stage 5: Advanced editor and relationships

1. Embedded blocks and synced blocks.
2. Editable views that span notes.
3. Hover previews.
4. Backlink filters.
5. Graph neighborhood.
6. Advanced writing controls.
7. Extracting and merging nodes.

**Done when:** Advanced content survives being opened and saved, and survives edits made outside the app.

### Stage 6: Windows integration and extensibility

1. Native capture and scratchpad.
2. Local terminal.
3. Shortcut management.
4. CLI.
5. Agent interface.
6. Plugins and safe mode.
7. Automations.

**Done when:** Native actions respect permissions and can't get around the write service.

### Stage 7: Hardening and release packaging

1. Export behavior.
2. Diagnostics.
3. Performance testing.
4. Recovery testing.
5. Final no-install packaging.
6. Upgrade and migration behavior, using the schema version.
7. User guide, including the manual backup procedure.
8. Release checklist.

**Done when:** Every committed Release 1.0 feature passes its acceptance tests.

---

## 12. Manual backup procedure (Release 1.0; replaced by rolling backups in 1.1)

This goes in the user guide until configurable backups arrive in Release 1.1.

1. Use Exit in the Windows client so the backend shuts down safely.
2. Copy the whole Vault folder to your backup location.
3. You don't need to back up `client-data/cache/`, because it can be rebuilt.
4. To restore, copy the Vault folder back, start the app, and run the cache rebuild.

---

## 13. Acceptance tests

### Storage and recovery

1. Delete the cache and rebuild all derived data.
2. Interrupt a save.
3. Interrupt a rename that spans several files.
4. Restore a file into a name that's already taken.
5. Restore a deleted task that several notes refer to.
6. Confirm trash never expires automatically.
7. Confirm history is still readable after a node moves.
8. Confirm the cache doesn't exist inside the vault.
9. Pick a OneDrive vault path and confirm the warning appears.
10. Restore from a manual backup and rebuild the cache.

### Editing and sync

1. Edit the same node from the browser and from the Windows client.
2. Capture while that day's daily note is open with unsaved edits.
3. Edit a file outside the app while it's open in the app. Confirm the comparison appears and both versions are kept.
4. Move task lines and confirm the tasks aren't deleted.
5. Copy task references without accidentally creating duplicate tasks.
6. Confirm unknown YAML and unsupported Markdown are preserved.

### Interface

1. Open an eleventh tab in one pane.
2. Repeat with every tab pinned.
3. Switch workspaces and confirm the layout is restored.
4. Keep editing past midnight.
5. Resume after the laptop sleeps.
6. Change the device time zone.
7. Navigate tables using only the keyboard.
8. Shift-click a link and confirm it opens in the other pane.
9. Confirm the browser hides the terminal and labels the features that need the Windows client.

### Tasks

1. Generate recurring occurrences with no duplicates.
2. Complete, snooze, trash, and restore a task.
3. Roll over planned dates and confirm due dates don't change.
4. Check dependencies and subtasks.
5. Confirm reminders aren't duplicated when several clients are open.

### Permissions and security

1. Deny an agent write.
2. Deny shell execution.
3. Try to trigger a native action from untrusted content.
4. Turn off plugins with safe mode.
5. Confirm permanent deletion requires approval in the Windows client.
6. Send a request without the access token and confirm it's rejected.
7. Send a request from an unexpected web origin and confirm it's rejected.
8. Try shell execution from the browser and confirm it needs approval in the Windows client.

### Packaging

1. Launch without installing anything.
2. Run without admin rights on the KPMG laptop.
3. Move the application folder.
4. Use a vault that's outside the application folder.
5. Minimize and reopen.
6. Exit safely.
7. Upgrade without losing settings or content.

**Recommended:** Set performance targets using a realistic test vault. Don't claim any speed or capacity until it's been measured.

---

## 14. Roadmap

### Release 1.1: Enhancement release

1. Configurable rolling backups, stored locally or in a chosen persistent location, with clear retention and restore behavior. *(Built.)*
2. Audio recording and playback. *(Built: record into a note or the daily note, stored as an attachment, inline player with speed.)*
3. Habit and metric tracking. *(Built: trackers as daily-note properties, grid, streaks, goals, averages.)*
4. Presentation mode. *(Built: slides from `---` breaks or headings, speaker notes, full screen.)*
5. Detailed comparison between revisions. *(Built: any version against now or its predecessor, word-level, side by side, properties apart.)*
6. Mermaid diagrams. *(Built.)*
7. Read-only mode. *(Built: per-note lock enforced by the write service, and reading mode per device.)*
8. Custom styling. *(Built.)*
9. CSV import and export for tables. *(Built.)*
10. Focus timer and work-session tracking. *(Built: sessions in the daily note and the task's `time_spent`, totals per day and task.)*
11. Math rendering. *(Built.)*
12. Sprint and iteration planning. *(Built: sprint notes, backlog, capacity, burndown, carry over.)*
13. Goal rollups and status analytics. *(Built: goals and projects roll up tasks, health against target, task flow analytics.)*
14. Batch find and replace. *(Built: preview, per-note choice, undo; links, properties and locked notes untouched.)*
15. Density controls. *(Built.)*
16. In-app AI and semantic search. *(Built: local and remote providers.)*

### Release 1.2: Visual and agent release

1. Whiteboard and spatial organization. *(Built: JSON Canvas whiteboards with text, note, link and group cards, arrows, undo.)*
2. Mindmap annotations. *(Built: mind map of any note's outline, annotations stored as footnotes.)*
3. Dependency Gantt views. *(Built: timeline from planned/due/effort, dependency arrows with conflicts, drag and keyboard rescheduling.)*
4. Map and location views. *(Built: offline world map, pins from location properties, clustering, place a note by clicking.)*
5. Adapters for agent frameworks. *(Built: MCP setups for the main frameworks, `quantos-cli tools`/`call` for function calling, a Python helper.)*
6. Global knowledge graph. *(Built: all workspaces, every link kind, tags as hubs, type filters, colour by workspace or type, highlight.)*

### Roadmap items without an assigned release

1. Containerized backend.
2. Server hosting.
3. Moving to the new deployment.
4. Multiuser accounts and sign-in.
5. Broader content importing.
6. Offline caching and sync for remote use.

Don't assign these to a release until there's a separate scope decision.

---

## 15. Rules for future remote hosting (confirmed)

1. The vault on the backend stays authoritative.
2. The browser and the Windows client both support offline editing.
3. Cache the workspaces you select.
4. Download attachments when they're needed.
5. Let attachments be kept for offline use.
6. Queue captures and edits until the connection comes back.
7. Show conflicts and keep both versions.
8. Never silently overwrite conflicting content.
9. If an edit conflicts with a deletion, keep the edit as a recoverable copy.
10. Don't restore deleted tasks automatically.
11. Use the device's current time zone.
12. Keep one authoritative deployment during the move.

**Recommended design:** Keep device caches separate from authoritative vault storage from the start, even though sync comes later.

---

## 16. Build deliverables

1. Source code and dependency list.
2. No-install Windows application package.
3. Shared browser frontend.
4. Local backend launcher.
5. Storage and schema documentation, including schema versioning.
6. Interface contracts for the backend API and the native bridge.
7. Permission and security model.
8. Write and recovery specification.
9. Automated acceptance tests.
10. Example vault and templates.
11. CLI reference.
12. User guide, including manual backup.
13. Troubleshooting and recovery guide.
14. Release checklist.
15. Roadmap and decision log.

---

## 17. Decision log

| Decision | Status |
|---|---|
| Product name: Quantos | Confirmed |
| Local, non-containerized Release 1.0 | Confirmed |
| No-install Windows client; creating local folders and files is OK | Confirmed |
| Single user, no sign-in | Confirmed |
| .NET 10 + WebView2 stack, SQLite cache, TypeScript frontend | Confirmed |
| Frontend built with Vite and Svelte | Confirmed |
| CodeMirror 6 as the editor engine | Confirmed |
| ULID node identifiers in frontmatter `id` | Confirmed |
| Storage and schema document written before Stage 1 code | Confirmed |
| Vault lock file and discovery file for one backend per vault | Confirmed |
| One-time code exchanged for a session cookie for browser access; `Host` header checks | Confirmed |
| Atomic writes, startup reconciliation, and the permission check in Stage 1 | Confirmed |
| Hello-world laptop test has explicit checks and a fallback chosen in advance | Confirmed |
| Cache stored only in `client-data/cache/` | Confirmed |
| Vault defaults to a non-synced folder, with a OneDrive warning | Confirmed |
| Access token, origin checks, Windows client approval for shell and permanent delete | Confirmed |
| Conflicts show a side-by-side comparison and keep both versions | Confirmed |
| Schema version in node frontmatter | Confirmed |
| Global and workspace supertag folders | Confirmed |
| Daily-use checkpoint after Stage 3 | Confirmed |
| Manual backup procedure for Release 1.0 | Confirmed |
| Portable build tested on the KPMG laptop in Stage 1 | Confirmed |
| 300ms sliding debounce | Confirmed |
| Ten tabs per pane | Confirmed |
| Trash never expires automatically | Confirmed |
| Shift-click opens links in the other pane | Confirmed |
| Empty daily entries collapse | Confirmed |
| Task deletion leaves placeholders with Restore | Confirmed |
| Rollover changes the planned date only | Confirmed |
| Recurring tasks are separate tasks, calculated from the scheduled date | Confirmed |
| Closing the window minimizes the client | Confirmed |
| Device's current time zone | Confirmed |
| Containerization, server hosting, multiuser, importing | Roadmap, release not assigned |
| Configurable backups | Release 1.1 |

### Implementation decisions (made during the Release 1.0 build)

These were chosen while building, following the recommendations above. They are implemented but not
separately approved; review them during the daily-use checkpoint.

| Decision | Status |
|---|---|
| Laptop-test fallback: if only the Windows client is blocked, use the browser (`Open in browser.cmd`); if the executables themselves are blocked, request a signed build through IT | Implemented |
| `client-data` sits beside `application\`; falls back to `%LOCALAPPDATA%\Quantos\client-data` when run from elsewhere | Implemented |
| Links stored as `[[id\|Title]]`; typed `[[Title]]` resolves on save when exactly one note matches, and when the note is created later | Implemented |
| Unbound `- [ ] text` lines in notes become task nodes automatically (`inlineTasks: false` in settings.json turns this off); bound by an invisible `<!--task:id-->` marker | Implemented |
| Synced blocks keep a readable copy in each note between `<!--sync:id-->` markers; the canonical text is in `synced_blocks/` | Implemented |
| History keeps every saved version, coalescing autosaves within two minutes; external edits are always kept | Implemented |
| Browser sessions use one-time codes exchanged for an HttpOnly cookie; the Windows client uses the same flow | Implemented |
| Changing agent, plugin or automation permissions needs approval in the Windows client, like shell execution and permanent deletion | Implemented |
| Agents connect through an MCP server (`quantos-cli mcp --agent name`), read-only until granted more | Implemented |
| Plugins run in sandboxed frames with no direct API access; safe mode stops them | Implemented |
| PDF export uses Microsoft Edge in headless mode; falls back to HTML when Edge is missing | Implemented |
| Global shortcut defaults: capture `Ctrl+Alt+Q`, scratchpad `Ctrl+Alt+J` | Implemented |
| Browser tabs share one push channel: one tab holds it and relays changes to the others (browsers allow six connections per host); a tab that stops hearing from it opens its own | Implemented |
| Rolling backups are verified zips with a hash manifest, named per vault (name plus a hash of its location), pruned to the newest N; restore goes to a new folder or, for one note, writes it back as a restore so the replaced text stays in history | Implemented |
| In-app AI is a provider layer: Claude through the official Anthropic SDK (with server-side fallback), everything else through the OpenAI-style API; presets for local (Ollama, LM Studio) and remote providers; keys DPAPI-encrypted in client-data; meaning search uses a separate embeddings index outside the vault | Implemented |
| Density is one CSS factor (`--d`) on row heights and padding; custom styles are vault `themes/*.css`, enabled in settings.json, loaded after the built-in styles and kept local by the page CSP | Implemented |
| Search indexes the text as you read it: link titles instead of ids, without task, sync and block markers, heading hashes, bullets or emphasis marks | Implemented |
| Views with no type filter leave out supertag, template and synced-block records; choose the type to list them | Implemented |
| Links in properties (owner, attendees) count as links: backlinks, graph and renames include them | Implemented |
| A note whose frontmatter breaks stays listed and linked, read-only, until it's fixed in the source view | Implemented |
| Files saved by other editors as UTF-16 or Windows-1252 are read correctly and written back as UTF-8 | Implemented |
| Changes found while running (a copied folder of notes) are indexed in small batches so saving never waits long | Implemented |
| Reminders show in a browser page only when the Windows client isn't running, so each alert shows once | Implemented |
| Exports list the note's properties under the title and carry its images | Implemented |
| First start offers the bundled example vault | Implemented |
| Unsaved text is kept in the browser until saved; drafts older than 30 days are dropped | Implemented |
| The Windows client restarts a backend that stops unexpectedly (at most three times in ten minutes) and signs its windows back in | Implemented |
| The backend re-reads the computer's time zone every 30 seconds, so a zone change applies without a restart | Implemented |
| The calendar file holds events, not to-dos (Outlook and Google ignore to-dos): open dated tasks are free all-day entries, time blocks are timed | Implemented |

### Decisions after Release 1.2 (from use; see CHANGELOG.md and documentation/PROJECT-STATUS.md)

| Decision | Status |
|---|---|
| Product versions are 0.1.x, separate from plan release names; 0.1.1 was the first public release (Releases 1.0 to 1.2) | Implemented |
| Updates: GitHub Releases is the source; checked only on request or daily if the person turns it on; a download is used only if it matches GitHub's SHA-256 digest; only `application\*` is unpacked; the Windows client swaps the program files (old ones kept as `*.old` until the new version starts) and restarts | Implemented |
| Single-file executables are not compressed (about 0.45 s faster start per program; the zip stays the same size) | Implemented |
| Theming is a per-device "look" (background, text, accent, fonts, size) from which the palette is derived with enforced contrast; a look can be saved to the vault as a theme file | Implemented |
| Whiteboards are notes with `type: canvas` holding a JSON Canvas block; note cards keep the note id as well as the path | Implemented |
| Mind-map annotations are Markdown footnotes; focus sessions are lines under `## Focus` in daily notes; habits are daily-note properties | Implemented |
| The map ships its own data (Natural Earth via world-atlas) and fetches nothing; locations are `location: "lat, lng"` and a few other shapes | Implemented |
| Agent frameworks use the MCP server where they support it; others use `quantos-cli tools`/`call` under the agent's grants | Implemented |
| Completing a task completes its open subtasks; reopening it doesn't reopen them | Implemented |
| The scratchpad is one note, shown as a pop-up window or docked in the right sidebar; send-to keys move text out of it | Implemented |
---

## Final build direction

**One local product with two interfaces, one authoritative vault, and one write service that coordinates every change.** Build the foundation, security, and recovery first. Test the portable build on your laptop in Stage 1. Then build the full Release 1.0 feature set in the stage order above. Containerization and remote hosting stay as planned extension points, not as requirements for Release 1.0.
