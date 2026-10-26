# Quantos: project context

## Overview
Quantos is a local-first productivity app (notes, tasks, projects, people) that keeps everything as
Markdown files with YAML frontmatter in a vault folder the user owns. One local backend serves a
Svelte interface in a Windows client (WebView2) and in the browser. Published by Atomic 22 Industries.

## Start here
- `documentation/HANDOVER.md`: how the owner works, open threads, this machine's quirks.
- `tools/dev/README.md`: run the app and check changes in a headless browser.
- `documentation/PROJECT-STATUS.md`: current state, what's built and where, unverified items, roadmap,
  design rules, build/test/release steps. Keep it current when you finish work.
- `BUILD-PLAN.md`: the approved plan, acceptance tests, roadmap (section 14) and decision log (section 17).
- `CHANGELOG.md`: what each release contains.
- `documentation/ANDROID.md`: Android host, phone/tablet layouts, public release signing and private debug builds.
- `build/VERSIONING.md`: Lohe-style release/build identity, artifact names and updater compatibility.
- `documentation/SERVER.md`: optional shared vault, offline sync, server MCP permissions and migration.

## Status
- BUILD-PLAN Releases 1.0, 1.1 and 1.2 are built. The 0.2.2 release set includes Windows x64 and a
  public release-signed Android ARM64 APK. The owner's phone stays on the private debug certificate.
  Semantic release and YYYY.DD.MM.revision build IDs are separate; About and filenames show both.

## Stack and layout
- .NET 10 self-contained single-file backend (127.0.0.1 only) and Windows client (WinForms + WebView2);
  SQLite derived cache in `client-data/cache/`; TypeScript frontend with Vite, Svelte 5 and CodeMirror 6.
- src/Quantos.Core (storage, write service, tasks, structure, AI, backups, updates, export),
  src/Quantos.Backend (API, security), src/Quantos.Client (Windows client, updater), src/Quantos.Cli
  (CLI + MCP + agent tool export), frontend (Svelte), tests/Quantos.Tests (xUnit), frontend/tests (Vitest).
- `android/` hosts the existing backend and built frontend inside .NET Android; it does not fork
  the product services. Android keys use Keystore. `src/Quantos.Server` is a portable sync/MCP service,
  deployed separately with a persistent vault. `Core/Sync` uses persisted three-way file hashes,
  conditional writes, retained recovery copies and explicit conflict review. App keys and MCP agent
  keys are separate; only app-authenticated requests can change server MCP write grants.
- `Quantos.FolderBridge` is a separately installed host worker for externally managed Markdown
  folders (including Google Drive). It explicitly opts one source into portable workspace paths,
  reuses WriteService/SyncEngine, preserves conflicts/recovery outside the source and stops on
  unavailable/empty folders or large deletion batches. Ordinary linked folders still never sync.
- Compact Android renders PhoneShell with Notes/Tasks/Today/More and PhonePanel for Details;
  the existing editor stays mounted beneath those temporary pages. Tablets retain the expanded
  workspace. PhoneNotes is a simpler entry point; the original notes browser remains in More.
  `prefs.phoneNavigation` keeps ordered phone shortcuts on the device; `phoneNavigation.ts`
  validates the destination list and reserves More. Settings changes never sync as vault content.
- Build with `pwsh build/build.ps1`; test with `dotnet test tests/Quantos.Tests` and `npx vitest run` in frontend.
- On Windows, `dotnet test tests/Quantos.Client.Tests` exercises the native script review dialog on
  an STA thread; the package build includes this tier. Shared native URL checks use
  `Core/Security/LocalOrigin`, and attachment file access uses `Core/Storage/FileBoundary`.
- `documentation/SECURITY-REMEDIATION.md` tracks implemented security batches and open findings.
  Tightened attachment/calendar/native boundaries do not yet fix shared-token actor impersonation.
- The owner tests Quantos from `C:\Users\davem\Apps\Quantos\Quantos\application` (vault in
  `C:\Users\davem\Quantos\Vault`). After every build, stage it there with
  `pwsh tools/dev/stage.ps1`: it closes the client, replaces the three programs with the ones from
  `publish\Quantos\application` (rename the old ones to `*.old` first — a running `.exe` can be
  renamed but not overwritten), copies the documents across, and starts `Quantos.exe` again. The owner
  has asked for this on each new release, without asking again.

## Conventions
- Every vault change goes through `WriteService`; never write vault files directly. Store feature data
  as plain note properties or Markdown and document new keys in documentation/STORAGE-SPEC.md.
- Person-only actions check the actor kind; nothing uses the network unless the person asks.
- Every user-visible change updates documentation/USER-GUIDE.md (also shown in the in-app Help).
- Preserve existing project instructions, configuration, credentials, and application data.
- Use Graft for code context when available, following the global and project Graft guidance.
- Herald compiler output replaces vendor files. Preserve and merge existing guidance before compiling.
- Public landing pages use dark app-specific compositions with real approved brand assets; shared Atomic 22
  navigation links to the blue Notes directory. Publish only site/ via GitHub Pages. Never serve or
  publish private workspace data.
