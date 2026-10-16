# Quantos: project context

## Overview
Software project in Quantos. See existing project documentation for its purpose.

## Existing guidance
- Read `README.md` for existing project guidance; follow directory-specific instructions where present.

## Stack evidence
- `BUILD-PLAN.md` is the approved plan. Release 1.0 Stages 1-7 are built; the daily-use checkpoint and laptop test are pending.
- Planned stack: .NET 10 self-contained single-file backend (localhost only) and Windows client (WebView2); SQLite derived cache in `client-data/cache/`; TypeScript frontend with Vite, Svelte and CodeMirror 6.
- Nodes are Markdown files with YAML frontmatter, identified by a ULID in `id`. Every vault write goes through one write service.
- Layout: src/Quantos.Core (storage, write service, tasks, structure, export), src/Quantos.Backend (API, security), src/Quantos.Client (WinForms + WebView2), src/Quantos.Cli (CLI + MCP), frontend (Svelte), tests/Quantos.Tests (acceptance tests).
- Build the package with `pwsh build/build.ps1`. Run tests with `dotnet test tests/Quantos.Tests`.
- Every vault change must go through `WriteService`; never write vault files directly. Docs for formats and contracts live in documentation/.

## Conventions
- Preserve existing project instructions, configuration, credentials, and application data.
- Use Graft for code context when available, following the global and project Graft guidance.
- Keep this context accurate as project architecture and conventions are established.
- Herald compiler output replaces vendor files. Preserve and merge existing guidance before compiling.

- Public landing pages use dark app-specific compositions with real approved brand assets; shared Atomic 22 navigation links to the blue Notes directory. Publish only site/ via GitHub Pages. Never serve or publish private workspace data.
