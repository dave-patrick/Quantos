# Permission and security model

Quantos 1.0 is single-user and local. The threats it defends against are other software on the same
computer (web pages, other local programs), untrusted content inside the vault (pasted notes,
plugins, imported files), and agents acting beyond what the user allowed.

## Processes and trust

| Process | Runs | Talks to |
|---|---|---|
| `Quantos.Backend.exe` | Owns the vault. The only writer. Listens on `127.0.0.1` only. | Everyone, after authentication |
| `Quantos.exe` (Windows client) | WebView2 window, tray, global shortcuts, notifications, PowerShell host | Backend (launch token), its own pages (bridge) |
| Browser tab | The same frontend | Backend (session cookie) |
| `quantos-cli.exe` | Commands and the MCP server for agents | Backend (launch token, optionally as a named agent) |

## Authentication (BUILD-PLAN 10.6-10.9)

- A new random **launch token** (256-bit) is created at every backend start. It is written to
  `client-data\settings\backend.json`, readable by the current Windows user only (ACL set on write).
- The Windows client and CLI send it as `Authorization: Bearer`. Page script never sees it.
- Pages get a session by **one-time code**: a native caller asks `POST /api/auth/code`, opens
  `/auth/open?code=…`, and the backend exchanges the code (single use, 60 seconds) for an
  `HttpOnly`, `SameSite=Strict` cookie, then redirects to a clean URL.
- Every request is checked for:
  - `Host` exactly `127.0.0.1:<port>` or `localhost:<port>` (blocks DNS rebinding),
  - `Origin`, when present, equal to the backend's own origin,
  - `Sec-Fetch-Site` not `cross-site`,
  - a valid token or session,
  - for cookie-authenticated writes, the `X-Quantos-Request` header (a cross-site form can't send it).
- Responses carry a strict Content-Security-Policy (`script-src 'self'`, no inline script, no
  objects, same-origin frames only), `nosniff` and `no-referrer`.

## Actors and permissions (BUILD-PLAN 10)

Every write goes through the single write service, which checks the actor's permission first.

| Actor | Granted by default |
|---|---|
| User (app or browser session, Windows client) | Everything, subject to approvals below |
| CLI (launch token, no agent name) | Content permissions; not files outside the vault, network, or managing permissions |
| Agent (`X-Quantos-Agent: name`) | Read only |
| Plugin (`X-Quantos-Plugin: name`) | Nothing |
| Automation (rule name) | Read, create and edit |

Categories: read; create and edit; move and archive; trash and restore; permanently delete; run
local commands; access files outside the vault; network requests; manage permissions.

- Grants live in `client-data\settings\permissions.json`, **outside the vault**, so content in the
  vault can never grant itself permissions.
- Find and replace across notes is the person's alone; agents and plugins edit one note at a time.
- A **locked** note (`locked: true`) refuses edits from every actor, the person included, until it is
  unlocked; only the person (app, browser session or CLI) can unlock it.
- Agents and plugins are also checked on every read, and can't reach setup, system, sign-in,
  plugin or approval endpoints at all.

## Approvals (BUILD-PLAN 10.10)

Shell execution, permanent deletion and changing permissions **always** need an approval, whoever
asks. The backend creates a pending approval describing exactly what will happen (for commands: the
exact text and folder). Only the Windows client can decide it, after a native confirmation dialog.
Approvals are single-use, tied to that exact action, and expire after five minutes. A browser
request alone can never trigger these actions.

## The native bridge

- WebView2 host objects are disabled; the page can only post messages.
- The client accepts messages only from the backend's origin, and only these types: ready, notify,
  open-external (http/https/mailto only), open-scratchpad, open-capture, close-popup, exit,
  pending-saves, hotkeys, terminal, run-code, selfcheck.
- Navigation away from the backend origin is cancelled and opened in the default browser.
- **Terminal:** each session is confirmed in a native dialog before it starts; it runs with the
  user's own permissions.
- **Code in notes never runs automatically.** The Run button on a PowerShell block shows the exact
  code and folder in a native dialog first.

## Untrusted content

- Markdown rendered as HTML (previews, embeds, exports) is sanitised (DOMPurify in the app; Markdig
  with raw HTML disabled in exports). Scripts and event handlers never survive.
- Plugins run in sandboxed frames (`sandbox="allow-scripts"`, opaque origin, no cookies) served with
  a per-response nonce CSP. They can only ask the app to call the API on their behalf; each call
  carries the plugin's name and its grants apply. **Safe mode** stops plugins loading and denies any
  plugin request.
- Automations act as their own actor and stop, with a recorded reason, when they need a permission
  they don't have.

## AI providers (Release 1.1)

- AI is off until switched on in *Settings › AI*. Then the backend talks only to the providers
  chosen there; nothing else in Quantos makes outbound requests.
- Local providers (Ollama, LM Studio, any address on this computer) keep everything on the machine.
  With a remote provider, a request sends only what it needs: a question plus the note passages it
  draws on, the selected text for a rewrite, or note passages while building the meaning-search index.
- API keys are kept in `client-data/settings/ai-keys.json`, encrypted with Windows DPAPI for the
  current user. They're never written to the vault, never returned to a page, and can't be read on
  another account or machine.
- Choosing providers and saving keys is for the person only (user or CLI). Agents and plugins that
  call AI endpoints need the **Network** permission, plus **Read** to use notes.
- Claude requests use Anthropic's server-side fallback, so a request Claude declines on policy
  grounds can be answered by another Claude model; if none can, the answer says it was declined.
- The meaning-search index (`client-data/cache/embeddings.sqlite`) is derived data like the cache:
  outside the vault, rebuilt when the model changes.

## Data safety

See [STORAGE-SPEC.md](STORAGE-SPEC.md): atomic writes, conflict checks by content hash, history
before every overwrite, journaled multi-file operations with recovery, trash that never expires.
