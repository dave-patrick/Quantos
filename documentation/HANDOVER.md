# Handover: working on Quantos with its owner

What the previous agent learned about working on this project that isn't in the code or the plan.
Read [PROJECT-STATUS.md](PROJECT-STATUS.md) for the technical picture; this file is about the owner, the
open threads and the machine.

## The owner and how they work

- **The owner uses Quantos daily on Windows** and reports what they find in short messages
  ("convert to task doesnt seem to do anything", "startup is pretty slow"). Reproduce first, find the
  cause, fix it, then explain the cause in a sentence or two. Messages are informal and often have typos;
  read for intent.
- **Requests arrive while you're working.** Acknowledge them in a line, finish (and commit) the current
  piece, then take them in order. Don't drop any; keep a running list.
- **Keep going without being told.** The standing instruction is to carry on building and not stop
  for confirmation on ordinary work. Commit and push to `main` as each piece is done, with a clear
  commit message describing the user-visible change. No attribution lines in commits.
- **Ask before:** cutting a release, publishing anything, anything costing money, or starting a roadmap
  item without a scope decision. When a release is warranted, ask ("Do you want this as 0.1.5?"); when the
  owner says yes, follow PROJECT-STATUS.md section 8.
- **Reports:** short and concrete. What changed (by what the owner will see), how it was checked, what
  couldn't be checked, and the one question if there is one. Use plain English, no jargon. When telling
  the owner how to do something in the app, give the exact place (*Settings › Shortcuts*) and keys.
- **Explain cause and effect** when something was broken: what was actually wrong, not just that it's
  fixed. Admit when something couldn't be reproduced or tested.
- **Workspace locations:** git repositories live in `C:\repos\` (this one is `C:\repos\Quantos`).
  Working files that aren't repository code (notes, drafts, exports, research) go in
  `D:\My Drive\Dev Projects\<project>\`.
- **Website:** only `site/` is published (GitHub Pages). Never publish private vault or workspace data.

## Open threads (as of 0.1.5)

- **Released 0.1.5** (tag `v0.1.5`, zip `Quantos-0.1.5-win-x64.zip`, SHA-256
  `ac7c131a6af2974f71db4d82f913c6059469c05ce7e2db8783cce065a40525fe`): the split right sidebar,
  linked folders, the MCP settings page, slash commands, the block right-click menu, and
  custom styles applied with buttons. Ask the owner to try *Settings › Updates* on their machine:
  it is the first test of the updater fetching a real release.
- **Linked folders (released, not yet used by the owner):** the owner keeps notes in a separate
  Markdown editor and wants both to work on the same files. *Link a folder…* in the workspace menu
  does it. Ask which editor they use and whether the two behave as expected on their real notes.
- **First in-app update:** the owner was asked to update 0.1.3 → 0.1.4 through *Settings › Updates*. This
  is the first real run of the Windows client's install-and-restart step (`Updater.cs`,
  `MainForm.ApplyUpdateAsync`). If it failed, the client log `client-data\logs\client-<date>.log` says why;
  the old program files are kept as `*.old` until a new version starts.
- **Convert to task (`Ctrl+Alt+T`):** reported not working; the cause found was the pop-up scratchpad
  window not handling app shortcuts (fixed in 0.1.4). If it still fails in the main window on the owner's
  PC, another program may own `Ctrl+Alt+T`; the command palette entry would still work.
- **Quick capture into the open daily note:** fixed in 0.1.2 and confirmed by the owner.
- **Startup speed:** improved in 0.1.3 (uncompressed executables, parallel start). The client logs a
  `Startup:` line with phase timings; ask for it if the owner still finds starting slow.
- **Not yet tried by the owner:** real AI providers, the pop-up scratchpad send keys, the release
  checklist's laptop items.

Next candidates the owner has been offered: content importing (which apps their notes are in decides the
first importers), updating RELEASE-CHECKLIST.md for 1.1/1.2, code signing (needs a certificate from them).

## This machine and its tools

- **Windows 11**, shells: PowerShell 7 (`pwsh`) and Git Bash. .NET 10 SDK, Node 22+, Microsoft Edge
  (used for PDF export and headless testing). **Python isn't installed**; use Node for scripts.
- **GitHub:** `gh` is signed in as `dave-patrick`; the remote is `github.com/dave-patrick/Quantos`.
- **Network from an agent's sandbox:** package installs (`npm install`, `dotnet restore` of new
  packages) and GitHub API calls may fail with DNS errors inside a sandbox; they need to run outside it.
- **Editing files from a shell:** `sed`, `printf` and inline `node -e` mangle backslashes and `$`. For
  regexes, `\n` in strings and C# verbatim strings, use a proper file edit, or write a small script file
  and run it with `node`.
- `build/build.ps1` has CRLF line endings (exact-text edits must match them).
- **Dev harness:** `tools/dev/` (see its README) starts everything and drives a headless browser. An
  older, git-ignored `.devharness/` folder from the previous agent is obsolete and can be deleted.
- **The Windows client can't be clicked through headlessly**, and the owner prefers not to have agents
  control their desktop. Verify client changes by building, by a short launch that reads the startup log,
  and by asking the owner to try them.
