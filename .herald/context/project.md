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

## Status
- BUILD-PLAN Releases 1.0, 1.1 and 1.2 are built; published as 0.1.x on GitHub Releases (the app updates
  itself from there). Remaining: the unscheduled roadmap items, which need the owner's scope decision.

## Stack and layout
- .NET 10 self-contained single-file backend (127.0.0.1 only) and Windows client (WinForms + WebView2);
  SQLite derived cache in `client-data/cache/`; TypeScript frontend with Vite, Svelte 5 and CodeMirror 6.
- src/Quantos.Core (storage, write service, tasks, structure, AI, backups, updates, export),
  src/Quantos.Backend (API, security), src/Quantos.Client (Windows client, updater), src/Quantos.Cli
  (CLI + MCP + agent tool export), frontend (Svelte), tests/Quantos.Tests (xUnit), frontend/tests (Vitest).
- Build with `pwsh build/build.ps1`; test with `dotnet test tests/Quantos.Tests` and `npx vitest run` in frontend.

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
