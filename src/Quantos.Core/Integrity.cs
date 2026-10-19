using Quantos.Core.Markdown;
using Quantos.Core.Storage;

namespace Quantos.Core;

/// <summary>Integrity diagnostics (BUILD-PLAN 8D.12): problems a user can see and fix, never auto-repaired silently.</summary>
public static class Integrity
{
    public static (IReadOnlyList<Diagnostic> Diagnostics, int Checked) Check(WriteService writes)
    {
        var vault = writes.Vault;
        var list = new List<Diagnostic>();
        var known = writes.Cache.List(includeArchived: true, limit: 1_000_000).ToDictionary(n => n.Id);
        var checkedFiles = 0;
        var ids = new Dictionary<string, string>();

        CheckLinkedFolders(writes, list);

        foreach (var abs in vault.EnumerateNodeFiles())
        {
            checkedFiles++;
            var rel = vault.Relative(abs);
            Node node;
            try { node = Node.FromText(rel, AtomicFile.ReadAllText(abs)); }
            catch (IOException ex) { list.Add(new Diagnostic("error", "unreadable", ex.Message, rel)); continue; }

            if (node.YamlError is not null) list.Add(new Diagnostic("warning", "yaml", $"Frontmatter isn't valid YAML, so Quantos leaves this file alone. Fix it in a text editor and it will appear. ({node.YamlError})", rel, node.Id));
            else if (!Ulid.IsValid(node.Id)) list.Add(new Diagnostic("warning", "no-id", "File has no identity yet; it will be adopted on the next rescan.", rel));
            else if (!ids.TryAdd(node.Id, rel)) list.Add(new Diagnostic("error", "duplicate-id", $"Same identity as {ids[node.Id]}.", rel, node.Id));
            if (node.Schema > Node.CurrentSchema) list.Add(new Diagnostic("warning", "schema", $"Schema {node.Schema} is newer than this app; the file is read-only.", rel, node.Id));
            var folderWs = Vault.WorkspaceOf(rel);
            if (node.Workspace.Length > 0 && folderWs.Length > 0 && node.Workspace != folderWs)
                list.Add(new Diagnostic("warning", "workspace-mismatch", $"Frontmatter says workspace '{node.Workspace}', folder says '{folderWs}'.", rel, node.Id));
            if (node.Archived != Vault.IsArchivedPath(rel) && folderWs.Length > 0)
                list.Add(new Diagnostic("info", "archive-mismatch", node.Archived ? "Marked archived but not in the archive folder." : "In the archive folder but not marked archived.", rel, node.Id));

            // A line bound to a task that is neither here nor in the trash: the task is gone for good.
            foreach (var taskId in TaskMarkers(node.Body))
            {
                if (known.ContainsKey(taskId)) continue;
                if (writes.TrashBin.FindById(taskId) is not null) continue;
                list.Add(new Diagnostic("warning", "task-missing", "This line is bound to a task that no longer exists. Delete the line, or the task will come back when it is restored.", rel, node.Id));
            }

            var propertyLinks = node.Properties.Values.SelectMany(Cache.CacheIndex.PropertyStrings).SelectMany(Links.Find);
            foreach (var link in Links.Find(node.Body).Concat(propertyLinks).Where(l => l.IsResolved && !known.ContainsKey(l.Target)).DistinctBy(l => l.Target))
            {
                var trashed = writes.TrashBin.FindById(link.Target);
                list.Add(new Diagnostic(trashed is null ? "warning" : "info", "broken-link",
                    trashed is null ? $"Links to {link.Display ?? link.Target}, which no longer exists." : $"Links to “{trashed.Title}”, which is in the trash.", rel, node.Id));
            }
        }

        foreach (var file in Directory.EnumerateFiles(vault.Journal, "*.unresolved"))
            list.Add(new Diagnostic("error", "journal", $"An interrupted operation couldn't be fully undone: {Path.GetFileName(file)}. Its files were left as they were."));
        return (list, checkedFiles);
    }

    /// <summary>What is wrong with the folders linked in from elsewhere on the computer.</summary>
    private static void CheckLinkedFolders(WriteService writes, List<Diagnostic> list)
    {
        var vault = writes.Vault;
        foreach (var (name, path) in vault.LinkedRoots)
        {
            if (!Directory.Exists(path))
            {
                list.Add(new Diagnostic("warning", "linked-missing", $"The linked folder '{name}' isn't there any more ({path}). Its notes are not in the index until the folder comes back."));
                continue;
            }
            try
            {
                // A folder Quantos can't read is a folder it can't index. Nothing is written to it.
                using var e = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
                e.MoveNext();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                list.Add(new Diagnostic("error", "linked-unreadable", $"The linked folder '{name}' can't be read: {ex.Message}", null));
                continue;
            }
            foreach (var other in vault.LinkedRoots.Where(o => !string.Equals(o.Key, name, StringComparison.OrdinalIgnoreCase)))
            {
                var same = string.Equals(Path.GetFullPath(other.Value), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
                var inside = Path.GetFullPath(path).StartsWith(Path.GetFullPath(other.Value).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                if (same || inside)
                    list.Add(new Diagnostic("warning", "linked-overlap", $"The linked folders '{name}' and '{other.Key}' overlap, so the same files are counted twice.", null));
            }
        }
    }

    /// <summary>The tasks a body is bound to, by the hidden markers after the task lines.</summary>
    private static IEnumerable<string> TaskMarkers(string body) =>
        System.Text.RegularExpressions.Regex.Matches(body, @"<!--task:([0-9A-HJKMNP-TV-Z]{26})-->")
            .Select(m => m.Groups[1].Value).Distinct();
}
