# Working on Quantos

Start with these, in order; together they replace any briefing:

1. [documentation/HANDOVER.md](documentation/HANDOVER.md): how the owner works, open threads, this machine's quirks.
2. [documentation/PROJECT-STATUS.md](documentation/PROJECT-STATUS.md): what's built and where, what isn't verified, the roadmap, the design rules, how to build, test and release.
3. [tools/dev/README.md](tools/dev/README.md): run the app and check changes in a headless browser (`pwsh tools/dev/start.ps1`).
4. [BUILD-PLAN.md](BUILD-PLAN.md) and [CHANGELOG.md](CHANGELOG.md) as needed.

Keep HANDOVER.md and PROJECT-STATUS.md current when you finish work.


<!-- herald:start -->
## Herald project context

Read `.herald/main.yaml` and the Markdown files in `.herald/context/` at the start of coding work. Follow existing project and directory instructions alongside Herald. Keep Herald context current when architecture or conventions change. Do not run `herald compile` over existing vendor instruction files without preserving and merging their content; the compiler overwrites files.
<!-- herald:end -->
