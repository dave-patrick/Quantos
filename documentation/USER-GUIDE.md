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

### If something blocks Quantos

Run `Self-check.cmd`. It checks the things a managed work laptop can block: whether the programs
are allowed to run, whether Quantos can listen on `127.0.0.1`, whether the WebView2 runtime is
installed, and which PowerShell language mode is enforced. The report is saved in `client-data\logs`.

If the Windows client can't run but the backend can, use **Open in browser**. Everything except the
Windows-only features (below) works the same.

---

## 2. Writing

- **New note:** `Ctrl+N`. Type a title, press Enter, write.
- **Links:** type `[[` and pick a note. Links keep working when notes are renamed or moved, and a
  rename updates the link text everywhere, including links in properties like *Owner*. If two
  notes share a title, Quantos asks which one you mean; it never guesses.
- **Web links:** `[text](https://…)` reads as its text away from the cursor; click it to open it in
  your browser. Paste a web address over selected words to link them. Links to stored files read
  as their names the same way.
- **Shift-click** a link to open it in the other pane. `Ctrl`-click opens a new tab.
- **Tasks:** type `- [ ] Something to do` in any note. It becomes a real task, kept in sync both
  ways: tick it in the note or in the task list and both change. Enter on an empty item ends the list.
  Quick-add words work on these lines too: `- [ ] Call Sam due:fri !1` keeps *Call Sam* in the note
  and puts the date and priority on the task.
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
written collapse. Quick capture adds to today's note without disturbing what you're typing.

### Tabs and panes

Each pane holds up to ten tabs. Pin tabs you want to keep; when a pane is full, the least recently
used unpinned tab closes (after saving). Split the window with `Ctrl+\`. Each workspace remembers its
own tabs, splits and scroll positions.

---

## 3. Tasks and planning

- **Quick add:** `Call Sam due:fri !2 ~30m #client every week`
  - `!1` to `!4` priority (coloured strip), `due:` deadline, `@tomorrow` or `plan:` planned day,
    `~45m`/`~2h` effort, `#tag`, `+project` (links to the note with that title), `every day|weekday|week|month`,
    `!myday`. The same words work in task lines in notes and in quick capture to a task.
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

---

## 6. Windows client features

These need the Windows client; the browser labels them *Requires Windows client*.

| Feature | Default shortcut |
|---|---|
| Quick capture from anywhere | `Ctrl+Alt+Q` |
| Always-on-top scratchpad | `Ctrl+Alt+J` |
| Native notifications for reminders | — |
| PowerShell terminal (left sidebar › Terminal) | — |

Change the global shortcuts in *Settings › Shortcuts*; if another program already uses one, Quantos
tells you. Each terminal session asks for confirmation before it starts, and PowerShell code blocks
in notes have a **Run** button that shows exactly what will run, and where, before running it. Code
in notes never runs by itself.

---

## 7. Command line and agents

`application\quantos-cli.exe` works with the running app: `quantos-cli search budget`,
`quantos-cli tasks`, `quantos-cli capture "Call the bank"`, `quantos-cli task add "Pay rent" --due 2026-11-01`.
Add `--json` for scripts. See `CLI-REFERENCE.md`.

AI agents can connect with `quantos-cli mcp --agent <name>` (Model Context Protocol). A new agent can
only **read** until you grant more in *Settings › Plugins & permissions*. Granting permissions is
confirmed in the Windows client. Agents can never run commands or delete permanently without your
approval in the Windows client.

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
- **Look** (*Settings › Appearance*): dark or light, accent colour, density (compact, comfortable,
  spacious), typeface and line spacing. These are per device.
- **Custom styles:** `.css` files in the vault's `themes` folder, switched on in *Settings ›
  Appearance*. Colours and sizes are CSS variables (`--accent`, `--bg`, `--text`…), so a few lines
  go a long way; the example vault includes `warm-paper.css`. Styles can't load anything from the
  internet, and safe mode turns them off.

---

## 9. Keyboard

| Action | Keys |
|---|---|
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
| Back / forward | `Alt+←` / `Alt+→` |
| Split right / close tab | `Ctrl+\` / `Ctrl+W` |
| Toggle sidebars | `Ctrl+[` / `Ctrl+]` |
| Extract selection to new note | `Ctrl+Alt+E` |

All in-app shortcuts can be changed in *Settings › Shortcuts* (saved in the vault's `shortcuts.json`).
