# Connecting an AI to Quantos

Start here if an AI (or script) on **another machine** needs to read or write your notes. This page
says which door to use, gives the exact wire details, and lists the usual reasons a connection fails.
Related pages: [AGENT-ADAPTERS.md](AGENT-ADAPTERS.md) (same-machine setup), [SERVER.md](SERVER.md)
(running the shared server), [API.md](API.md) (the desktop backend's HTTP API),
[SECURITY-MODEL.md](SECURITY-MODEL.md).

## Which door?

| The AI runs… | Use | Reachable from another machine? |
|---|---|---|
| On the same computer as Quantos | **stdio MCP**: `quantos-cli mcp --agent <name>` | No. It talks to the app over `127.0.0.1`. |
| On another computer | **Server MCP**: Streamable HTTP at `https://<server>/mcp` | **Yes. This is the one to use.** |
| Anywhere, no MCP support | Function calling through `quantos-cli call`, or the server MCP with plain HTTP (below) | Server MCP only |

The desktop app's backend (`http://127.0.0.1:<port>`, [API.md](API.md)) listens on this computer only
and needs a credential approved by a person in the Windows client, so a remote machine cannot use it.
For anything remote, run the optional shared server ([SERVER.md](SERVER.md)) and connect to its `/mcp`.

## What you need from the server

1. **The URL**, ending in `/mcp`. The AI's machine only has to be able to reach it; any of these work:
   - **Tailscale** (the standard Windows-host install): `https://<host>.<tailnet>.ts.net:8445/mcp`. The installer
     prints the HTTPS address; add `/mcp`. The AI's machine must be signed in to the same tailnet.
   - **Local network**: `http://<server's LAN address>:47831/mcp`, after installing with `-LanAddress` (see
     [SERVER.md](SERVER.md)). This is plain HTTP, so the key and your notes are not encrypted on the wire; use it
     only on a network you trust.
   - **Any HTTPS address** you put in front of the server (a reverse proxy or tunnel): `https://<your host>/mcp`.
     Needed when the AI runs in someone else's cloud.
2. **The MCP key**, a separate secret from the key the apps use. Get it with
   *Settings › MCP › Shared server MCP › Copy MCP connection*, which copies
   `{ "url": "…/mcp", "headers": { "Authorization": "Bearer <key>" } }`, or read
   `~/Apps/QuantosServer/keys/mcp-token` on the server host. Treat it like a password.
3. **Write access, if the AI should create or change anything.** New servers are read-only for MCP.
   Turn on *Allow the server MCP to create and edit notes and tasks* and choose *Save server permissions*.

The MCP key only works on `/mcp`. The app key (used for sync and for turning write access on) does not
work on `/mcp`, and the MCP key does not work on `/sync/*`. Using the wrong one gives `401`.

## Wire details

Checked with curl against a Quantos Server built from this source: the handshake, tool list, a
search, a write refused while read-only, a write after enabling access, `401` on a missing key and
on the app key, and `403` on an `Origin` header.

- **Transport:** MCP *Streamable HTTP*, **stateless**. Send each JSON-RPC message as an HTTP `POST` to
  `/mcp`. There is no session to open and no `Mcp-Session-Id` header to keep.
- **Headers on every request:**
  - `Authorization: Bearer <MCP key>`
  - `Content-Type: application/json`
  - `Accept: application/json, text/event-stream` (what the MCP specification requires clients to send)
- **Responses** are `text/event-stream`: one `event: message` whose `data:` line is the JSON-RPC
  response. A client that expects bare JSON must read the `data:` line. MCP client libraries do this.
- **Protocol version:** initialise with `2025-03-26`; the server accepts it.
- **Browsers and anything that sends an `Origin` header are refused with `403`.** Only non-browser
  clients (command-line tools, agent frameworks, servers) can connect. A web page cannot.
- **HTTP or HTTPS:** the server itself speaks plain HTTP on its port and does not care which address reached it.
  HTTPS comes from whatever sits in front (Tailscale, a proxy). The **Quantos apps' own sync** requires an
  `https://` address, but MCP clients take whatever URL they are given, so a plain `http://` LAN address works
  for MCP. Redirects are not followed with credentials.

### Smoke test with curl

Replace `URL` and `KEY`. Each call should return `200` with an `event: message` body.

```bash
URL=https://myserver.example.ts.net:8445/mcp
KEY=<the MCP key>
H=(-H "Authorization: Bearer $KEY" -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream")

# 1. Handshake
curl -s -X POST "$URL" "${H[@]}" -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}'

# 2. List the tools
curl -s -X POST "$URL" "${H[@]}" -d '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'

# 3. Call one
curl -s -X POST "$URL" "${H[@]}" -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"search_notes","arguments":{"query":"budget"}}}'
```

Without `-H "Accept: …"` this server still answers, but other MCP servers and some clients are strict, so send it.
A `/health` request needs no key and tells you whether the server itself is up:
`curl https://myserver.example.ts.net:8445/health` returns `{"ok":true,…}`.

## Client configuration

These are the shapes each client documents for a remote (HTTP) MCP server. Only the curl calls above
were run against Quantos; if a client's configuration has moved, follow its own documentation and
keep the same URL and `Authorization` header.

**Claude Code**

```
claude mcp add --transport http quantos https://myserver.example.ts.net:8445/mcp --header "Authorization: Bearer <MCP key>"
```

**Cursor** (`.cursor/mcp.json`)

```json
{
  "mcpServers": {
    "quantos": {
      "url": "https://myserver.example.ts.net:8445/mcp",
      "headers": { "Authorization": "Bearer <MCP key>" }
    }
  }
}
```

**Clients that only speak stdio** (Claude Desktop's config file is one) can bridge with
[`mcp-remote`](https://www.npmjs.com/package/mcp-remote), which runs locally and forwards to the URL:

```json
{
  "mcpServers": {
    "quantos": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "https://myserver.example.ts.net:8445/mcp", "--header", "Authorization: Bearer <MCP key>"]
    }
  }
}
```

**Your own code**: any MCP SDK's Streamable HTTP client (for example `streamablehttp_client` in the
Python SDK, or `StreamableHTTPClientTransport` in the TypeScript SDK) with the URL and the header.
If you would rather not use MCP, the three curl calls above are the whole protocol.

## The tools

The server exposes nine tools, the same ones the local stdio MCP offers. Arguments marked `?` are optional.

| Tool | Arguments | Needs write access |
|---|---|---|
| `search_notes` | `query`, `workspace?` | No |
| `read_note` | `note` (note id or exact title) | No |
| `list_tasks` | `workspace?`, `include_done?` (default false) | No |
| `run_query` | `query`: the YAML used in ` ```query ` blocks, e.g. `tags: [meeting]` | No |
| `create_note` | `title`, `body?`, `workspace?` (default `default`), `tags?` (array) | Yes |
| `append_to_note` | `note` (id or exact title), `text` | Yes |
| `create_task` | `title`, `due?` (`YYYY-MM-DD`), `priority?` (1–4), `workspace?` | Yes |
| `complete_task` | `task` (id or exact title) | Yes |
| `capture` | `text`, `to?` (`daily`, `inbox` or `task`; default `daily`) | Yes |

Results come back as text content holding JSON (notes as the full Markdown file, searches as a list of
`{ id, title, type, workspace, path, snippet }`). A title that matches several notes is refused: use the id.
There is no tool to delete, move or rename notes, run shell commands, or change permissions.

## When it doesn't work

| What you see | Cause and fix |
|---|---|
| Connection refused, timeout, DNS failure | The remote machine can't reach the server. Test `/health` from that machine first. Tailscale: it must be signed in to the same tailnet, and use port 8445. Local network: the server must have been installed with `-LanAddress`, Windows Firewall must allow the port (47831) from your subnet, and the address is the server host's LAN address. Port 47830 is bound to the host's loopback only on purpose. |
| TLS or certificate error | Use the `https://…ts.net:8445` address Tailscale serves, not the machine's IP. For a LAN address use `http://`, not `https://`: nothing on that port speaks TLS. |
| `401` | Missing, mistyped or wrong key. `/mcp` takes the **MCP key**, not the app key. Check there is no trailing newline or quote in the header, and that the word `Bearer`, a space, and the key are all present. A `401` with the right key means the server has no MCP key configured (`GET /sync/mcp` with the app key shows `"enabled": false`). |
| `403` | The request carried an `Origin` header. Browsers and some web-based agent hosts add one. Use a non-browser client, or one that doesn't send `Origin`. |
| `404` | Wrong path. The endpoint is `/mcp` (no trailing path), not `/` or `/api/…`. |
| Another `4xx` from a client library | Send `POST` with `Content-Type: application/json` and `Accept: application/json, text/event-stream`. This server answers a `POST` without `Accept`, but not every client does. |
| Tools list but writing fails | Write access is off. The error now says so ("…The server MCP is read-only unless write access is turned on…"). Turn it on under *Settings › MCP › Shared server MCP* and save. An older server build only says "An error occurred invoking 'create_note'"; update it. |
| "Several notes have this title" | Two notes share the title. Pass the note id (from `search_notes`). |
| A note was created but doesn't show in an app | The apps pick it up on their next sync (every 15 seconds while running and connected). *Sync now* in Settings forces it. |
| `/health` and `serverInfo` report version `0.2.0` | That number is not kept in step with the apps' version; it does not tell you which build is running. |

## For agents on the same computer

Use the stdio route in [AGENT-ADAPTERS.md](AGENT-ADAPTERS.md): `quantos-cli mcp --agent <name>`.
It acts as the agent you name, with the permissions you've granted that agent, and the first connection
needs approving in the Windows client. The desktop backend's HTTP API ([API.md](API.md)) is for programs
that can't start a process; it is likewise local-only and credential-gated.
