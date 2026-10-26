Quantos 0.2.2

Build 2026.26.10.2 (YYYY.DD.MM.revision, separate from the release version).

- Fixed dragging Windows window edges and corners to resize. The embedded browser no longer
  intercepts those drags; maximize and restore keep the resize border in step.
- Fixed the right sidebar's last close button, preserving its configuration for reopening.
- Connect the Windows app to an optional shared-vault server and configure server MCP write access.
- Imported/shared notes appear in their physical workspace without rewriting other apps' frontmatter.

This release publishes the Windows x64 portable package and a signed Android ARM64 Release APK for
phones and tablets (Android 8 or newer). The public APK is not debug-signed or debuggable. Private
debug APKs use a separate signing certificate and cannot update over the public APK or vice versa.
Keep using the same channel; export your vault before uninstalling to change channels.

The optional server/folder-bridge binaries remain separate builds; their source and setup
documentation are included in the repository. Android works offline without configuring a server.

Unzip the package and run Start Quantos.cmd, or update from Settings > Updates.

Windows ZIP SHA-256: 3cee1554487fdef0ccc15018e609c0483634331efc1e87f6a29dda12c24fc05c

Public Android APK SHA-256: 2be3e212548e55b854db0112b8cd40b69f546b3cb7688b42e473e2f48e4bd0ec
