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
- **Shift-click** a link to open it in the other pane. `Ctrl`-click opens a new tab.
- **Tasks:** type `- [ ] Something to do` in any note. It becomes a real task, kept in sync both
  ways: tick it in the note or in the task list and both change. Enter on an empty item ends the list.
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

- **Saving** is automatic. If a save fails (the backend restarting, say), Quantos keeps trying, and keeps
  a copy of the unsaved text in the browser until it's saved, restoring it the next time the note opens.
  If the backend stops unexpectedly, the Windows client starts it again within a few seconds and signs
  its windows back in; your unsaved text comes back with them.
- **Source view:** the `</>` button edits the raw Markdown, frontmatter included.
- **Writing tools:** table of contents and outline zoom (list button), typewriter scrolling,
  word count and reading time, writing targets (note menu), multiple cursors (`Alt`-drag for a
  column selection), folding headings and lists, copy as Markdown or rich text.
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
    `~45m`/`~2h` effort, `#tag`, `+project`, `every day|weekday|week|month`.
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
- **Metadata dictionary** lists every property, how it's used and which supertags define it.

---

## 5. Files, history and recovery

- **History:** every saved version is kept (`.history` in the vault). Open it from the clock button
  on any note, compare with the current version, and restore.
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

### Manual backup (Release 1.0)

Configurable automatic backups arrive in Release 1.1. Until then:

1. Use **Exit** in the Windows client so the backend shuts down safely.
2. Copy the whole **Vault** folder to your backup location.
3. You don't need to back up `client-data\cache\`; it's rebuilt automatically.
4. **To restore:** copy the Vault folder back, start Quantos, and if anything looks out of date
   choose *Vault diagnostics › Rebuild cache*.

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
  stamp the date and create a follow-up task. Edit them in *Settings › Plugins & permissions*.

---

## 9. Keyboard

| Action | Keys |
|---|---|
| Quick open | `Ctrl+P` or `Ctrl+K` |
| Command palette | `Ctrl+Shift+P` |
| New note | `Ctrl+N` |
| Daily stream / My Day / Tasks | `Ctrl+Shift+D` / `Ctrl+Shift+M` / `Ctrl+Shift+T` |
| Search | `Ctrl+Shift+F` |
| Back / forward | `Alt+←` / `Alt+→` |
| Split right / close tab | `Ctrl+\` / `Ctrl+W` |
| Toggle sidebars | `Ctrl+[` / `Ctrl+]` |
| Extract selection to new note | `Ctrl+Alt+E` |

All in-app shortcuts can be changed in *Settings › Shortcuts* (saved in the vault's `shortcuts.json`).
