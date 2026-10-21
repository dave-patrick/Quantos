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
- **Every new build is staged into the owner's copy of the app, and the app is restarted.** The owner
  runs Quantos from `C:\Users\davem\Quantos\Quantos\application` (their vault is
  `C:\Users\davem\Quantos\Vault`). `pwsh tools/dev/stage.ps1` does the whole thing: it closes the
  running client, replaces the three programs in that `application` folder with the ones from
  `publish\Quantos\application`, refreshes the documents, and starts `Quantos.exe` again. The owner
  has asked for this to be done for each new release without asking again. Two things to know:
  rename the old files to `*.old` before copying the new ones over, because a running `.exe` can be
  renamed but not overwritten (this is what the updater does); and a hard-killed client leaves its
  WebView2 processes behind, which lock the browser profile and make the next start fail with
  `COMException 0x800700AA` ("The requested resource is in use") — the script closes the client
  properly and, if that happens anyway, moves `client-data\webview2` aside and starts again on a
  fresh profile. Don't kill the `quantos-cli.exe` processes; they are the MCP server. Afterwards read
  the `Startup:` line in `client-data\logs\client-<date>.log` to confirm it came up.
- **Workspace locations:** git repositories live in `C:\repos\` (this one is `C:\repos\Quantos`).
  Working files that aren't repository code (notes, drafts, exports, research) go in
  `D:\My Drive\Dev Projects\<project>\`.
- **Website:** only `site/` is published (GitHub Pages). Never publish private vault or workspace data.

## Open threads (as of 0.1.5.2)

- **Editor: collapsible headings and block selection (unreleased, on `main`).** Headings now carry
  their own arrow and fold by level, and blocks can be selected together (`Ctrl`-click,
  `Ctrl`-`Shift`-click), dragged to a new place, and worked on from the right-click menu. The
  *Collapsible headings* setting now really turns the arrows off; the Markdown language's own
  heading folding is dropped in `lib/editor/setup.ts` so that ours decides. Worth asking the owner
  to try: folding a section, dragging two blocks, and the right-click menu on a selection.
- **Released 0.1.5.3** (tag `v0.1.5.3`): fixes three faults in the 0.1.5.2 speed work — a query read
  only the first few thousand notes, so a filtered view left the rest out and the count of results was
  short; the notes browser asked the cache for a column that isn't there, so it showed no excerpts
  and its text search found nothing; and a note's parsed frontmatter was kept after a change, so
  reading a field could give the value from before the change. All three are fixed and covered by
  tests. **Ask the owner to update to 0.1.5.3** and to check that the notes browser shows the first
  lines of each note again.
- **Released 0.1.5.2** (tag `v0.1.5.2`): a patch release of speed fixes — a note's frontmatter is
  parsed once instead of on every lookup, saving no longer reads the note back off disk, the editor
  stops rebuilding on every keystroke, the cache's queries are bounded, and saving a file another
  program has locked gives up instead of retrying for ever.
- **The first published asset for v0.1.5.2 was packed by hand and was the wrong shape** (no
  `Quantos\` wrapper, the programs split into loose DLLs, and no documents or example vault), so the
  updater refused it and nobody could update to it. It has been replaced with one built by
  `build/build.ps1`, which now checks its own package before it is published. **The owner updated
  0.1.5.1 → 0.1.5.2 through *Settings › Updates* and the new version started**, so the updater's
  install-and-restart step has now worked on a real machine.
- **Released 0.1.5** (tag `v0.1.5`, zip `Quantos-0.1.5-win-x64.zip`, SHA-256
  `ac7c131a6af2974f71db4d82f913c6059469c05ce7e2db8783cce065a40525fe`): the split right sidebar,
  linked folders, the MCP settings page, slash commands, the block right-click menu, and
  custom styles applied with buttons.
- **Linked folders (released, not yet used by the owner):** the owner keeps notes in a separate
  Markdown editor and wants both to work on the same files. *Link a folder…* in the workspace menu
  does it. Ask which editor they use and whether the two behave as expected on their real notes.
- **First in-app update:** done — the owner updated 0.1.5.1 → 0.1.5.2 on their machine and the new
  version started. The Windows client's install-and-restart step (`Updater.cs`,
  `MainForm.ApplyUpdateAsync`) has now worked for real. If a later update fails, the client log
  `client-data\logs\client-<date>.log` says why; the old program files are kept as `*.old` until a new
  version starts. The updater only accepts a package laid out as `Quantos\application\...`
  (`UpdateService.Unpack`), so the package must come from `build/build.ps1` and never be zipped by
  hand.
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
