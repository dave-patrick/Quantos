# Quantos

**[Visit the Quantos website →](https://dave-patrick.github.io/Quantos/)** · [Build plan](BUILD-PLAN.md) · [All Atomic 22 apps](https://dave-patrick.github.io/JetPane/apps/)

Published by **Atomic 22 Industries**.

A local-first productivity workspace, now in early development, connecting notes, tasks, projects and people through readable Markdown files. The website explains the proposed workflow and architecture. There's no release yet, only early test builds.

## Project status

**Stage 1 (Foundation) is in progress.** The repository now contains:

- `BUILD-PLAN.md`: the approved build plan (now on .NET 10).
- `documentation/PLAN-REVIEW.md`: review findings and Stage 1 decisions.
- `documentation/ARCHITECTURE.md`: how the code fits together.
- `src/`: the local backend, the shared core, the Windows client and the laptop-test program. `frontend/` holds the shared TypeScript interface. `tests/` holds the automated tests.
- `site/`: the public landing page, published by GitHub Pages when it changes on `main`.

Test builds (not a release) are produced by the **Build and test** workflow on the Actions tab. Start with the laptop test: see `documentation/LAPTOP-TEST.md`.
