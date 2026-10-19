# Interface contracts

## Backend HTTP API

Base URL: `http://127.0.0.1:<port>/` (from `client-data\settings\backend.json`). JSON in and out,
camelCase. Errors: `{ "error": "…" }` with 400 (bad input), 401 (not signed in), 403 (not permitted),
404, 409 (conflict, or name taken), 421 (wrong host), 422 (invalid frontmatter or newer schema),
428 (approval required: `{ error, approval: { id, permission, description, … } }`).
Authentication and headers: see [SECURITY-MODEL.md](SECURITY-MODEL.md).

### Nodes

| Method and path | Purpose |
|---|---|
| `GET /api/nodes?workspace=&type=&archived=` | Summaries |
| `GET /api/nodes/{id}` | Full node: properties, body, exact `content`, `hash` |
| `POST /api/nodes` | Create `{ title, type?, workspace?, body?, tags?, properties?, folder? }` |
| `PUT /api/nodes/{id}` | Save full text `{ content, baseHash }` → 200, or **409** with `diskContent`/`diskHash` |
| `PATCH /api/nodes/{id}` | `{ baseHash?, set?, remove?, body? }` property or body change |
| `POST /api/nodes/{id}/rename` · `/move` · `/archive` · `/trash` · `/rollback` · `/preserve` | Journaled operations |
| `POST /api/nodes/{id}/extract` `{ text, title, embed? }` · `/merge` `{ other }` · `/apply-template` | |
| `GET /api/nodes/{id}/backlinks` · `/outlinks` · `/history` · `/history/{version}` · `/fragment?f=` | |
| `GET /api/resolve?title=&workspace=` | Title or alias lookup (all matches; never picks silently) |
| `POST /api/nodes/batch` | Structured paste |

### Daily, tasks, capture

`GET|POST /api/daily/{ws}/{date}` · `GET /api/daily-stream/{ws}?from=&to=` ·
`GET|POST /api/tasks` · `POST /api/tasks/{id}/status|update|snooze` · `POST /api/tasks/rollover` ·
`GET /api/tasks/calendar.ics` · `POST /api/tasks/calendar` · `POST /api/capture { text, workspace?, target: daily|inbox|task }`

### Structure and discovery

`GET|POST /api/supertags` · `PUT /api/supertags/{id}` · `POST /api/supertags/{id}/rename|migrate` ·
`GET|POST /api/templates` · `POST /api/query` (spec) · `POST /api/query/text` (YAML) ·
`GET /api/dictionary` · `GET /api/search?q=&workspace=` · `GET /api/graph?id=&hops=` ·
`GET|POST /api/synced-blocks`

### Vault

`GET /api/tree?workspace=` · `GET|POST /api/workspaces` · `POST /api/folders` ·
`GET|POST|DELETE /api/linked-folders[/{name}]` (folders linked in from elsewhere; `POST` takes
`{ name, path }`) ·
`GET /api/trash` · `POST /api/trash/{entry}/restore` · `DELETE /api/trash/{entry}?approval=` ·
`GET|PUT /api/config/{name}` (settings, views, shortcuts, toolbar, automations, … `.json`) ·
`GET|PUT /api/workspace-state/{ws}` · `POST /api/export/node/{id}` · `POST /api/export/folder` ·
`POST /api/attachments?name=` (raw body) · `GET /api/attachments/audit` · `POST /api/attachments/trash` ·
`GET /api/files/attachments/…` · `GET /api/diagnostics/integrity` · `GET /api/notifications` ·
`POST /api/notifications/read`

### System

`GET /api/ping` · `GET /api/status` · `POST /api/system/rebuild` · `POST /api/system/shutdown` (native only) ·
`POST /api/system/safe-mode` · `GET /api/setup` · `POST /api/setup/check|open` · `POST /api/auth/code` (native only) ·
`GET /api/approvals` · `GET /api/approvals/{id}` · `POST /api/approvals/{id}` (Windows client only) ·
`GET /api/permissions` · `PUT /api/permissions` (approval) · `GET /api/plugins` · `POST /api/plugins/enabled` ·
`GET /plugin-host/{name}` · `POST /api/shell/run` (approval) · `GET /api/shell/jobs/{id}` ·
`POST /api/shell/jobs/{id}/result` (Windows client only) · `GET /api/automations/recent`

### Push channel

`GET /api/events` is a server-sent event stream: `event: change` with
`{ kind, id, path, oldPath, hash, origin, data }`. Kinds: Created, Updated, Moved, Trashed, Restored,
Deleted, Archived, Unarchived, Conflict, Reindexed, Notification, Approval, ShellJob. `origin` ends
with `#<instance>` for writes made by a page that sent `X-Quantos-Instance`, so a page can ignore its
own changes. A comment line is sent every 20 seconds. The bundled frontend opens one stream per browser, not per tab: the
tab holding the `quantos-events` Web Lock relays events to the others over a `BroadcastChannel`.

`GET /api/status` includes `windowsClientConnected`: true while the Windows client has its push
channel open. It shows reminders natively, so browser pages then stay quiet instead of alerting twice.

## Native bridge (WebView2 `postMessage`)

Page → client: `ready`, `notify {title, body}`, `open-external {url}`, `open-scratchpad`,
`open-capture`, `close-popup`, `exit`, `pending-saves {count}`, `hotkeys {capture, scratchpad}`,
`terminal {action: open|input|stop|close, id?, data?}`, `run-code {code, cwd?}`, `selfcheck`.

Client → page: `client {version, captureHotkey, scratchpadHotkey}`, `flush` (reply with
`pending-saves`), `open-node {id}`, `hotkey-status {problems}`, `terminal {event: opened|output|clear|state|closed|denied, …}`,
`run-result {output, failed}`, `popup-shown`.

## Command line

See [CLI-REFERENCE.md](CLI-REFERENCE.md). Exit codes 0 ok, 1 failed, 2 usage, 3 not running, 4 not permitted.
