# Optional shared vault server

Quantos can remain entirely local, or Windows and Android can each keep a local copy of a shared
vault. The server owns the shared copy and serialises writes through `WriteService`. Apps retain
all their local features and continue editing when disconnected. They sync every 15 seconds
while running and connected. The first sync combines the two vaults; back up an existing vault
before connecting it.

## Connect and review changes

In **Settings › Shared vault**, enter the server's HTTPS address and app server key, then choose
**Connect**. The key is encrypted for the current device and is not part of the vault. **Sync now**
checks immediately. **Disconnect** preserves the local vault and unsynced changes.

Each transfer checks a persisted base hash. Concurrent edits, including an edit against a
deletion, keep both versions for review. **Versions to review** lets you save either version and
choose which becomes current. Reviewed and replaced versions remain in device recovery storage.
The implementation uses conservative file conflicts rather than automatically merging text.
Unknown frontmatter and binary attachments are transferred unchanged. Device locks, caches,
provider keys, permissions, open-pane layouts and linked-folder absolute paths do not cross devices.

## Server MCP and write access

The server provides Streamable HTTP MCP at `/mcp`, using the official MCP C# SDK. Its nine tools
match the existing Quantos MCP tools: search/read notes, list tasks, run queries, create notes,
append to notes, create/complete tasks and capture text. They use the same vault services as the
apps. No shell or permission-administration tool is exposed.

Open **Settings › MCP › Shared server MCP** in either app. Enable **Allow the server MCP to create
and edit notes and tasks**, then choose **Save server permissions**. Turning it off revokes write
access immediately. This updates the server's grants; an offline permission change is not shown
as saved. **Copy MCP connection** copies a remote configuration including the separate agent key.
Keep that copied configuration private.

The app server key administers connections and MCP permissions. The MCP key authenticates only
the fixed `server-mcp` agent and cannot use the sync/admin routes or grant itself permissions.
New installations are read-only for MCP until write access is enabled. Neither key is returned
by ordinary status requests. The agent key is returned only for the person's explicit copy action.

## Run on T580 or another Windows Docker host

From this checkout, build `server/Dockerfile` with the repository root as its build context.
`server/install-windows-host.ps1` installs the resulting image, creates separate random app/MCP
keys protected by Windows ACLs, retains old containers during updates, and attaches the named
`quantos-data` volume. It binds port 47830 to the host's loopback only and adds private Tailscale
HTTPS on port 8445 without replacing existing Serve ports. It prints the HTTPS address to enter
in the apps. Keys are in `~/Apps/QuantosServer/keys/` on the host; do not commit or publish them.
The installer uses `America/Phoenix` for the owner's server time zone; its `-TimeZone` argument
and the Compose `TZ` setting control which day server MCP daily capture uses.

The server exposes `/health`, conditional `/sync` routes and `/mcp`. It does not expose the local
desktop backend's filesystem, shell, launch-token or setup endpoints. Browser cross-origin
requests are rejected. Tailscale access is required for the initial home-server setup.

## Move to a hosted server

The container is portable. `server/compose.yaml` is a starting point for Docker Compose, using
separate local `server-token` and `mcp-token` secret files and one persistent data volume. Put a
trusted HTTPS proxy in front of it; clients require HTTPS and do not follow credential redirects.

1. Stop the original container cleanly. Keep a verified backup of its entire `quantos-data` volume
   and both runtime key files. Do not run two writers against the same physical volume.
2. Restore the whole volume to the destination, including `Vault`, `server-id`, client data,
   permissions, history and recovery records. Supply the same two key files to the container.
3. Preserve the server's `TZ` setting, start the new container behind HTTPS and verify `/health`,
   authenticated sync and MCP.
4. Change **Server address** in each app, keeping its saved key. The connection retains its
   checkpoint and verifies the pinned server identity. Devices that were offline can then upload
   their pending changes.

Keeping `server-id` prevents a move from looking like a different, empty vault. If the destination
is intentionally a different vault, disconnect first and connect it as a new shared vault.
This first server is for one owner and their devices, with a separate MCP agent; multiuser
accounts and per-person sharing are not implemented.
