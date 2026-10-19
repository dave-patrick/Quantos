# Development harness

Scripts for running Quantos locally and checking it in a real browser, built for both people and AI
agents (they need no display). Work data lives in `.dev\` at the repo root, which git ignores.

| Script | What it does |
|---|---|
| `pwsh tools/dev/start.ps1` | Copies the example vault to `.dev\Vault` (first time), starts the backend on 127.0.0.1:47821 (`dotnet run`, so it reflects current source), Vite on localhost:5173, and headless Edge with the DevTools protocol on 9333, then opens the app signed in. Parts already running are left alone. `-Fresh` resets the test vault and data; `-FakeAi` also starts a fake OpenAI-style AI provider on 127.0.0.1:11555; `-NoEdge` skips the browser. |
| `pwsh tools/dev/stop.ps1` | Stops everything `start.ps1` started. |
| `pwsh tools/dev/signin.ps1` | Prints a one-time sign-in link (`-Backend` for the backend's own built frontend instead of Vite). |
| `node tools/dev/edge.mjs 9333 <command> …` | Drives the headless Edge tab (see below). |
| `node tools/dev/axe.mjs here` or `… Notes Gantt …` | Accessibility scan (axe-core): the current page, or each left-sidebar entry by its label. Exits 1 if any serious or critical issue is found. |
| `node tools/dev/fakeai.mjs` | The fake AI provider on its own. In Settings › AI choose *Other (OpenAI-compatible)* with base URL `http://127.0.0.1:11555/v1`, models `fake-chat` and `fake-embed`. |

## Driving the browser

```
node tools/dev/edge.mjs 9333 nav "<url>"          # open a page (e.g. the sign-in link)
node tools/dev/edge.mjs 9333 eval "<js>"          # run JavaScript in the page and print the result as JSON
node tools/dev/edge.mjs 9333 shot out.png         # screenshot (1440x900)
node tools/dev/edge.mjs 9333 key k 2              # a key with modifiers: 1 Alt, 2 Ctrl, 4 Meta, 8 Shift (add them)
node tools/dev/edge.mjs 9333 type "some text"     # type text into the focused element
node tools/dev/edge.mjs 9333 click 400 300        # click at page coordinates
node tools/dev/edge.mjs 9333 drag 100 100 300 200 # real mouse drag (pointer events)
```

Tips:
- Open views through the UI (click sidebar buttons via `eval`), not by importing app modules: Vite
  serves changed modules under new URLs, so an `import('/src/…')` from a script can reach a stale copy.
- `Ctrl+K` (`key k 2`) opens quick open; type a title and press Enter to open a note.
- HTML5 drag and drop can be simulated with `new DragEvent(type, { dataTransfer })` in `eval`.
- After changing backend code, restart the backend: `pwsh tools/dev/stop.ps1` then `start.ps1`
  (or end just the process listening on 47821). Frontend changes hot-reload.
- The Windows client can't be driven headlessly. To check that it starts, run
  `src/Quantos.Client/bin/Debug/net10.0-windows/Quantos.exe --client-data <empty folder>` and read the
  `Startup:` line in that folder's `logs\client-*.log`, then stop the process. It opens a real window.
