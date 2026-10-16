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

        foreach (var abs in vault.EnumerateNodeFiles())
        {
            checkedFiles++;
            var rel = vault.Relative(abs);
            Node node;
            try { node = Node.FromText(rel, AtomicFile.ReadAllText(abs)); }
            catch (IOException ex) { list.Add(new Diagnostic("error", "unreadable", ex.Message, rel)); continue; }

            if (node.YamlError is not null) list.Add(new Diagnostic("warning", "yaml", $"Frontmatter isn't valid YAML: {node.YamlError}", rel, node.Id));
            if (!Ulid.IsValid(node.Id)) list.Add(new Diagnostic("warning", "no-id", "File has no identity yet; it will be adopted on the next rescan.", rel));
            else if (!ids.TryAdd(node.Id, rel)) list.Add(new Diagnostic("error", "duplicate-id", $"Same identity as {ids[node.Id]}.", rel, node.Id));
            if (node.Schema > Node.CurrentSchema) list.Add(new Diagnostic("warning", "schema", $"Schema {node.Schema} is newer than this app; the file is read-only.", rel, node.Id));
            var folderWs = Vault.WorkspaceOf(rel);
            if (node.Workspace.Length > 0 && folderWs.Length > 0 && node.Workspace != folderWs)
                list.Add(new Diagnostic("warning", "workspace-mismatch", $"Frontmatter says workspace '{node.Workspace}', folder says '{folderWs}'.", rel, node.Id));
            if (node.Archived != Vault.IsArchivedPath(rel) && folderWs.Length > 0)
                list.Add(new Diagnostic("info", "archive-mismatch", node.Archived ? "Marked archived but not in the archive folder." : "In the archive folder but not marked archived.", rel, node.Id));

            foreach (var link in Links.Find(node.Body).Where(l => l.IsResolved && !known.ContainsKey(l.Target)))
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
}
