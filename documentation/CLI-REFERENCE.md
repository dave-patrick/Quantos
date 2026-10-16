# quantos-cli reference

`quantos-cli.exe` lives in `application\`. It talks to the running Quantos backend (start Quantos
first) using the access token in `client-data\settings\backend.json`, which only your Windows account
can read.

```
quantos-cli <command> [arguments] [--json] [--workspace <name>] [--agent <name>]
```

- Output is a readable table; add `--json` for machine-readable output.
- `--workspace` limits commands to one workspace.
- `--agent <name>` acts as a named agent, limited to the permissions you granted that agent.
- Exit codes: `0` success, `1` failure, `2` usage error, `3` Quantos not running, `4` not permitted.

| Command | What it does |
|---|---|
| `status` | Backend version, vault, number of notes, workspaces, warnings |
| `search <words>` | Full-text search |
| `list [--type note\|task\|daily\|…] [--tag T] [--limit N]` | Notes, newest first |
| `show <title or id>` | Print a note's Markdown with frontmatter |
| `new <title> [--body text\|-] [--tag T]… [--type T]` | Create a note (`--body -` reads stdin) |
| `append <title or id> [text]` | Append text (or stdin) to a note |
| `capture <text> [--to daily\|inbox\|task]` | Quick capture; default is today's daily note |
| `daily [YYYY-MM-DD]` | Print a daily note |
| `tasks [--all]` | Open tasks, by priority then due date |
| `task add <title> [--due D] [--planned D] [--priority 1-4]` | Create a task |
| `task done <title or id>` | Complete a task (inline checkboxes everywhere update) |
| `query "<yaml>"` | Run a query; use `\n` for new lines |
| `open [title or id]` | Open Quantos (or a note) in your browser |
| `run "<powershell>" [--cwd folder]` | Run a command after you approve it in the Windows client |
| `rebuild` | Rebuild the cache from the vault |
| `mcp [--agent name]` | Model Context Protocol server on stdin/stdout |

When a title matches more than one note, the CLI lists the ids instead of guessing.

## Examples

```
quantos-cli capture "Ask Pat about the Q4 budget" --to task
quantos-cli query "tags: [meeting]\nwhere: when >= today\nsort: when"
Get-Content notes.txt | quantos-cli new "Imported notes" --body -
quantos-cli tasks --json | ConvertFrom-Json | Where-Object priority -eq 1
```

## Connecting an AI agent (MCP)

Configure your MCP client to run:

```
C:\path\to\Quantos\application\quantos-cli.exe mcp --agent claude
```

Tools: `search_notes`, `read_note`, `create_note`, `append_to_note`, `list_tasks`, `create_task`,
`complete_task`, `run_query`, `capture`. A new agent can read only. Grant more in
*Settings › Plugins & permissions* (confirmed in the Windows client).
