# Connecting Microsoft Copilot to Quantos

Quantos can be used from Microsoft's Copilot products that support **MCP** (Model Context Protocol), by
connecting them to the shared server's `/mcp` endpoint. Read [CONNECTING-AI.md](CONNECTING-AI.md) first for
the endpoint, the key and the wire details; this page covers what is specific to Copilot.

Checked against Microsoft Learn and the VS Code documentation on 2026-11-03. Only the Quantos side
(the endpoint, headers and tools) was tested; the Copilot screens below were **not** run against Quantos.
Microsoft changes these screens often, so if a label has moved follow Microsoft's page and keep the same URL,
header name and key.

## Which Copilot?

| Product | Works with Quantos? | What it needs |
|---|---|---|
| **GitHub Copilot in VS Code** (agent mode) | Yes | The server reachable from your computer: a Tailscale address, a local-network address or an HTTPS address all work. |
| **Copilot Studio** agents | Yes, with an API key | The server reachable **from Microsoft's cloud**: a public HTTPS address. Tailscale and local-network addresses will not work. |
| **Microsoft 365 Copilot** (declarative agents with MCP plugins) | Not directly | Microsoft's MCP plugin documentation lists OAuth 2.0 or Microsoft Entra single sign-on. Quantos's `/mcp` uses a static bearer key and has no OAuth. Use Copilot Studio instead. |
| The Copilot app or website (consumer) | Not covered | No way to add a custom MCP server was found. |

What you need from Quantos is the same in every case: the **URL** ending in `/mcp` and the **MCP key**
(*Settings › MCP › Shared server MCP › Copy MCP connection*). Quantos expects the key as
`Authorization: Bearer <MCP key>`. Writing is off until you turn on *Allow the server MCP to create and edit
notes and tasks*; leave it off unless the agent needs it.

## GitHub Copilot in VS Code

Needs a version of VS Code and the GitHub Copilot extension that support MCP, and Copilot's **Agent** mode
(tools are not used in Ask or Edit mode). Your organisation can restrict which MCP servers are allowed.

1. In your project create `.vscode/mcp.json` (or run **MCP: Add Server** from the Command Palette and choose HTTP):

   ```json
   {
     "servers": {
       "quantos": {
         "type": "http",
         "url": "https://myserver.example.ts.net:8445/mcp",
         "headers": { "Authorization": "Bearer ${input:quantos-key}" }
       }
     },
     "inputs": [
       { "type": "promptString", "id": "quantos-key", "description": "Quantos MCP key", "password": true }
     ]
   }
   ```

   VS Code asks for the key once and stores it, so it stays out of the file. For a local-network server use
   `"url": "http://192.168.1.20:47831/mcp"` (see [SERVER.md](SERVER.md)).
2. Open `mcp.json` and choose **Start** above the server, or run **MCP: List Servers**.
3. Open Copilot Chat, switch to **Agent**, open the tools picker and make sure the nine Quantos tools are ticked.
4. Ask something that needs your notes ("Search my Quantos notes for the Apollo launch"). VS Code asks you to
   approve each tool call the first time.

If the server shows an error, **MCP: List Servers › Show Output** has the HTTP status; see the table at the end.

## Copilot Studio

Copilot Studio calls your server from Microsoft's cloud, so the address must be reachable from the internet,
with a valid HTTPS certificate. It supports the Streamable HTTP transport (the one Quantos uses), not the old SSE one.

### 1. Give the server a public HTTPS address

A Tailscale `…ts.net` address and a `192.168…` address are **not** reachable from Microsoft. Options:

- **Tailscale Funnel** on the server's HTTPS port, which publishes that one port to the internet.
- A reverse proxy or tunnel you already trust (for example Cloudflare Tunnel) in front of the server's loopback
  port, with a certificate.

Think about this before doing it. Anyone on the internet can then reach `/mcp`; the only protection is the key
(64 hexadecimal characters, so not guessable) and TLS. Keep write access off, expose only `/mcp`, and treat the
key as revocable by replacing `mcp-token` on the server and restarting it. The sync routes use a different key
and are not usable with the MCP key, but they are still reachable if you publish the whole server; proxy only
the `/mcp` path if you can.

### 2. Add the tool in the Copilot Studio wizard

1. Open your agent, go to **Tools** › **Add a tool** › **New tool** › **Model Context Protocol**.
2. Fill in:
   - **Server name:** `Quantos`
   - **Server description:** a short sentence the agent uses to decide when to call it, for example
     *"Searches and reads the owner's Quantos notes and tasks, and (if allowed) creates notes and tasks."*
   - **Server URL:** `https://<your public host>/mcp`
3. **Authentication:** choose **API key**, **Type: Header**, and enter the header name `Authorization`.
4. Select **Create**, then **Create a new connection**. When it asks for the API key, enter the whole header
   value: `Bearer ` (with the space) followed by the MCP key.
5. Select **Add to agent**. The nine Quantos tools should appear under the agent's tools.

If the first call fails with `401`, the value reached Quantos without the word `Bearer`. Recreate the connection
with `Bearer <key>` as the value. If it still fails, see the table below.

### Alternative: a custom connector

If the wizard is not available to you, create a custom connector from this OpenAPI (Swagger 2.0) file in Power Apps
(**Tools › Add a tool › New tool › Custom connector › Import OpenAPI file**). `x-ms-agentic-protocol` is what marks the
operation as an MCP endpoint. Replace the host:

```yaml
swagger: '2.0'
info:
  title: Quantos
  description: Search, read and (if enabled) write the owner's Quantos notes and tasks.
  version: 1.0.0
host: quantos.example.com
basePath: /
schemes:
  - https
paths:
  /mcp:
    post:
      summary: Quantos
      x-ms-agentic-protocol: mcp-streamable-1.0
      operationId: InvokeMCP
      responses:
        '200':
          description: Success
securityDefinitions:
  api_key:
    type: apiKey
    in: header
    name: Authorization
security:
  - api_key: []
```

When you create the connection for it, the key value is again `Bearer <MCP key>`.

### Policies

Copilot Studio reaches MCP servers through Power Platform connectors, so a Power Platform data policy (DLP) that
blocks custom connectors also blocks Quantos. An administrator has to allow it.

## When it doesn't work

| What you see | Cause and fix |
|---|---|
| Copilot Studio cannot reach the server, times out | The address is not public. Test from outside your network (a phone on mobile data): `https://<host>/health` must return `{"ok":true,…}`. |
| `401` | Wrong or missing key, or `Bearer ` is missing from the value. `/mcp` takes the **MCP key**, not the app key. |
| `403` | The request carried an `Origin` header. Server-to-server calls from Copilot Studio normally do not; if a proxy or gateway adds one, remove it. |
| Tools appear but writing fails | Write access is off for the server MCP; turn it on in *Settings › MCP › Shared server MCP* and save. |
| VS Code: server won't start | Check the URL ends in `/mcp`, that you are in **Agent** mode, and **MCP: List Servers › Show Output** for the status. |
| VS Code: "blocked by your organisation" | An administrator restricts MCP servers. Ask for it to be allowed. |
| Microsoft 365 Copilot plugin asks for OAuth | Quantos does not support OAuth. Use Copilot Studio with an API key. |

To prove the server side independent of Copilot, run the curl calls in [CONNECTING-AI.md](CONNECTING-AI.md#smoke-test-with-curl)
with the same URL and key from a machine outside your network. If curl works and Copilot does not, the difference is in how
Copilot sends the key or reaches the address.

## Sources

- Microsoft Learn, *Connect your agent to an existing MCP server* (Copilot Studio):
  <https://learn.microsoft.com/microsoft-copilot-studio/mcp-add-existing-server-to-agent>
- Microsoft Learn, *Build MCP plugins* (Microsoft 365 Copilot):
  <https://learn.microsoft.com/microsoft-365/copilot/extensibility/build-mcp-plugins>
- VS Code, *MCP servers in Copilot*: <https://code.visualstudio.com/docs/copilot/customization/mcp-servers>
