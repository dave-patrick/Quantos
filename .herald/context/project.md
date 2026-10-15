# Quantos: project context

## Overview
Software project in Quantos. See existing project documentation for its purpose.

## Existing guidance
- Read `README.md` for existing project guidance; follow directory-specific instructions where present.

## Stack evidence
- `BUILD-PLAN.md` is the approved plan. No application source exists yet; Stage 1 (Foundation) is next.
- Planned stack: .NET 10 self-contained single-file backend (localhost only) and Windows client (WebView2); SQLite derived cache in `client-data/cache/`; TypeScript frontend with Vite, Svelte and CodeMirror 6.
- Nodes are Markdown files with YAML frontmatter, identified by a ULID in `id`. Every vault write goes through one write service.
- Once source exists, confirm the stack from the source and build files.

## Conventions
- Preserve existing project instructions, configuration, credentials, and application data.
- Use Graft for code context when available, following the global and project Graft guidance.
- Keep this context accurate as project architecture and conventions are established.
- Herald compiler output replaces vendor files. Preserve and merge existing guidance before compiling.

- Public landing pages use dark app-specific compositions with real approved brand assets; shared Atomic 22 navigation links to the blue Notes directory. Publish only site/ via GitHub Pages. Never serve or publish private workspace data.
