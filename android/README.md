# Quantos Android

Native .NET Android host for the existing Quantos frontend and vault services, with phone and
tablet layouts. It can run fully offline and optionally sync with Quantos Server.

See [Android setup and build instructions](../documentation/ANDROID.md) and
[shared server and MCP configuration](../documentation/SERVER.md).

From the repository root: `pwsh build/android.ps1` builds an ARM64 test APK.
`Backend/` links existing backend source; it does not fork the API or product features.
