# Writing a Quantos plugin

A plugin is a small JavaScript program that adds commands to Quantos. It lives in your vault, runs
in a sandbox, and can only do what you grant it.

## Where plugins live

```
Vault\
  plugins\
    open-task-count\
      plugin.json
      main.js
```

Quantos lists every folder in `plugins` under *Settings › Plugins & permissions*. A plugin does
nothing until you tick **Enabled** there. **Safe mode** (same page) turns every plugin off at once.

## plugin.json

```json
{
  "name": "open-task-count",
  "version": "1.0.0",
  "description": "Adds a command that tells you how many tasks are open.",
  "main": "main.js",
  "permissions": ["Read"],
  "commands": [{ "id": "count", "title": "Count open tasks" }]
}
```

| Field | Meaning |
|---|---|
| `name` | Must match the folder name. |
| `version`, `description` | Shown in Settings. |
| `main` | The script to run. Defaults to `main.js`. |
| `permissions` | What the plugin asks for. Settings shows the list; granting is up to you. |
| `commands` | Listed in Settings so you know what the plugin adds. Register them in code too (below). |

### Permissions

| Name | Allows |
|---|---|
| `Read` | Reading notes, tasks and search results |
| `CreateEdit` | Creating and editing notes and tasks |
| `MoveArchive` | Moving and archiving |
| `TrashRestore` | Moving to the trash and restoring |
| `Network` | Outbound network requests (reserved) |

Granting is confirmed in the Windows client. Deleting permanently and running commands need your
approval in the Windows client every time, whatever is granted. Plugins can't change permissions,
and have no way to reach files outside the vault.

## main.js

The script runs in a sandboxed frame: no access to the Quantos page, your cookies, the file system
or the network. Everything goes through `window.quantos`, and every call is checked against the
plugin's grants.

```js
quantos.registerCommand('count', 'Count open tasks', async () => {
  try {
    const tasks = await quantos.api('GET', 'tasks?include=open');
    await quantos.toast(`${tasks.length} open task${tasks.length === 1 ? '' : 's'}`);
  } catch (err) {
    await quantos.toast(`Couldn't count tasks: ${err.message}`);
  }
});
```

| Function | Does |
|---|---|
| `quantos.registerCommand(id, title, fn)` | Adds *title (plugin name)* to the command palette; runs `fn` when chosen. |
| `quantos.toast(message)` | Shows a short message in Quantos. |
| `quantos.search(words)` | Full-text search. Needs `Read`. |
| `quantos.getNote(id)` | A note with its properties and Markdown. Needs `Read`. |
| `quantos.createNote(title, body)` | Creates a note. Needs `CreateEdit`. |
| `quantos.api(method, path, body)` | Any endpoint in `API.md` under `/api/`, except setup, system, sign-in, plugins and approvals. Returns the parsed JSON, or throws with the reason. |

A call the plugin isn't granted fails with a message such as *The plugin "open-task-count" isn't
allowed to read content*. Catch it and tell the user, as the example does.

## Trying it

1. Create the two files above in `Vault\plugins\open-task-count\`.
2. Open *Settings › Plugins & permissions*, tick **Enabled**, then grant **Read content** and approve
   it in the Windows client.
3. Press `Ctrl+Shift+P` and run *Count open tasks (open-task-count)*.

Plugins load when Quantos starts or when you enable them; reload the window after editing a plugin's
code.
