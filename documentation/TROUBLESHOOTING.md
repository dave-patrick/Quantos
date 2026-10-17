# Troubleshooting and recovery

Start with `Self-check.cmd` (or `Quantos.exe --selfcheck`). Reports are saved in `client-data\logs`.
Logs: `client-data\logs\backend-YYYYMMDD.log` and `client-YYYYMMDD.log`.

## Quantos won't start

| Symptom | What to do |
|---|---|
| Windows says the app was blocked, or nothing happens | The file may carry the "downloaded from the internet" mark. Right-click the zip before unzipping › Properties › **Unblock**, then unzip again. On a managed laptop, app control (AppLocker/WDAC) may block unsigned programs; ask IT, or use the browser option if only the client is blocked. |
| "This computer doesn't have the WebView2 runtime" | Use `Open in browser.cmd`. Everything except capture shortcuts, the scratchpad, notifications and the terminal works there. |
| "Another Quantos backend already owns the vault" | A backend is still running. Use Exit in the Windows client, or end `Quantos.Backend.exe` in Task Manager. The lock is released automatically if the process ended. |
| Browser shows "Open Quantos from the app" | Sign-in links work once and for a minute. Use `Open in browser.cmd` or the tray menu again. |
| Quick-capture shortcut does nothing | Another program uses it. Pick another in Settings › Shortcuts; Quantos reports conflicts. |
| The window reloads by itself | The backend stopped and the Windows client started it again. Unsaved text comes back with the window. If it happens three times in ten minutes the client stops trying and says so; the reason is in `backend-YYYYMMDD.log`. |
| Terminal says ConstrainedLanguage | App control is enforced on this machine. Commands still run, with PowerShell's restrictions. |

## Something looks wrong in my notes

- **A note changed in two places:** Quantos shows both versions side by side. Choose one; the other
  is kept in the note's history.
- **A note is read-only:** its frontmatter isn't valid YAML, or it was written by a newer Quantos.
  Fix the YAML in the source view (`</>`); nothing is lost in the meantime.
- **Links show in red or dotted:** the target is in the trash or was deleted outside Quantos.
  *Vault diagnostics › Check integrity* lists them.
- **Counts, search or backlinks look stale:** *Vault diagnostics › Rebuild cache*. The cache lives in
  `client-data\cache` and holds nothing that isn't in your files.

## After a crash or power cut

Quantos writes every file through a temporary copy and only swaps it in when complete, and records
multi-file changes (renames, moves, merges) in a journal before starting them. On the next start it:

1. removes temporary files left by an interrupted save (your previous version is intact);
2. finishes interrupted multi-file operations, or rolls them back if a file was changed meanwhile;
3. picks up anything edited outside Quantos while it wasn't running, keeping the old version in history.

*Vault diagnostics* shows what was recovered. If an operation couldn't be fully undone, its journal
is kept as `.state\journal\*.unresolved` and the files are left exactly as they were.

## Getting an old version back

- Open the note › clock button › choose a version › **Restore this version**. The current version is
  kept too.
- A note deleted outside Quantos: its last version is in `.history\<id>\` inside the vault.
- Trashed notes: Trash › Restore.

## Restoring from a backup

1. Exit Quantos.
2. Replace the Vault folder with your backup copy.
3. Start Quantos. It rescans the vault; use *Rebuild cache* if anything looks out of date.

## Starting completely fresh (keeping your notes)

Exit Quantos and delete `client-data\cache`. Settings and your vault are untouched; the cache is
rebuilt on the next start. Deleting all of `client-data` also resets window positions, shortcuts
for the Windows client, plugin switches and agent permissions.
