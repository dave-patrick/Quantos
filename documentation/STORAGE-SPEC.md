# Quantos storage and schema specification

Schema version: **1**. This document is the contract between the vault on disk and every
part of the app. The backend is the only writer; anything another tool writes is read
through the same rules and reconciled.

---

## 1. Locations

| What | Default location | Notes |
|---|---|---|
| Application folder | `Quantos\application\` as unpacked from the release zip | Holds the executables only. |
| Client data | `Quantos\client-data\`, beside `application\` | Settings, cache, logs, WebView2 profile. Never inside the vault. |
| Vault | `%USERPROFILE%\Quantos\Vault` | Chosen at first start; can live anywhere the user can write. |

This is the layout in BUILD-PLAN.md section 4. If the executables are not in a folder named
`application` (for example, run straight from a build output folder), client data falls back
to `%LOCALAPPDATA%\Quantos\client-data`. Setting `QUANTOS_CLIENT_DATA` overrides both (used by
tests). Moving the whole `Quantos\` folder keeps settings; the vault path is stored absolute.

```
client-data/
  settings/
    client.json          vault path, window state, last workspace
    backend.json         discovery file (port, pid, token), current user only
  cache/
    cache.sqlite         derived data; delete freely
  logs/
    backend-YYYYMMDD.log
  webview2/              WebView2 user-data folder
```

The vault layout is the one in BUILD-PLAN.md section 4. Folders are created on demand.

## 2. Node files

A node is a UTF-8 Markdown file (no BOM written; a BOM on read is accepted and preserved)
with a YAML frontmatter block delimited by `---` lines. Files another editor saved as UTF-16 (with a
BOM) or in the Windows-1252 code page are read correctly and written back as UTF-8 when they next change.

```markdown
---
id: 01JABCDEF0123456789XYZABCD
title: Quarterly planning
type: note
workspace: default
tags: [project]
created: 2026-10-15T09:12:44-04:00
modified: 2026-10-15T09:40:02-04:00
schema: 1
status: Active          # any other key is a property
---
Body Markdown.
```

### Reserved keys

| Key | Type | Meaning |
|---|---|---|
| `id` | ULID string | Permanent identity. Never changes, survives renames and moves. |
| `title` | string | Readable title. Filenames derive from it but don't define it. |
| `type` | `note` \| `daily` \| `task` \| `person` \| `project` \| `event` \| custom | Built-in kind. |
| `workspace` | string | Workspace name. Must match the folder the file lives in; on mismatch the folder wins and a diagnostic is raised. |
| `tags` | list of strings | Supertag assignments, without `#`. |
| `aliases` | list of strings | Alternative titles for link resolution. |
| `created`, `modified` | ISO 8601 with offset | Recorded in the device's time zone at the time of the change. |
| `schema` | integer | Schema version of this file. Missing means 1. |
| `archived` | bool | Archive state (archived nodes also live under `archive/`). |
| `locked` | bool | Read-only mode: Quantos refuses edits to the note until the person unlocks it. Agents, plugins and automations can't unlock it. Link text, synced block copies and task checkboxes inside it still update. |

Every other key is a **property**. Unknown keys, comments and key order are preserved exactly:
the backend edits frontmatter by replacing only the lines of keys it changes, and appends new
keys at the end of the block. If the block can't be parsed as YAML, the file is read-only in
the visual editor and opens in raw-source mode with a diagnostic.

Property values are read as YAML writes them: unquoted `true`/`false` are booleans, unquoted `null`
or `~` is empty, and an unquoted number is a number when it reads back exactly as written (`1200`,
`-3`, `2.5`). Anything else is text, including quoted values (`"42"`) and values such as `007`,
`1.10`, `+5`, dates and times. Quantos writes numbers and booleans unquoted and quotes text that
would otherwise read as one.

A Markdown file with no frontmatter, or with frontmatter missing `id`, is **adopted** on
reconciliation: an `id` (and `schema: 1`) is added, everything else is left untouched.

### Task nodes

Tasks are nodes with `type: task` stored in `workspaces/<ws>/tasks/`.

| Key | Meaning |
|---|---|
| `status` | `todo` \| `doing` \| `waiting` \| `done` \| `cancelled` |
| `priority` | `1` (highest) to `4`, or absent |
| `due` | date or date-time — the deadline |
| `planned` | date — the day you intend to work on it; rollover changes only this |
| `effort` | minutes (integer) |
| `assignees` | list of node links to people |
| `project` | node link |
| `waiting_on` | free text or node link |
| `parent` | task id |
| `depends_on` | list of task ids |
| `recurrence` | RRULE subset: `FREQ=DAILY\|WEEKLY\|MONTHLY\|YEARLY;INTERVAL=n;BYDAY=MO,TU…` |
| `series` | ULID shared by every occurrence of a recurring task |
| `occurrence` | the scheduled date this occurrence belongs to (with `series`, unique) |
| `reminders` | list of `{at: datetime, snoozed_until: datetime?, fired: datetime?}` |
| `completed` | datetime the task was completed |
| `source` | id of the note the task was created from |
| `time_block` | `{start: datetime, end: datetime}` for the timeline |
| *(tracker keys)* | On daily notes: habit (`true`) and metric (number) values, one property per tracker listed under `trackers` in `settings.json` (Release 1.1). An unticked habit is simply absent. |
| *(whiteboards)* | A note with `type: canvas` whose body holds one ` ```canvas ` block in JSON Canvas 1.0 (jsoncanvas.org); note cards add `node` (the note's id) next to `file` (its path). Release 1.2. |
| `location`, `place` | Release 1.2 map: `location` is `"lat, lng"` in decimal degrees (also read: `coordinates: [lat, lng]`, `geo: {lat, lng}`, separate `lat` and `lng`/`lon`); `place` is an optional name. |
| `sprint` | Task: link to the sprint note it belongs to (Release 1.1). Sprint notes (`type: sprint`) have `start`, `end`, optional `sprint_goal` and `capacity` (minutes). |
| `goal`, `parent`, `target` | Release 1.1 goals: a project's `goal` links a goal note (`type: goal`); a goal's `parent` links a broader goal; `target` is a goal's date. |
| `time_spent` | Minutes of logged focus sessions (Release 1.1); each session is also a line under `## Focus` in the daily note: `- 09:00–09:25 · 25 min · [[id|Task]]` |
| `tz` | Time zone used for `due`/`planned`/reminders. New files use the IANA name (`America/Phoenix`); Windows names in older files remain valid. |

## 3. Inline references

| Syntax | Meaning |
|---|---|
| `[[01JID…\|Title]]` | Node link. The id is authoritative; the text after `\|` is the display title, rewritten on rename. |
| `[[Title]]` | Unresolved link as typed. On save the backend resolves it to the id form when exactly one node in the active workspace (then all workspaces) has that title or alias. Ambiguous links stay as typed and are reported so the UI can prompt. |
| `[[ws:Title]]` | Title lookup in another workspace. |
| `- [ ] text <!--task:01JID…-->` | Inline task line bound to a task node. Checking the box completes the task everywhere. The comment keeps raw Markdown readable in other editors. |
| `![[01JID…#^blockid]]` | Embedded block from another node. |
| `<!--sync:01JID…-->` … `<!--/sync-->` | Synced block; the canonical content lives in `synced_blocks/<id>.md`. |
| `^blockid` at end of a line | Block anchor (Obsidian-compatible). |
| `![alt](attachments/ab/abcdef….png)` | Attachment, stored content-addressed (below). |

## 4. File naming

- Node files: title lowercased, non-alphanumerics collapsed to `_`, trimmed, max 80 chars,
  `.md`. Empty → `untitled`. Collisions get `_2`, `_3`, … The name is a convenience only;
  identity is the `id`.
- Daily notes: `daily/YYYY/YYYY-MM-DD.md`.
- Tasks: `tasks/<slug>.md` with the same rules.
- Attachments: `attachments/<first 2 hex of SHA-256>/<sha256>.<ext>`, so identical files are
  stored once. The original filename is kept in the node that references it.
- Renaming a node renames its file and rewrites the display text of every `[[id|…]]` link, in note
  bodies and in property values (an `owner:` or `attendees:` link). Links in properties count as
  backlinks too.

## 5. History, trash and journal

- **History.** Before any write replaces a file, the previous content is copied to
  `.history/<id>/<UTC yyyyMMddTHHmmssfff>.md`. History is keyed by id, so it stays readable
  after the node moves or is renamed.
- **Trash.** Trashing moves the file to `.trash/<UTC timestamp>_<id>/<original filename>` and
  writes `manifest.json` beside it: id, title, original relative path, trashed time, and the
  ids of nodes that referenced it. Trash never expires. Restore puts the file back at the
  original path, or the next free name if it's taken, and reports the new path.
- **Journal.** Operations touching more than one file write
  `.state/journal/<opid>.json` first: the list of steps, each with the target path, the
  expected content hash before, and the new content's hash. Steps are marked done as they
  complete; the journal file is deleted at the end. On start, an unfinished journal is
  completed if every remaining target still has its expected "before" hash, and otherwise
  rolled back step by step using history; anything that can't be resolved either way is left
  untouched and raised as a diagnostic.
- **Notifications.** `.state/notifications.jsonl`, one JSON object per line, append-only.
- **Lock.** `.state/backend.lock`, held open exclusively by the running backend.

## 6. Writes

1. The caller supplies the node id and the content hash it last saw (`baseHash`).
2. The permission check runs.
3. If the file's current hash differs from `baseHash`, the write is refused as a conflict and
   the current disk content is returned. Nothing is overwritten.
4. The current content is copied to history.
5. The new content goes to `<name>.md.quantos-tmp` in the same folder, is flushed to disk, and
   replaces the target with `File.Replace` (`File.Move` for new files).
6. Derived indexes are updated, connected clients are notified, then success is returned.

Hashes are SHA-256 of the file bytes, lowercase hex.

## 7. Derived cache

`cache.sqlite` holds: `files(path, id, hash, mtime, size)`, `nodes`, `links`, `tasks`, a
full-text `search` table (FTS5), `properties`, `attachments_refs`. It carries its own
`cache_version`; a mismatch or any read error deletes and rebuilds it. Nothing in it is
authoritative.

## 8. Upgrades

When the app reads a file with a `schema` greater than it understands, the file is read-only.
Upgrades that change the format bump the schema number and migrate files through the write
service (so history is kept) only after the user agrees.

`settings.json` names the custom style applied to the app in `theme` (one file name, or absent
for the built-in look). Earlier versions kept a `themes` array of enabled styles; the first of
them becomes the applied one and the array is dropped, so only one style is ever applied.

