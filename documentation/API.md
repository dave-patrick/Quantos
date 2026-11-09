# Interface contracts

## Backend HTTP API

Base URL: `http://127.0.0.1:<port>/` (from `client-data\settings\backend.json`). JSON in and out,
camelCase. Errors: `{ "error": "…" }` with 400 (bad input), 401 (not signed in), 403 (not permitted),
404, 409 (conflict, or name taken), 421 (wrong host), 422 (invalid frontmatter or newer schema),
428 (approval required: `{ error, approval: { id, permission, description, … } }`).
Authentication and headers: see [SECURITY-MODEL.md](SECURITY-MODEL.md).

### Local connection authentication

Discovery `authVersion: 2` credentials allow only ping and pairing. Send a paired credential as
`Authorization: Bearer <credential>` for content APIs; actor identity comes from the credential,
never `X-Quantos-Agent` or `X-Quantos-Client`. Do not log credentials or sign-in URLs.

| Method and path | Purpose |
|---|---|
| `POST /api/auth/credentials/request` | Bootstrap only; `{ kind: "Agent"|"Cli", name, proof }`, random 64-hex proof; native confirmation required. CLI name must be `cli`. Returns approval metadata. |
| `POST /api/auth/credentials/poll` | Bootstrap only; `{ id, proof }`. Returns `{ state, token? }`; token appears only after native approval and requires the same secret proof. Requests expire after five minutes. |
| `GET /api/auth/whoami` | Authenticated actor metadata, no credential secrets. |
| `GET /api/auth/credentials` | User only; connection IDs, names, kinds and creation times. |
| `POST /api/auth/credentials/{id}/revoke` | User only; `{ approval? }`, requires native ManagePermissions confirmation. Revokes subsequent credential authentication. |
| `POST /api/auth/code` | Native may request app/browser sessions; paired personal CLI may request browser sessions only. Named agents and bootstrap are denied. |

### Nodes

| Method and path | Purpose |
|---|---|
| `GET /api/nodes?workspace=&type=&archived=` | Summaries |
| `GET /api/nodes/{id}` | Full node: properties, body, exact `content`, `hash` |
| `GET /api/nodes/{id}/summary` | One node's list entry (same shape as `GET /api/nodes` rows); 404 when gone |
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
`POST /api/export/site` (publishes a workspace or a linked folder as a small website; takes
`{ scope }`) · `POST /api/export/table` ·
`POST /api/attachments?name=` (raw body) · `GET /api/attachments/audit` · `POST /api/attachments/trash` ·
`GET /api/files/attachments/…` · `GET /api/diagnostics/integrity` · `GET /api/notifications` ·
`POST /api/notifications/read`

### System

`GET /api/ping` · `GET /api/status` · `POST /api/system/rebuild` · `POST /api/system/shutdown` (native/personal CLI) ·
`POST /api/system/safe-mode` · `GET /api/setup` · `POST /api/setup/check|open` · `POST /api/auth/code` (see above) ·
`GET /api/approvals` · `GET /api/approvals/{id}` · `POST /api/approvals/{id}` (Windows client only) ·
`GET /api/permissions` · `PUT /api/permissions` (approval) · `GET /api/plugins` · `POST /api/plugins/enabled` ·
`GET /plugin-host/{name}` · `POST /api/shell/run` (approval) · `GET /api/shell/jobs/{id}` ·
`POST /api/shell/jobs/{id}/result` (Windows client only) · `GET /api/automations/recent`

### Push channel

`GET /api/events` is a server-sent event stream: `event: change` with
`{ kind, id, path, oldPath, hash, origin, data, summary, seq }`. `summary` is the node's list entry after
the change (as in `GET /api/nodes`) for Created, Updated, Moved, Archived, Unarchived and Restored, or
null when not known (during a start-up scan). `seq` numbers events 1, 2, 3 … per backend run; a client
that sees a gap or a smaller number has missed events and should reload. Kinds: Created, Updated, Moved, Trashed, Restored,
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

## Optional shared-vault API

Windows app-managed folder bridges use person-only `/api/folder-bridges/`: `GET` reports platform
support, connection availability, normal workspaces and mapping status without credentials; `POST`
accepts `{folder, workspace}` and saves a stopped immutable mapping. `POST /{id}/start` enables it;
`POST /{id}/stop` durably disables and drains transfers. `DELETE /{id}` stops and archives the link
while retaining source/server files and all private recovery state. Replacement links use fresh state.
Workers run only while the local backend is open. Connection/vault changes stop and drain them.

The local `/api/sync` endpoints are restricted to the person using the app; agent and plugin actors
cannot configure connections or MCP permissions. `GET /api/sync/` returns connection status,
last successful sync, error and conflicts. `PUT /api/sync/` accepts `{url, token?}`; `DELETE` disconnects
without deleting either vault. `POST /api/sync/run` retries now. `POST /api/sync/conflicts/{id}` accepts
`{keepLocal}`; `GET /api/sync/conflicts/{id}/version?local=true|false` downloads the preserved copy.
`GET|PUT /api/sync/mcp` reads or saves `{writeAccess}`. Explicit `POST /api/sync/mcp/connection` returns
the MCP endpoint and separate Bearer header for the user's Copy action.

The separate server exposes public `GET /health`, app-key authenticated `/sync/manifest`,
`GET|PUT|DELETE /sync/file?path=…` (required `If-Match` hash or `missing`), `GET|PUT /sync/mcp`,
and `POST /sync/mcp/connection`. `/mcp` uses the separate agent key and Streamable HTTP. See
[SERVER.md](SERVER.md) for setup and migration. Android also maps authenticated, attachment-only
`GET /api/android/pdf-info?path=…` and `/api/android/pdf-page?path=…&page=1` on its local backend.

## Google Tasks sync API (optional)

All routes are user-only (403 for agents, plugins and automations, including `GET`). Errors: 400 validation, 409 not ready
(not connected, no preview, wrong vault), 502/503 Google problems.

| Route | Purpose |
|---|---|
| `GET /api/google-tasks` | Status: `state` (`Off`, `Connected`, `NeedsReconnect`, `Paused`), `reason`, `hasCredentials`, `flow` (`idle`, `waiting`), `mappings[]` (`workspace`, `listId`, `listTitle`, `confirmed`, `state`, `reason`), `intervalSeconds`, `lastSynced`, `nextAttempt`, `error`, `counts` (last cycle), `pendingDeletes[]`, `problems[]`. Never returns secrets. |
| `PUT /api/google-tasks/settings` | `{clientId?, clientSecret?, intervalSeconds?(30–900), mappings?}`. An empty secret keeps the saved one; changing the client ID or secret signs out; `mappings` omitted keeps them, `[]` removes all. |
| `POST /api/google-tasks/connect` | Starts sign-in; returns `{authUrl}` for the system browser. |
| `GET` / `POST /api/google-tasks/lists` | The account's lists / create one (`{title}`). |
| `POST /api/google-tasks/preview` | `{workspace}` → `{cutoff, createInGoogle, importToQuantos, link, excluded}`. Reads only. |
| `POST /api/google-tasks/confirm` | `{workspace, cutoff}`; needs a preview of that workspace and list made in this run of the app. |
| `POST /api/google-tasks/sync` | Runs a cycle (waits for one already running); returns `{pushed, pulled, deleted, linked, skipped, conflicts, pendingDeletes, problems}`. |
| `POST /api/google-tasks/deletes` | `{action: "apply" \| "cancel"}` for the held deletions. |
| `DELETE /api/google-tasks` | Disconnect: revokes the token, removes settings and sync state, keeps all tasks. |
| `GET /oauth/google/callback` | Anonymous, GET only: Google's redirect after consent. Valid only for a pending sign-in with the matching `state`. |

## MCP over HTTP (the app's own endpoint)

`POST /mcp` is a stateless Model Context Protocol endpoint (Streamable HTTP) on the backend's address. It takes
`Authorization: Bearer <agent connection key>` only; the personal CLI and app credentials are refused, a missing or wrong
key is `401` with `WWW-Authenticate: Bearer`, and `GET` or `DELETE` is `405`. Host and Origin are checked before anything
else, so only `127.0.0.1`/`localhost` and no cross-origin pages. Messages: `initialize`, `ping`, `tools/list`,
`tools/call` (single or batched; notifications get `202`). The tools and their grants are those of `quantos-cli mcp`.
Full details and a curl test: [CONNECTING-AI.md](CONNECTING-AI.md).

| Route | Purpose |
|---|---|
| `GET /api/mcp-http` | `{ url, transport, defaultAgent, tools[] }`. |
| `POST /api/mcp-http/connection` | User only. `{ name?, approval? }` creates an agent connection and returns `{ id, name, url, token, config }` once; needs native confirmation (428 with an `approval`, then retry with its id). Revoke it with `POST /api/auth/credentials/{id}/revoke`. |

## Command line reference

See [CLI-REFERENCE.md](CLI-REFERENCE.md). Exit codes 0 ok, 1 failed, 2 usage, 3 not running, 4 not permitted.

### Supertag scope

`PUT /api/supertags/{id}/scope` with `{ "scope": "videos" }` moves an active definition to an existing normal workspace; an empty scope shares it across workspaces. It preserves the definition ID and field data. Collisions, locked/read-only/archived/linked definitions and newly introduced effective inheritance cycles are refused before the move. Synthetic task or implicit tag IDs materialize a definition in the chosen scope and return its real ID; clients must use that returned ID for subsequent schema edits. Existing used tag names (including imported names with spaces) are preserved when materialized.
