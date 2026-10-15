# Build plan review (Stage 1 start)

Reviewed: `BUILD-PLAN.md` (revised), October 15, 2026.

Overall the plan is strong. It is unusually clear about data ownership (vault vs. cache), the single write path, recovery and security. The items below are gaps or risks found during review, with the decision taken for Stage 1. Items marked **Needs your decision** are open.

## Changed

| # | Finding | Decision |
|---|---|---|
| 1 | .NET 8 reaches end of support on **November 10, 2026**, less than a month after build start. | **Use .NET 10 (LTS, supported to Nov 2028).** Decision log updated. Approved by Dave. |

## Gaps filled for Stage 1 (Recommended design)

| # | Gap in the plan | Stage 1 decision |
|---|---|---|
| 2 | How the browser gets the per-launch access token. | The backend writes `client-data/runtime/backend.json` (port, token, process ID) and prints a launch link: `http://127.0.0.1:<port>/#token=<token>`. The page reads the token from the link, keeps it in session storage and removes it from the address bar. The Windows client and the future CLI read the runtime file. |
| 3 | Which port the backend uses. | Default `47821` on `127.0.0.1` only. If it's taken, the backend picks a free port and records it in the runtime file. |
| 4 | Origin checks alone don't stop DNS-rebinding attacks. | The backend also checks the `Host` header (`127.0.0.1:<port>` or `localhost:<port>` only). Requests with an `Origin` header must match the backend's own origin. |
| 5 | "One backend per vault" needs a mechanism. | The backend holds an exclusive lock on `Vault/.state/backend.lock` while running. A second backend on the same vault refuses to start and points to the running one. |
| 6 | "The version it's expected to be at" isn't defined. | A node's version is the SHA-256 hash of its file bytes. Every update sends the version it started from; a mismatch returns a conflict and nothing is written. |
| 7 | Permanent ID format isn't defined. | UUID version 7 (time-ordered), stored as `id` in frontmatter. |
| 8 | Daily-note naming isn't defined. | `workspaces/<ws>/daily/YYYY-MM-DD.md`. |
| 9 | How "leave untouched parts exactly as they were" works. | Frontmatter is edited line by line: only the keys Quantos changes are rewritten; every other line, comment and unknown key stays byte-for-byte. The body is written only when it changes. |
| 10 | Timestamp format. | ISO 8601 with the device's UTC offset, for example `2026-10-15T14:05:00-07:00`. |
| 11 | Where `client-data/` lives. | Next to the application folder by default (portable). Can be overridden with `--client-data`. |
| 12 | Search technology. | SQLite FTS5 inside `cache.sqlite` (Stage 2). Stage 1 caches node metadata only. |

## Risks to watch

1. **Work laptop policy.** The plan tests the app on a KPMG laptop. Running self-built, unsigned programs on an employer-managed laptop may break company IT policy even when it technically works. Check with IT before installing. The Stage 1 probe also checks for the two most likely blockers:
   - Application control (AppLocker/WDAC/SmartScreen) blocking unsigned `.exe` files.
   - PowerShell running in **Constrained Language Mode**, which would limit the planned terminal and automations.
2. **Code signing.** If unsigned builds are blocked, a code-signing certificate becomes a Release 1.0 requirement. It isn't in the plan today.
3. **Disk growth.** Trash never expires and every save keeps history. Stage 1 diagnostics report history and trash size so growth is visible. A history-pruning rule will be needed by Release 1.1.
4. **Scope.** Release 1.0 has roughly 80 features across 7 stages. The daily-use checkpoint after Stage 3 is the right control. **Needs your decision (later):** consider moving lower-value editor items (multiple cursors, typewriter scrolling, multi-column layouts) to Release 1.1 if Stages 4-5 run long.
5. **WebView2 runtime.** It's built into Windows 10/11, but some managed devices remove or pin it. The probe checks for it.

## Stage 1 build order used

1. Portable probe (`Quantos.Probe.exe`), for the laptop test.
2. Solution structure, .NET 10.
3. Backend: loopback only, token, Host and Origin checks, runtime file, vault lock.
4. Vault selection and creation, OneDrive/synced-folder warning.
5. Node model, Markdown/YAML reader, schema version.
6. Single write service: version check, history snapshot, atomic write, cache update.
7. Trash and restore with manifests.
8. Cache rebuild.
9. Shared frontend shell (TypeScript) talking to the backend.
10. Windows client (WebView2) that starts or connects to the backend and minimizes on close.
