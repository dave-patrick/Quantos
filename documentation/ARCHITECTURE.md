# Quantos architecture (as built, Stage 1)

See `BUILD-PLAN.md` for the full plan and `documentation/PLAN-REVIEW.md` for the decisions taken at Stage 1.

## Projects

| Path | What it is | Output |
|---|---|---|
| `src/Quantos.Core` | Vault layout, node file parsing, the single write service, history, trash, cache | library |
| `src/Quantos.Backend` | Local web service (loopback only) serving the API and the embedded frontend | `Quantos.Backend.exe` (self-contained single file) |
| `src/Quantos.Client` | Windows client: WebView2 window that starts or connects to the backend | `Quantos.exe` (self-contained single file) |
| `src/Quantos.Probe` | Stage 1 laptop test | `Quantos.Probe.exe` |
| `frontend/` | Shared TypeScript frontend; compiles into `src/Quantos.Backend/wwwroot/js` | embedded in the backend |
| `tests/Quantos.Tests` | Automated acceptance tests (xUnit) | — |

## Runtime picture

```
Quantos.exe (WebView2) ─┐
Browser tab ────────────┼─ http://127.0.0.1:47821  (token + Host + Origin checks)
Future CLI ─────────────┘            │
                              Quantos.Backend.exe ── holds Vault/.state/backend.lock
                                     │
                         WriteService (one write at a time)
                          │                    │
                Vault/ (authoritative)   client-data/cache/cache.sqlite (derived)
```

- **Connecting:** the backend writes `client-data/runtime/backend.json` (port, token, pid). The client and CLI read it. The browser gets the token from the launch link `#token=…`, keeps it in session storage and removes it from the address bar.
- **Saving:** the client sends the version (SHA-256 of the file) it started from. The write service re-reads the file and refuses with `409 version_conflict` if it changed. Otherwise it saves the old bytes to `.history/<id>/`, writes a temp file, flushes it to disk, atomically replaces the note, then updates the cache.
- **Crash safety:** a crash mid-save leaves the original note intact plus a `*.qtmp` temp file. On the next start that file is moved to `.state/recovered/`, never deleted.
- **Trash:** `.trash/<trashId>/` holds the note and a `manifest.json` with its original path. Restoring into a taken name uses `<name>_restored_N.md`. Trash never expires.
- **Cache:** rebuilt from vault files on every start (Stage 1 has no file watcher yet) and on demand. Files without an `id`, with duplicate ids, or with invalid YAML are listed under diagnostics and never rewritten.

## Node file format

```markdown
---
id: 0192f0c4-5a56-7648-92ee-f0580610d9da   # UUIDv7, permanent
title: "Meeting: Q4 plan"
workspace: default
supertags: []
schema: 1
created: "2026-10-15T14:31:39-07:00"
modified: "2026-10-15T14:31:40-07:00"
state: active
---
Markdown body…
```

Unknown keys, comments, key order, line endings and the BOM are preserved. Only the keys Quantos changes are rewritten.

## API v1 (all routes need the `X-Quantos-Token` header)

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/status` | Vault path, warnings, workspaces, diagnostics |
| GET | `/api/workspaces/{ws}/nodes` | List active notes |
| POST | `/api/nodes` | Create `{workspace, title, body}` |
| GET | `/api/nodes/{id}` | Read a note with its version |
| PUT | `/api/nodes/{id}` | Update `{expectedVersion, title?, body?}`. Returns 409 on conflict |
| POST | `/api/nodes/{id}/trash` | Move to trash `{expectedVersion}` |
| GET | `/api/nodes/{id}/history[/{entry}]` | Earlier versions |
| GET | `/api/daily/{yyyy-mm-dd or today}` | Daily note (created if missing) |
| GET | `/api/trash` | Trash entries |
| POST | `/api/trash/{trashId}/restore` | Restore |
| POST | `/api/cache/rebuild` | Delete and rebuild the cache |
| POST | `/api/shutdown` | Safe shutdown (used by the Windows client's Exit) |

## Building

```
cd frontend && npm ci && npm run build && cd ..
dotnet test tests/Quantos.Tests
dotnet publish src/Quantos.Client  -c Release -r win-x64
dotnet publish src/Quantos.Backend -c Release -r win-x64
```

The GitHub Actions workflow `.github/workflows/build.yml` does all of this on Windows and uploads the programs as downloadable artifacts.

## Not yet built (later Stage 1 items and Stage 2)

File watcher with the 300 ms debounce and side-by-side conflict view; operation journal for multi-file changes; rename/move with link updates; search (SQLite FTS5); live push to connected clients; the CLI.
