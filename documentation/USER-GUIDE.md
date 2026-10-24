# Quantos user guide

Quantos is a personal productivity app for notes, tasks, projects and people. Everything you write
is a Markdown file with YAML frontmatter, kept in a folder you choose (your **vault**). You can open
and edit those files with any editor, and copy the folder to back it up.

Release 1.0 runs entirely on your computer. Nothing is sent anywhere.

---

## 1. Starting Quantos

Unzip `Quantos-<version>-win-x64.zip` anywhere you can write, for example `Documents\Quantos`.
Nothing needs installing and you don't need admin rights.

```
Quantos\
  application\          the programs (Quantos.exe, Quantos.Backend.exe, quantos-cli.exe)
  client-data\          created on first start: settings, cache, logs (safe to delete the cache)
  Start Quantos.cmd     opens the Windows client
  Open in browser.cmd   starts Quantos and opens it in your browser instead
  Self-check.cmd        checks this computer can run Quantos
  Example Vault\        a small vault to explore
  Feature Demo Vault\   feature-rich dummy records for testing
```

- **Windows client:** double-click `Start Quantos.cmd` (or `application\Quantos.exe`).
- **Browser:** double-click `Open in browser.cmd`. The browser and the Windows client can be open at
  the same time; both edit the same vault through the same backend.
- **First start:** choose where your vault lives. The default is `C:\Users\<you>\Quantos\Vault`.
  To look around first, choose **Explore the example vault first**; open your own vault later from
  *Settings › Vault*.
  If you pick a folder inside OneDrive, Dropbox or another sync service, Quantos warns you: sync
  tools can lock files while Quantos saves, create conflict copies, and occasionally corrupt files.
  A folder that isn't synced is safer.

Closing the window **minimizes** Quantos; it keeps running for reminders and quick capture. To quit,
use **Exit** from the tray icon menu. Exit saves your open notes first and warns you if anything
couldn't be saved.

The top bar has a taller header with a larger Quantos wordmark and an 8px gap before the main
content. The dark appearance uses a slightly lighter neutral gray background. Its quick actions can
be changed in *Settings › Toolbar*. Daily stream, My Day and Tasks remain available through their navigation shortcuts and app navigation,
but are no longer offered as top-bar actions.
The *New* action sits beside the workspace on the left. *Split* and *Quick Capture* sit beside Search on
the right. In *Settings › Toolbar*, one *Show button labels* option controls labels on all four buttons;
when it is off, each button shows only its icon.

The left navigation sits in one softly raised panel. Its rows are unboxed; the current view and
hovered row use a light fill to show their state.

Split views appear as two separate cards with a clear gutter between them. The right sidebar aligns
to the same gutter as the center view. Drag the center handle to resize the panes. The focused pane
has a quiet neutral emphasis on its outline; each pane has its own close button. The right sidebar
keeps its panels on the same neutral card color as the rest of the app, with an 8px inset at the
window edge. Its Tasks panel uses one heading, a compact add field, and a simple list rather than
nested task cards. Sidebar cards have an 8px gap. Drag the small grip between two cards to resize
them; it appears on hover or keyboard focus. The left and right sidebar toggle icons in the top bar
stay unboxed.

Use **Add a panel** at the bottom of the right sidebar to add **My Day** alongside Files, Tasks,
Scratchpad and the other available panels. Each panel is its own card; drag the subtle grip between
cards to give them more or less room. The right sidebar can hold three or more panels at once.
Quantos automatically saves the panels, their order, and their relative heights for the next launch.
For a safe walkthrough, open `Feature Demo Vault` (beside the application folder in the test copy)
as a separate vault. The source is `examples/Feature Demo Vault`. It has two sample
items for each built-in note type and the shipped goal, sprint and canvas types; the records include
representative properties, links, schedules, trackers, reminders, focus details and canvas edges.

### If something blocks Quantos

Run `Self-check.cmd`. It checks the things a managed work laptop can block: whether the programs
are allowed to run, whether Quantos can listen on `127.0.0.1`, whether the WebView2 runtime is
installed, and which PowerShell language mode is enforced. The report is saved in `client-data\logs`.

If the Windows client can't run but the backend can, use **Open in browser**. Everything except the
Windows-only features (below) works the same.

---

## 2. Writing

- **New note:** `Ctrl+N`. Type a title, press Enter, write.
- **Slash commands:** type `/` on a line and a list of things to put in the note appears: a task,
  a heading, a list, a quote, a callout, a code block, a table, a divider, today's date. Type a
  word to narrow the list, press Enter to use it.
- **Right-click a block** for what can be done with it: copy it, duplicate it, move it up or down,
  delete it, or turn it into a task, a heading, a bullet or a quote. For a task there are more:
  mark it done, set it to doing or waiting, give it a due date or a priority. `Shift+F10` opens the
  same menu from the keyboard.
- **Collapsible headings:** every heading has a small arrow in front of it. Click the arrow and the
  section under that heading folds away, down to the next heading of the same level; a deeper
  heading goes with it. Click again to bring it back. The arrow in the left margin does the same.
  Turn the arrows off in *Settings › Writing › Collapsible headings*.
  - **A heading that starts folded:** type `/` and choose *Collapsible heading 1*, *2* or *3*. It
    writes `## [-] Title`: the section under it is folded, and stays folded when you come back to
    the note, because the mark is saved in the file.
  - **Fold the heading you are in:** type `/` and choose *Fold the heading here*. The section under
    the nearest heading above the cursor folds. Nothing is written to the note, so it opens again
    next time.
- **Several blocks at once:** `Ctrl`-click a block to select it, and `Ctrl`-click others to add
  them. `Ctrl`-`Shift`-click stretches the selection over everything between. The selected blocks
  are shaded, and you can
  - **drag** them to move them: the line under the pointer shows where they will land;
  - **right-click** them for the same menu, now working on all of them at once: copy, duplicate,
    move up or down, delete, or turn them into tasks, headings, bullets or quotes.
- **Links:** type `[[` and pick a note. Links keep working when notes are renamed or moved, and a
  rename updates the link text everywhere, including links in properties like *Owner*. If two
  notes share a title, Quantos asks which one you mean; it never guesses.
- **Web links:** `[text](https://…)` reads as its text away from the cursor; click it to open it in
  your browser. Paste a web address over selected words to link them. Links to stored files read
  as their names the same way.
- **Shift-click** a link to open it in the other pane. `Ctrl`-click opens a new tab.
- **Convert to task:** `Ctrl+Alt+T` turns the line you're on (or every selected line) into a task,
  whether it's plain text, a list item, a heading or a quote.
- **Tasks:** type `- [ ] Something to do` in any note. It becomes a real task, kept in sync both
  ways: tick it in the note or in the task list and both change. Enter on an empty item ends the list.
  Quick-add words work on these lines too: `- [ ] Call Sam due:fri !1` keeps *Call Sam* in the note
  and puts the date and priority on the task.
- **Delete a task:** hover the task in any task list and press the **×** at the end of the row — it
  goes to the trash and the toast offers an undo. Right-clicking the task gives the same thing
  plus marking it done, setting it to doing or waiting, a due date and a priority. In a note,
  right-click the task line to move the task itself to the trash, or *Delete this block* to take
  just the line out of the note. A task in the trash can be brought back from *Trash* in the
  sidebar. Trashing a task takes its subtasks with it, and restoring it brings them back.
- **Embeds:** `![[Note]]` shows another note (or `![[Note#Heading]]`, `![[Note#^block]]`) inline.
- **Synced blocks:** select text, open the command palette (`Ctrl+Shift+P`) and choose
  *Make selection a synced block*. Insert it elsewhere with *Insert synced block*. Editing any copy
  updates all of them.
- **Columns:** `:::columns`, then text, `+++`, more text, `:::`.
- **Callouts:** `> [!note]- Title` makes a collapsible callout (`-` starts it folded).
- **Footnotes:** `[^1]` with `[^1]: text` at the end; hover to preview.
- **Queries:** a fenced block marked `query` shows live results you can edit in place:

  ````
  ```query
  tags: [meeting]
  where:
    - status = Planned
    - when >= today
  sort: when
  columns: [title, when, status]
  ```
  ````

- **Math:** `$E = mc^2$` in a line, or `$$` on lines of their own around a block of TeX. A dollar
  followed by a number stays a dollar, so prices are safe. Click rendered math to edit it.
- **Diagrams:** a fenced block marked `mermaid` draws a flowchart, sequence, Gantt or other
  [Mermaid](https://mermaid.js.org) diagram in the app's colours. A mistake shows what went wrong
  under the source. Both work offline. Exports keep the TeX and Mermaid source as text.
- **Read-only:** *Lock* in a note's `⋮` menu stops anyone editing it, including agents, plugins and
  automations, until you choose *Unlock*. Links in it still follow renames. *Reading mode*
  (`Ctrl+Alt+R`, or the command palette) makes every note read-only on this device until you turn
  it off from the status bar.
- **Find and replace across notes:** `Ctrl+Shift+H` (or the button beside Search). Every change shows
  before anything happens; untick notes to leave them out. Match case, whole words and patterns
  (regular expressions, with `$1` for a captured group) are options. Properties, links and task
  markers are never changed, and locked notes are skipped. *Undo* in the message afterwards puts the
  notes back (any you've edited since keep your newer text), and each note's history has its old text.
- **Whiteboards:** *New whiteboard* in the command palette. Double-click the board for a text card
  (Markdown, math and diagrams work), or add a **Note** (shows the note and opens it on double-click),
  a **Link** or a **Group** (cards on a group move with it). Drag a card to move it, its corner to
  resize, and one of its edge dots onto another card to draw an arrow (or onto empty space for a new,
  connected card); double-click an arrow to label it. Shift-drag selects several; colour, `Del`,
  `Ctrl+D` duplicate, arrow keys nudge, `Ctrl+Z` undo. Drag the board to pan, `Ctrl`+scroll to zoom,
  *Fit* to see everything. A whiteboard is a note (`type: canvas`) holding a JSON Canvas block, the
  open format other tools read, so it has history and search like any note.
- **Mind maps:** *Mind map* in a note's `⋮` menu draws the note's headings and list items as
  branches. Collapse branches with their − button. To annotate a branch, select it and press `A` (or
  its 💬 button); click an annotation to change or remove it. Annotations are footnotes on that line
  (`[^m1]` with `[^m1]: your note` at the end), so they're plain Markdown in the note too.
- **Presenting:** *Present* in a note's `⋮` menu (or `Ctrl+Alt+P`) shows it full screen as slides. A
  `---` line with a blank line above it starts a new slide; a note without any is split at its `#` and
  `##` headings. Lines after a `Notes:` line are speaker notes: press `N` to see them. Arrows, Space
  or a click move on, `F` toggles full screen, `Esc` ends. Math, diagrams and images show as in the note.
- **Saving** is automatic. If a save fails (the backend restarting, say), Quantos keeps trying, and keeps
  a copy of the unsaved text in the browser until it's saved, restoring it the next time the note opens.
  If the backend stops unexpectedly, the Windows client starts it again within a few seconds and signs
  its windows back in; your unsaved text comes back with them.
- **Source view:** the `</>` button edits the raw Markdown, frontmatter included.
- **Writing tools:** table of contents and outline zoom (list button), typewriter scrolling,
  word count and reading time, writing targets (note menu; saved with the note as `writing_target`), multiple cursors (`Alt`-drag for a
  column selection), folding headings and lists, copy as Markdown or rich text.
- **Audio:** *Record audio here* in a note's `⋮` menu (or `Ctrl+Alt+A`, again to stop). A bar shows
  the time and the input level; *Stop and save* stores the recording in the vault (Opus audio, about
  half a megabyte a minute) and puts its link under the line you were on, or in today's daily note if
  no note is open. The first time, allow Quantos to use the microphone. Audio files (recordings, MP3,
  M4A, WAV, Ogg) play in the note, with a speed button for listening back faster.
- **Images and files:** paste or drag them into a note. Identical files are stored once.
  PDFs open in a viewer with *Cite this page*.

### Daily notes

The **Daily stream** shows today's note at the top, with earlier days below. Days with nothing
written collapse. A dot and a short summary ("120 words · 2 tasks") mark days that have something
in them; tick *Hide empty days* to leave out the rest. Quick capture adds to today's note without disturbing what you're typing.

### Browsing notes

*Notes* at the top of the sidebar (`Ctrl+Shift+B`) lists every note with its first lines, newest
edits first. Filter by words, type, tag or folder, sort by edited, created or title, and include
archived notes or other workspaces. Click a note to preview it beside the list; double-click or press
Enter to open it. Tick notes (or press Space) to tag, archive or trash several at once. *Tree* shows the same notes
nested in their folders (empty folders included); drag a note onto a folder to move it there.

### The sidebar

Drag sidebar items to reorder them, and drag a section by its heading (Saved queries, Supertags…)
to move the whole section. The sliders button next to *Settings* also customises the left sidebar: untick items to hide them, use
the arrows to reorder them, and hide whole sections (Pinned views, Folders, Saved queries, Supertags,
Vault). *Reset* brings back the standard layout. The choice is kept on this device. Everything hidden
is still in the command palette (`Ctrl+Shift+P`).

### Tabs and panes

Each pane holds up to ten tabs. The active view has a small close button at the upper-right corner;
tab names do not take up space above the view. Pin tabs you want to keep; when a pane is full, the
least recently used unpinned tab closes (after saving). Split the window with `Ctrl+\`; a visible
divider separates the panes and can be dragged or keyboard-resized. To switch tabs without a visible
tab strip, open the command palette with `Ctrl+Shift+P` and run *Next tab* or *Previous tab*. Each
workspace remembers its own tabs, splits and scroll positions.

---

## 3. Tasks and planning

- **Quick add:** `Call Sam due:fri !2 ~30m #client every week`
  - `!1` to `!4` priority (coloured strip), `due:` deadline, `@tomorrow` or `plan:` planned day,
    `~45m`/`~2h` effort, `#tag`, `+project` (links to the note with that title), `every day|weekday|week|month`,
    `!myday`. The same words work in task lines in notes and in quick capture to a task.
- **Gantt:** *Gantt* in the sidebar shows tasks on a timeline, grouped by project. A bar runs from a
  task's planned day (or time block) to its due day; with only one of them, the effort estimate sets
  the length (striped bars are estimates). Arrows show `depends_on`; a red one means a task starts
  before what it depends on is due to finish. Drag a bar to move both dates, drag its right edge to
  change the due date, or focus it and use ← → (Shift to resize). Zoom by day, week or month; tasks
  without dates are listed below with *Plan for today*.
- **Sprints:** *Sprints* in the sidebar. A sprint is a note (`type: sprint`) with a start, an end,
  an optional goal and a capacity in hours. Add tasks from the backlog (open tasks in no sprint);
  each gets a `sprint` property linking the sprint. The sprint shows tasks by status, estimated work
  against capacity, and a burndown (by estimate when every task has one, otherwise by count). *Move
  unfinished* sends what's left to another sprint or back to the backlog.
- **Goals and analytics:** *Goals* in the sidebar. A goal is a note (`type: goal`) with an optional
  `target` date; sub-goals link it with `parent`, projects with `goal`, and tasks their project with
  `project`. Progress rolls up through all of them, and health (on track, at risk, off track) compares
  progress with the time passed to the target. Notes typed or tagged *project*, or named as a task's
  project, count as projects. Above the list: open, overdue, blocked and waiting tasks, what's due
  this week, the time from created to done, and tasks created and completed per week.
- **Focus timer:** *Focus* in the sidebar (`Ctrl+Alt+F`), or *Focus on this task* in a task's `⋮`
  menu. Pick a task (or just say what it's for) and a length; the time left shows in the status bar
  and the window title. When it ends you hear a short tone and can take a break. Each session is
  written to that day's daily note under **## Focus** and added to the task's `time_spent` (minutes).
  Stopping early still logs the minutes done. The Focus view shows today's sessions and totals per
  day and per task for the last week or month, read from those daily notes, so editing a line there
  changes the totals too.
- **Habits and metrics:** *Habits* in the sidebar. *Track something* adds a habit (done or not) or
  a number with a unit and an optional daily goal (or a limit to stay under). Click a day to tick it
  or type the number. Each value is a property on that day's daily note (`exercise: true`,
  `sleep: 7.5`), so it's in your files and works in queries. Rows show the current streak and the
  share of days done, or the average for numbers. Click a name to change or stop tracking it; the
  values already written stay in the notes. The list of trackers is kept in `settings.json`.
- **My Day:** pick what you'll do today. Tasks planned for earlier days can be moved to today with
  one click. This changes the **planned** date only; due dates never move by themselves.
- **Subtasks:** marking a task done also marks its open subtasks done (and theirs). Reopening it
  leaves them as they are.
- **Recurring tasks:** each occurrence is a separate task with its own history. The next one is
  calculated from the scheduled date, not from when you finished. A monthly task on the 31st falls on
  the last day of shorter months and goes back to the 31st after.
- **Subtasks, dependencies, effort:** in a task's panel. Effort rolls up from subtasks; a task
  waiting on unfinished dependencies shows *blocked*.
- **Reminders:** add a date and time in the task panel. The Windows client shows a notification
  (once, however many windows are open). Without it, the browser shows the reminder in the page, or
  as a system notification if you allow them under Notifications. Every reminder is kept under
  Notifications. Snooze from the task panel.
- **Timeline:** drag tasks onto the day to block time.
- **Stale tasks:** a review of open tasks untouched for a while.
- **Calendar file:** the calendar button in Tasks saves a `.ics` file to the vault's `exports` folder.
  Open tasks with a date appear as all-day entries marked free (or half an hour at their due time);
  time blocks appear as meetings. Import it into Outlook or any calendar app.
- **Deleting a task** moves it to the trash. Notes that referred to it show a placeholder with
  **Restore**; restoring reconnects the same task everywhere.

---

## 4. Structure

- **Supertags** turn notes into typed records: `#meeting` with *When*, *Status*, *Attendees*.
  Supertags can inherit fields from a parent (`#standup` inherits from `#meeting`).
  Manage them under *Supertags* in the sidebar. Use **New #tag** for a form that checks required
  fields; editing properties directly warns but never blocks.
- **Changing a field's type never rewrites your notes.** When you're ready, use **Migrate…** to
  convert existing values; anything that can't be converted is kept as it was.
- **Templates** live in the vault's `templates` folder. A supertag can apply its template
  automatically when the tag is added.
- **Views:** *New view* under Saved queries builds tables, lists, boards, grids and galleries with
  multi-level sorting and grouping. Tables edit like a spreadsheet (arrow keys, Enter, Tab, Escape).
  On a board, drag a card to another column, or focus it and press Alt+Left / Alt+Right.
  Paste rows from Excel into *Paste rows* to create notes in bulk.
- **Search** (`Ctrl+Shift+F`) matches the start of words, across every workspace or just one.
  `"exact phrase"` matches words together, `budget OR forecast` either word, `launch -draft`
  leaves out notes with *draft*. A small typo still finds close matches when nothing else does.
- **Graph:** *Graph* in the sidebar. *Most connected* shows a workspace's best-linked notes, or the
  notes within one or two links of a note you choose. *Everything* is the whole knowledge graph, in
  this workspace or all of them: links from text, properties, embeds and synced blocks, bigger dots for
  more links, coloured by workspace or type, filtered by type, and with tags as hubs if you like.
  *Highlight* picks out matching titles. Scroll to zoom, drag to pan, hover to see a note's links.
- **Map:** *Map* in the sidebar shows notes with a location as pins on a world map. *Place a note*,
  then click the map, to give a note a `location` (`51.50720, -0.12760`); you can also type one, or
  use `coordinates: [lat, lng]`, a `geo:` map or `lat:`/`lng:` properties. An optional `place`
  property names it. Nearby pins group into a numbered bubble; click it to zoom in. The map ships
  with Quantos (country outlines from Natural Earth) and nothing is fetched from the internet, so it
  shows countries and coastlines, not streets.
- **Metadata dictionary** lists every property, how it's used and which supertags define it.

---

## 5. Files, history and recovery

- **History:** every saved version is kept (`.history` in the vault). Open it from the clock button
  on any note and pick a version. *Since then* shows what changed from it to now; *Made in this
  version* shows what that save changed. Changed words are marked inside each line, property changes
  are listed in their own table, unchanged stretches fold away, and the arrows jump between changes.
  *Side by side* puts the two versions next to each other. *Restore this version* brings it back,
  keeping the current text in history.
- **Trash:** never empties itself. Restore puts a note back where it was, or under a new name if the
  old one is taken. Permanent deletion always asks for confirmation in the Windows client.
- **Editing outside Quantos:** fine. Quantos notices within a moment. If you also had unsaved changes
  to the same lines, it shows both versions side by side; you choose which to keep and the other is
  saved in history. Nothing is thrown away silently.
- **Vault diagnostics** shows what the last start-up check found, and can check integrity or
  rebuild the cache.
- **Export:** Markdown, HTML or PDF from a note's menu, or a zip of a whole workspace from the file
  tree. Queries are frozen into tables, embeds are included and properties are listed under the title,
  so exports read the same anywhere. A workspace zip carries the images and files its notes use.
  Exports go to the vault's `exports` folder.
- **Publish:** *Publish this folder…* in the workspace menu makes a small website from the notes of
  the workspace, or of a linked folder: a page per note with the notes that link to it and the ones
  it links to, an index you can search, and a graph of how they connect. The site is written to the
  vault's `exports` folder and can be put on any web host; the notes themselves are not changed.

### Linked folders

Do you keep notes in another app? Link that folder into Quantos and both can work on the same
Markdown: Quantos reads and writes the files **where they are**, so nothing is moved, copied or
deleted, and your other app still sees every change.

- **Link a folder:** *Link a folder…* in the workspace menu (top left, next to the workspace name).
  Give it the folder's path; the name comes from the folder itself.
- **It appears as its own group** under *Linked folders* in the same menu, next to the workspaces.
  Pick it and Quantos shows the notes in that folder, and you can edit them as usual.
- **Your files stay yours.** Quantos adds only an `id` to a note so it can recognise it later; the
  rest of the frontmatter and the text are untouched. The id comes from the file's path, so a note
  keeps its identity even if the other app rewrites the file.
- **Both apps can write.** If you change a note in one app while Quantos has it open, the newest
  change wins and the other version is kept in the note's history, so you can get it back.
- **Unlink** with *Unlink “name”* in the same menu. Quantos stops reading the folder; the files
  are left exactly as they are.
- A linked folder can't be moved to another workspace or archived, and Quantos keeps its own
  layout settings in the vault rather than in your folder.

### Backups

Quantos backs up the vault automatically while it's running, by default once a day to
`client-data\backups`, keeping the newest 14. Change this in *Settings › Backups*:

- **Folder:** pick another drive, a OneDrive folder or a network share, so a backup survives the
  laptop. It must be outside the vault.
- **How often** (hourly to weekly), **how many to keep**, and whether to include version history
  (left out by default because it can be large).
- **Back up now** makes one straight away. **Check** re-reads a backup and compares every file with
  the fingerprint recorded when it was made.
- **Restore a note…** lists the notes in a backup. Restoring puts that note back as it was; its
  current text stays in the note's history. A note deleted since comes back where it was.
- **Restore all…** unpacks the whole backup into a new folder (never over your vault) and offers to
  open it. Switch back from *Settings › Vault*.

Each backup is one zip file. You can also open it with Windows Explorer and copy files out.
You don't need to back up `client-data\cache\`; it's rebuilt automatically.

### Scratchpad

The scratchpad is one note for quick jotting. Open it as an always-on-top window with `Ctrl+Alt+J`
(Windows client), or dock it in the right sidebar (its pencil tab); it's the same note in both.
Use **Add a panel** at the bottom of the right sidebar to stack panels (say, the scratchpad above the
file tree); drag the grip between cards to resize. Keys
move text out of it: the selection, or the paragraph the cursor is in, goes to a place and leaves the
scratchpad. By default `Ctrl+Enter` sends to today's daily note, `Ctrl+Shift+Enter` makes a new note
and `Ctrl+Alt+Enter` a new task; *Send to…* at the bottom lists them. Add your own in *Settings ›
Shortcuts › Scratchpad: send to*, for example the end of a project note under an *Inbox* heading,
each with its own keys. Editing commands such as *Convert to task* work in the scratchpad window too.

---

## 6. Windows client features

These need the Windows client; the browser labels them *Requires Windows client*.

| Feature | Default shortcut |
|---|---|
| Quick capture from anywhere | `Ctrl+Alt+Q` |
| Always-on-top scratchpad (also dockable in the right sidebar) | `Ctrl+Alt+J` |
| Native notifications for reminders | — |
| PowerShell terminal (left sidebar › Terminal) | — |

Change the global shortcuts in *Settings › Shortcuts*; if another program already uses one, Quantos
tells you. Each terminal session asks for confirmation before it starts, and PowerShell code blocks
in notes have a **Run** button that shows exactly what will run, and where, before running it. Code
in notes never runs by itself.

---

### Help and About

*Help* (`F1`, or the ? button at the bottom of the sidebar) has this guide, troubleshooting, the
command line and agent guides, and the keyboard shortcuts as they're currently set, all searchable
and available offline. *About Quantos* (the i button) shows the version, where your vault and app
data are, and the licences of the open-source projects Quantos includes.

### Updating

*Settings › Updates* shows your version. *Check for updates* asks GitHub for the latest release and
shows what's new; *Download* fetches it and checks it against the checksum GitHub publishes. In the
Windows client, *Install and restart* saves your notes, swaps in the new program files and restarts;
your vault and `client-data` aren't touched, and if anything goes wrong the old files are put back.
The status bar says when an update is waiting. Quantos only contacts GitHub when you check, or once a
day if you tick *Check once a day*. Updating by hand still works: close Quantos and copy the new
`application` folder over the old one.

## 7. Command line and agents

`application\quantos-cli.exe` works with the running app: `quantos-cli search budget`,
`quantos-cli tasks`, `quantos-cli capture "Call the bank"`, `quantos-cli task add "Pay rent" --due 2026-11-01`.
Add `--json` for scripts. See `CLI-REFERENCE.md`.

AI agents can connect with `quantos-cli mcp --agent <name>` (Model Context Protocol). A new agent can
only **read** until you grant more in *Settings › Plugins & permissions*. Granting permissions is
confirmed in the Windows client. Agents can never run commands or delete permanently without your
approval in the Windows client.

- **Set an agent up** in *Settings › MCP*: give the agent a name, check the program it will
  run, and copy the settings for Claude Desktop, Claude Code, Cursor, Codex or OpenCode into that
  agent's configuration file. The page also lists the nine tools an agent gets. Quantos has to be
  running, because the agent talks to the app and not to the files.

---

## 8. Plugins and automations

- **Plugins** go in the vault's `plugins` folder (`plugin.json` + `main.js`). They run in a sandbox
  and get only the permissions you grant. **Safe mode** (Settings) turns all plugins off. To write one,
  see `PLUGINS.md`.
- **Automations** react to property changes, e.g. when a `#project` note's status becomes *Done*,
  stamp the date and create a follow-up task. Build them with the form in *Settings › Plugins &
  permissions* (or edit the JSON there). *Recent runs* shows what each one did.
- **AI** (*Settings › AI*, off until you switch it on): choose a provider for writing and answering
  and one for meaning search. Presets cover Ollama and LM Studio (on this computer: nothing leaves
  it), Anthropic Claude, OpenAI, Google Gemini, Mistral, Voyage AI, OpenRouter, Azure OpenAI, and any
  OpenAI-compatible server. *List models* asks the provider what it offers. Keys are encrypted for
  your Windows account and never stored in the vault.
  - **Ask your notes** (sidebar, `Ctrl+Shift+K`): answers from the notes that match, with links to them.
  - **Meaning search**: in Search, switch *Words* to *Meaning* to find notes by what they mean.
  - **On selected text** (command palette, “AI:”): rewrite, improve, fix spelling, make shorter,
    summarise, list action items, continue writing. The result opens in a window; the note changes
    only when you choose *Replace* or *Insert below*.
- **Theme** (*Settings › Appearance*): start from a theme (Quantos Dark and Light, Midnight, Graphite,
  Nord, Forest, Rosé, Solarized, Paper, Sky, High contrast), then change anything:
  - **Quantos Dark:** a lighter neutral gray page background with darker gray cards; the Windows app's
    title bar uses the same background colour.
  - **Window size:** drag any edge or corner of the Windows app window to resize it.
  - Scrollbars are hidden to keep the workspace clear; wheel, touchpad, keyboard and touch scrolling
    continue to work.
  - **Background:** any colour. Panels, lines and softer text are worked out from it, light or dark,
    and kept readable.
  - **Text:** any colour, nudged if it would be hard to read.
  - **Accent:** a preset swatch or any colour, from the picker or as hex. Quantos adjusts it slightly
    if text in that colour would be hard to read, and says so.
  - **Fonts:** for the interface, headings, the editor and code. Pick from the list or *Another font…*
    to type any font installed on the PC.
  - **Interface size:** scales everything.

  *Save as a vault style…* writes the theme to the vault's `themes` folder so you can apply it
  from any device; otherwise the theme, density, editor text size, line spacing and width are per
  device.
- **Custom styles:** `.css` files in the vault's `themes` folder, applied in *Settings ›
  Appearance*. One style is applied at a time: press the button for the style you want, or *None*
  to go back to Quantos's own look. Colours and sizes are CSS variables (`--accent`, `--bg`,
  `--text`…), so a few lines go a long way; the example vault includes `warm-paper.css`. Styles
  can't load anything from the internet, and safe mode turns them off.

---

## 9. Keyboard

| Action | Keys |
|---|---|
| Help | `F1` |
| Quick open | `Ctrl+P` or `Ctrl+K` |
| Command palette | `Ctrl+Shift+P` |
| New note | `Ctrl+N` |
| Daily stream / My Day / Tasks | `Ctrl+Shift+D` / `Ctrl+Shift+M` / `Ctrl+Shift+T` |
| Search | `Ctrl+Shift+F` |
| Ask your notes (AI) | `Ctrl+Shift+K` |
| Reading mode on / off | `Ctrl+Alt+R` |
| Find and replace across notes | `Ctrl+Shift+H` |
| Present the open note as slides | `Ctrl+Alt+P` |
| Focus timer | `Ctrl+Alt+F` |
| Record audio / stop | `Ctrl+Alt+A` |
| Convert line or selection to tasks | `Ctrl+Alt+T` |
| Browse notes | `Ctrl+Shift+B` |
| Back / forward | `Alt+←` / `Alt+→` |
| Split right / close tab | `Ctrl+\` / `Ctrl+W` |
| Toggle sidebars | `Ctrl+[` / `Ctrl+]` |
| Extract selection to new note | `Ctrl+Alt+E` |

All in-app shortcuts can be changed in *Settings › Shortcuts* (saved in the vault's `shortcuts.json`).
