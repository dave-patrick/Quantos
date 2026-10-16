# Quantos

**[Visit the Quantos website →](https://dave-patrick.github.io/Quantos/)** · [Build plan](BUILD-PLAN.md) · [All Atomic 22 apps](https://dave-patrick.github.io/JetPane/apps/)

Published by **Atomic 22 Industries**.

A local-first productivity workspace connecting notes, tasks, projects and people through readable
Markdown files. One local backend owns your vault; the same interface runs in a no-install Windows
client and in your browser.

## Project status

Release 1.0 is built to the [build plan](BUILD-PLAN.md), Stages 1–7. It hasn't been released yet:
the plan's daily-use checkpoint (a week of real use) and the test on the target laptop come first.
See [documentation/RELEASE-CHECKLIST.md](documentation/RELEASE-CHECKLIST.md).

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
| `documentation` | Storage spec, security model, API, user guide, troubleshooting, CLI reference, performance, release checklist |
| `site` | Public website (GitHub Pages) |
| `brand` | Locked wordmark and icon |

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
