# Quantos 0.2.9 release verification

Windows x64 · build **2026.03.11.38** · November 3, 2026

- [x] Advanced the release to 0.2.9 (numeric, for Windows updater compatibility); build revision 38.
- [x] The app serves a stateless Streamable HTTP MCP endpoint (`POST /mcp`, loopback only, agent connection keys created in Settings › MCP › HTTP endpoint); the shared server can run without Docker (`server/install-windows-native.ps1`). Covered by six new endpoint tests and a real start, health and `/mcp` check of the native server. Not exercised: creating a key through the Settings screen by hand, an external MCP client against the app endpoint, and the native script's scheduled task.
- [x] Built without a built-in Google client: no executable in the package contains the owner's client ID.
- [x] Full build: 154 frontend, 342 backend and 6 client tests pass; package/ZIP checks pass. One earlier attempt failed on a timing-sensitive AI search test that passes on rerun.
- [x] Source commit `90b2104` pushed to main; numeric v0.2.9 tag points to it. All three executables report ProductVersion 0.2.9 plus that commit.
- [x] Windows ZIP and checksum published as a non-draft, non-prerelease latest release; GitHub asset digest matches local SHA-256. In-app updater install was not exercised.

Android and server delivery are outside this Windows-only release; Android downloads remain on 0.2.2.

Public release: https://github.com/dave-patrick/Quantos/releases/tag/v0.2.9

Windows ZIP SHA-256: `ecb7e3e66ff72341db62182b27c91887b6c98a28cdc6acd7cb1015a23b662653`.
