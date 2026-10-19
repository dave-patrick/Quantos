# Quantos

**[Visit the Quantos website →](https://dave-patrick.github.io/Quantos/)** · [Download](https://github.com/dave-patrick/Quantos/releases/latest) · [Build plan](BUILD-PLAN.md) · [Changelog](CHANGELOG.md) · [Project status](documentation/PROJECT-STATUS.md) · [All Atomic 22 apps](https://dave-patrick.github.io/JetPane/apps/)

Published by **Atomic 22 Industries**.

A local-first productivity workspace connecting notes, tasks, projects and people through readable
Markdown files. One local backend owns your vault; the same interface runs in a no-install Windows
client and in your browser.

## Project status

Releases 1.0, 1.1 and 1.2 of the [build plan](BUILD-PLAN.md) are built, and the app is published on
[GitHub Releases](https://github.com/dave-patrick/Quantos/releases) (latest: 0.1.4; it updates itself from
*Settings › Updates*). What's left is the plan's unscheduled roadmap (importing, hosting, accounts, sync).

**Picking up work (people and agents):** start with [AGENTS.md](AGENTS.md), which points to the handover notes and
[documentation/PROJECT-STATUS.md](documentation/PROJECT-STATUS.md):
what's built and where, what isn't verified yet, the roadmap, the design rules, how to build, test and
release. [CHANGELOG.md](CHANGELOG.md) has the per-release changes.

## Repository layout

| Path | What it is |
|---|---|
| `src/Quantos.Core` | Vault, nodes, the single write service, journal, history, trash, cache, tasks, supertags, queries, export |
| `src/Quantos.Backend` | Local web service (127.0.0.1 only): API, security, push channel, embedded frontend |
| `src/Quantos.Client` | Windows client: WebView2 window, tray, global shortcuts, notifications, PowerShell terminal |
| `src/Quantos.Cli` | `quantos-cli`: command line and MCP server for agents |
| `frontend` | TypeScript, Svelte and CodeMirror 6 interface shared by both clients |
| `tests/Quantos.Tests` | Acceptance tests from BUILD-PLAN section 13 |
| `examples/Example Vault` | Sample vault shipped in the package |
| `documentation` | Project status and handoff, storage spec, security model, API, user guide, troubleshooting, CLI reference, agent adapters, plugins, performance, release checklist |
| `site` | Public website (GitHub Pages) |
| `brand` | Locked wordmark and icon |
| `examples/agents` | Python helper for agent frameworks that call tools themselves |
| `tools/dev` | Development harness: run the app and check it in a headless browser |

## Building

Requirements: .NET 10 SDK, Node.js 22+.

```
pwsh build/build.ps1
```

This builds the frontend, runs the tests, publishes three self-contained single-file executables to
`publish/Quantos/application/`, and zips the package as `publish/Quantos-<version>-win-x64.zip`.
Nothing needs installing on the target machine.

Development:

```
dotnet run --project src/Quantos.Backend -- --open      # backend + browser
npm --prefix frontend run dev                            # live frontend on :5173, proxied to the backend
dotnet test tests/Quantos.Tests
```

The website at `site/` publishes automatically when those files change on `main`.
