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
- `documentation/ANDROID.md`: Android host, phone/tablet layouts, native features and test APK build.
- `documentation/SERVER.md`: optional shared vault, offline sync, server MCP permissions and migration.

## Status
- BUILD-PLAN Releases 1.0, 1.1 and 1.2 are built; published as 0.1.x on GitHub Releases (the app updates
  itself from there). Remaining: the unscheduled roadmap items, which need the owner's scope decision.

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
- Compact Android renders PhoneShell with Notes/Tasks/Today/More and PhonePanel for Details;
  the existing editor stays mounted beneath those temporary pages. Tablets retain the expanded
  workspace. PhoneNotes is a simpler entry point; the original notes browser remains in More.
- Build with `pwsh build/build.ps1`; test with `dotnet test tests/Quantos.Tests` and `npx vitest run` in frontend.
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
