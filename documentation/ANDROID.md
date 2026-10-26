# Quantos on Android

The Android app runs the same Svelte interface and .NET vault services as Windows. Notes, tasks,
projects, daily notes, queries, search, graphs, maps, whiteboards, mind maps, presentations, focus,
habits, planning, attachments, templates, backups, plugins and AI settings use the shared code.
It works with a local vault without a server or account. AI providers still require the network
when configured, just as on Windows.

## Phone and tablet

The existing Quantos wordmark, icon, fonts, charcoal surfaces, blue accent and user themes are
preserved. Phones use one page at a time, initially with **Daily notes, Tasks, Capture, Today and More**
at the bottom. **Capture** opens quick capture without leaving the current page.
To change it, open **More › Settings**, choose **Phone navigation** in **Settings section**, then
add/remove shortcuts and use the arrows to order them. Choose one to four shortcuts; **More** stays
last. Changes apply immediately, persist on this device and do not change tablet navigation.
**Capture** stays in the top bar; Notes has a readable recent-note list and **New note** action.
Open **More › Notes** for the simple notes list, even when Notes is not pinned in the bottom bar.
More also holds advanced tools, saved views, workspace switching and settings. The advanced notes
browser remains available there for filters, bulk actions and preview. Task grouping and board
controls are behind **Options**. Note properties and files open in **Details**, with an explicit
close button; note source, history and other secondary actions live in the note's overflow menu.
Settings uses a section picker on phones. For server setup, open **More › Settings**, then choose
**Shared vault** in **Settings section**; choose **MCP** there for agent write access.
Tablets retain the wider workspace, persistent sidebars where space permits, and split panes.
Closing the final right-hand panel hides the sidebar and keeps its configuration for reopening.
Pane dividers support touch as well as mouse and keyboard. Touch controls have 48 dp targets.
System bars, display cutouts, the keyboard and Android Back are handled by the native host.

PDF pages use Android's PDF renderer and keep Quantos's page citation controls. **More actions ›
Export › PDF** opens Android's print dialog, including Save as PDF. Markdown and HTML exports,
attachment selection and conflict-copy downloads use the system file picker. The local Terminal
uses Android `sh`; Windows PowerShell scripts and Windows global hotkeys remain Windows-specific.
Android notifications are enabled from the notification panel. Android may stop a background app;
missed reminders and pending sync resume when the app starts again.

## Keep a recoverable copy

The vault is in the app's private storage. Uninstalling Quantos removes that storage. Use
**Settings › Shared vault › Android files › Export vault** to save a ZIP elsewhere. Importing
a ZIP opens a separate vault copy and preserves the previous vault. Only portable vault content
is imported; device keys, locks and caches are excluded. Archives are limited to 1 GB unpacked,
with a 200 MB limit per file.

## Shared vault and MCP

See [SERVER.md](SERVER.md). A server is optional. Each device keeps its complete local vault;
editing continues offline and synchronises on reconnect. The server MCP works while the apps
are closed. **Settings › MCP › Shared server MCP** controls its note/task write access and copies
its connection configuration.

For a Tailscale-only server, Quantos must be allowed through the phone's VPN. If Tailscale uses
an included-app list, open **Tailscale › Settings › App split tunneling** and include **Quantos**
(`com.atomic22.quantos`). Otherwise the private server hostname may fail to resolve even while
Tailscale shows Connected.

## Build

Install .NET 10, the .NET Android workload (`dotnet workload install android`), JDK 17+, Android
SDK platform 36, and ASP.NET Core runtime **10.0.13**. ARM64 builds restore the matching managed
ARM64 runtime package automatically. The Android project packages the managed
ASP.NET assemblies because Microsoft does not ship an ASP.NET Android runtime pack. Override
`AspNetRuntimeDirectory` if that runtime is installed outside the usual .NET shared folder.
Do not add the Windows client's framework reference to this project.

```powershell
pwsh build/android.ps1                         # ARM64 phones and tablets, debug-signed test APK
pwsh build/android.ps1 -Runtime android-x64    # x64 emulator test APK
```

Output is in `publish/android/`, with SHA-256 files. Debug signing is for testing. A public release
needs a separately managed release signing key and the usual publication decision.
`android/Backend/` compiles the existing backend source and embeds the existing built frontend;
it is not a separate implementation of Quantos features.

AI provider and server keys are encrypted with Android Keystore. The backend listens only on
loopback. Native messages are accepted only from Quantos's app pages; plugin frames and external
pages cannot invoke the native bridge. External web links open in the user's browser.
