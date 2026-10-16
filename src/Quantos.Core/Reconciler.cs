using System.Text;
using Quantos.Core.Storage;

namespace Quantos.Core;

public sealed record Diagnostic(string Severity, string Code, string Message, string? Path = null, string? Id = null);

public sealed record ReconcileReport(
    int Scanned, int Changed, int Added, int Removed, int Adopted, int TempFilesRemoved,
    IReadOnlyList<RecoveryResult> Recovered, IReadOnlyList<Diagnostic> Diagnostics, TimeSpan Elapsed,
    bool Rebuilt = false); // the cache started empty (first open, new cache version, or a rebuild): every file counts as added

/// <summary>
/// Startup reconciliation (BUILD-PLAN section 6): before accepting writes, finish or roll back
/// interrupted journal operations, then bring the cache in line with the vault, recording history
/// for files changed outside the app while the backend was stopped.
/// </summary>
public sealed class Reconciler(WriteService writes)
{
    private Vault Vault => writes.Vault;

    public ReconcileReport Run(bool fullRebuild = false) => writes.Exclusive(() => RunLocked(fullRebuild));

    private ReconcileReport RunLocked(bool fullRebuild)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var diagnostics = new List<Diagnostic>();
        var temps = AtomicFile.CleanupTempFiles(Vault.Root);
        var recovered = writes.Journal.Recover();
        foreach (var r in recovered)
            foreach (var problem in r.Problems) diagnostics.Add(new Diagnostic("warning", "journal", $"{r.Description}: {problem}"));

        if (fullRebuild) writes.Cache.Clear();
        using var batch = writes.Cache.Batch();
        var cached = writes.Cache.AllFiles().ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var rebuilt = cached.Count == 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<(string Abs, string Rel, Node Node, bool IsNew)>();
        int scanned = 0, changed = 0, added = 0, adopted = 0;
        var ids = new Dictionary<string, string>(); // id -> rel path that owns it

        foreach (var abs in Vault.EnumerateNodeFiles())
        {
            scanned++;
            var rel = Vault.Relative(abs);
            seen.Add(rel);
            var info = new FileInfo(abs);
            if (cached.TryGetValue(rel, out var hit) && hit.MTime == info.LastWriteTimeUtc.Ticks && hit.Size == info.Length && Ulid.IsValid(hit.Id))
            {
                if (!ids.TryAdd(hit.Id, rel)) pending.Add((abs, rel, Node.FromText(rel, AtomicFile.ReadAllText(abs)), false));
                continue;
            }
            string content;
            try { content = AtomicFile.ReadAllText(abs); }
            catch (IOException ex) { diagnostics.Add(new Diagnostic("error", "unreadable", ex.Message, rel)); continue; }
            var node = Node.FromText(rel, content);
            if (hit is not null && hit.Hash == node.Hash && Ulid.IsValid(node.Id) && ids.TryAdd(node.Id, rel))
            {
                writes.Cache.Upsert(node, info.LastWriteTimeUtc.Ticks, info.Length); // only the timestamp moved
                continue;
            }
            pending.Add((abs, rel, node, hit is null));
        }

        foreach (var (abs, rel, node, isNew) in pending)
        {
            if (node.YamlError is not null)
            {
                diagnostics.Add(new Diagnostic("warning", "yaml", $"Frontmatter isn't valid YAML ({node.YamlError}). The file opens in source mode.", rel, node.Id));
                writes.Index(abs, node.Content);
                continue;
            }
            var duplicateOf = Ulid.IsValid(node.Id) && ids.TryGetValue(node.Id, out var owner) && !string.Equals(owner, rel, StringComparison.OrdinalIgnoreCase) ? owner : null;
            if (!Ulid.IsValid(node.Id) || duplicateOf is not null)
            {
                // Keep the identity with the file the cache already knew; give copies a new one.
                if (duplicateOf is not null && string.Equals(writes.Cache.PathOf(node.Id), rel, StringComparison.OrdinalIgnoreCase))
                {
                    var other = Vault.Absolute(duplicateOf);
                    ids[node.Id] = rel;
                    writes.Index(abs, node.Content);
                    var adoptedOther = writes.Adopt(other, "duplicate id");
                    ids[adoptedOther.Id] = adoptedOther.Path;
                    diagnostics.Add(new Diagnostic("info", "duplicate-id", $"{duplicateOf} was a copy of {rel}; it now has its own identity.", duplicateOf, adoptedOther.Id));
                    adopted++;
                    continue;
                }
                var reason = duplicateOf is null ? "no id" : "duplicate id";
                var adoptedNode = writes.Adopt(abs, reason);
                ids[adoptedNode.Id] = adoptedNode.Path;
                diagnostics.Add(new Diagnostic("info", "adopted",
                    duplicateOf is null ? $"{rel} had no identity; one was added." : $"{rel} was a copy of {duplicateOf}; it now has its own identity.", rel, adoptedNode.Id));
                adopted++;
                continue;
            }
            ids[node.Id] = rel;
            if (isNew) added++; else changed++;
            // Edited outside the app: keep the version so it's recoverable. A file seen for the first time is
            // its own only version, so there's nothing to copy yet.
            if (!isNew || Directory.Exists(Path.Combine(Vault.History, node.Id)))
                writes.History.Record(node.Id, Encoding.UTF8.GetBytes(node.Content), HistoryStore.External);
            writes.Index(abs, node.Content);
            if (node.Workspace.Length > 0 && Vault.WorkspaceOf(rel) is { Length: > 0 } folderWs && folderWs != node.Workspace)
                diagnostics.Add(new Diagnostic("warning", "workspace-mismatch", $"Frontmatter says workspace '{node.Workspace}' but the file is in '{folderWs}'. The folder wins.", rel, node.Id));
            writes.Changes.Publish(new ChangeEvent(isNew ? ChangeKind.Created : ChangeKind.Updated, node.Id, rel, Hash: node.Hash, Origin: "external"));
        }

        var removed = 0;
        foreach (var gone in cached.Keys.Where(p => !seen.Contains(p)))
        {
            var id = cached[gone].Id;
            writes.Cache.Remove(gone);
            removed++;
            // A file removed outside the app is gone from the vault, but history still has it.
            if (Ulid.IsValid(id) && writes.Cache.PathOf(id) is null)
            {
                diagnostics.Add(new Diagnostic("warning", "deleted-outside", $"{gone} was deleted outside the app. Its last version is in history.", gone, id));
                writes.Changes.Publish(new ChangeEvent(ChangeKind.Deleted, id, null, gone, Origin: "external"));
            }
        }

        return new ReconcileReport(scanned, changed, added, removed, adopted, temps, recovered, diagnostics, started.Elapsed, rebuilt);
    }
}
