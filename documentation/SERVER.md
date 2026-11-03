# Optional shared vault server

Quantos can remain entirely local, or Windows and Android can each keep a local copy of a shared
vault. The server owns the shared copy and serialises writes through `WriteService`. Apps retain
all their local features and continue editing when disconnected. They sync every 15 seconds
while running and connected. The first sync combines the two vaults; back up an existing vault
before connecting it.

## Link a folder managed by Google Drive or another app

Run the optional **folder bridge on the host that has the original folder**. It keeps the shared
server's workspace and the original Markdown files in sync in both directions. Windows and Android
then use that shared workspace normally, including offline edits. Other programs can keep using the
original folder. JSON, text files, application configuration and other non-Markdown files are excluded.
Original relative paths are preserved. New notes, daily notes and tasks in that workspace can create
their usual subfolders in the source; existing files are not rearranged.
The server stores its own copy in its persistent volume; Google Drive is not used for its database,
locks, checkpoints or recovery records.

Build with `pwsh build/folder-bridge.ps1`, copy the executable to the server host, and run
`server/install-folder-bridge.ps1` there with `-Folder`, `-Workspace`, `-Server`, `-TokenFile`,
`-Binary` and `-Start`. To register a known mapping before Google Drive is mounted, add
`-AllowUnavailableFolder` and omit `-Start`; the logon task starts after desktop sign-in. Missing
source folders are never recreated by the bridge. The token file is the existing protected **app server key**, not the MCP key.
For example, use folder `G:\My Drive\Notes` and workspace `shared-notes`. The installer registers a
hidden task for the current user's interactive session, where Google Drive is available; it starts
at sign-in and polls every 15 seconds while running. Keep that user signed in. Configure each folder
as a separate workspace/task. State defaults to `~/Apps/QuantosFolderBridge/<workspace>/` with private
ACLs. Source, workspace and pinned server identity cannot silently change under an old checkpoint.

Before the first connection, back up both copies. If a Windows app already links the same source
through Google Drive, unlink that **local Quantos link**, leaving its files intact, then use the shared
workspace instead. Otherwise the same note identities would be indexed twice.

Transfers use persisted three-way hashes and conditional writes. Outside edits and app edits made
concurrently keep both versions for review, including edit-versus-delete. Replaced versions remain
in recovery outside the source folder. An unavailable/empty source, an emptied established server
workspace, or a large removal batch pauses the bridge instead of propagating a mass deletion.
Ordinary Quantos note adoption still applies on the server: Markdown without a note identity can
receive one; unknown frontmatter and content are otherwise preserved by exact file transfers.

`status.json` records transfer counts, conflict count or the reason for a pause. Full conflict versions
are retained under `sync/conflicts/`. Stop the folder's scheduled task before running a manual check
with the same arguments plus `--once`, or resolving a reviewed conflict with
`--resolve <id> --keep folder` / `--keep server`. Restart the task afterwards. Never delete checkpoints
to dismiss conflicts or an unavailable-drive warning. Unlinking is done by stopping the task; it leaves
both the original folder and the server workspace intact. When migrating hosts, transfer bridge state,
recovery and access to the original folder too, and keep only one running bridge for that folder.

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

To point an AI on another machine at this, see [CONNECTING-AI.md](CONNECTING-AI.md): the URL, headers,
client configurations, a curl test and troubleshooting.

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

### Listening on the local network too

Tailscale is not required for the server MCP. To also reach the server from other computers on your local
network, install with a LAN address: `server/install-windows-host.ps1 -LanAddress 192.168.1.20` (the server
host's own address; `-LanPort` defaults to 47831). Add `-SkipTailscale` if the host doesn't use Tailscale. The
installer does not change the firewall; it prints the `New-NetFirewallRule` command that allows the port from
the local subnet only. With Compose, add a second `ports:` line such as `"192.168.1.20:47831:8080"`.

The LAN port is **plain HTTP**: the keys and your notes are not encrypted on the wire, so use it only on a
network you trust. An MCP client can use `http://192.168.1.20:47831/mcp`. The Quantos apps' own sync still
requires an `https://` address, so Windows and Android sync should keep using the Tailscale (or another HTTPS)
address. Allowing plain HTTP for app sync would be a code change and a security decision, not done here.

The server exposes `/health`, conditional `/sync` routes and `/mcp`. It does not expose the local
desktop backend's filesystem, shell, launch-token or setup endpoints. Browser cross-origin
requests are rejected. The standard install reaches the server through Tailscale HTTPS; a local-network address can be added (above).

## Run on Windows without Docker

`server/install-windows-native.ps1` runs the same server as a plain Windows program. It needs no Docker; the
.NET SDK is used once to build it (or pass `-Binary` with a folder you already published).

```
pwsh server/install-windows-native.ps1 -Start                                  # this computer only, http://127.0.0.1:47830
pwsh server/install-windows-native.ps1 -Listen http://192.168.1.20:47830 -Start  # also on your local network
pwsh server/install-windows-native.ps1 -TailscaleHttpsPort 8445 -Start         # also over Tailscale HTTPS
pwsh server/install-windows-native.ps1 -Uninstall                              # stop it and remove the scheduled task
```

It keeps everything under `~/Apps/QuantosServerNative/` (`-RuntimeDirectory` changes it): `app\` (the program),
`data\` (the vault, client data and server identity), `keys\` (the app key `server-token` and the MCP key `mcp-token`,
protected for your account) and `run-server.ps1` (the launcher). A scheduled task, *Quantos Server (native)*, starts it hidden at sign-in
(`-NoTask` skips it). The server uses this computer's Windows time zone. `-Listen` takes several addresses separated by
semicolons; anything but loopback is plain HTTP, and the script prints the firewall command that allows the port from your
local subnet only (it does not change the firewall). Use a different `-RuntimeDirectory` and port to run a second copy.
Connect an AI to it as described in [CONNECTING-AI.md](CONNECTING-AI.md); connect the apps with the app key.

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
