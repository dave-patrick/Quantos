using System.Text;

namespace Quantos.Core.Storage;

/// <summary>
/// Watches the vault for changes made outside the app (BUILD-PLAN section 6). Events are grouped
/// with a 300ms sliding debounce (confirmed), then processed through the same indexing and
/// conflict checks as everything else. The debounce doesn't make writes safe; the write steps and
/// hash checks do.
/// </summary>
public sealed class VaultWatcher : IDisposable
{
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);
    /// <summary>How often the watcher checks that every folder it should watch is being watched.</summary>
    public static TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(15);

    private readonly WriteService _writes;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _timer;
    private readonly Timer _sweep;
    private bool _fullScan;
    private bool _disposed;

    private static readonly string[] Ignored = [".history", ".trash", ".state", "exports", "plugins", "themes", "attachments", ".git", ".obsidian"];

    public VaultWatcher(WriteService writes)
    {
        _writes = writes;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        // A watched folder can be deleted and made again, which leaves the watcher blind. The sweep
        // notices a folder that is being watched but has no watcher, and starts watching it again.
        _sweep = new Timer(_ => Sweep(), null, SweepInterval, SweepInterval);
        Watch();
    }

    /// <summary>Makes a watcher for any folder that should be watched but isn't, and rescans.</summary>
    private void Sweep()
    {
        if (_disposed) return;
        var wanted = new List<string> { _writes.Vault.Root };
        wanted.AddRange(_writes.Vault.LinkedRoots.Values);
        var missing = false;
        lock (_gate) missing = wanted.Any(r => Directory.Exists(r) && !_watchers.ContainsKey(r));
        if (!missing) return;
        Watch();
        lock (_gate) _fullScan = true;
        Kick();
    }

    /// <summary>Watches the vault and every linked folder. Called again when a folder is linked or unlinked:
    /// a new folder starts being watched and an unlinked one stops.</summary>
    public void Watch()
    {
        var wanted = new List<string> { _writes.Vault.Root };
        wanted.AddRange(_writes.Vault.LinkedRoots.Values);
        lock (_gate)
        {
            foreach (var root in wanted.Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            {
                if (_watchers.ContainsKey(root) || !Directory.Exists(root)) continue;
                var fsw = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
                };
                fsw.Changed += (_, e) => Queue(e.FullPath);
                fsw.Created += (_, e) => Queue(e.FullPath);
                fsw.Deleted += (_, e) => Queue(e.FullPath);
                fsw.Renamed += (_, e) => { Queue(e.OldFullPath); Queue(e.FullPath); };
                fsw.Error += (_, _) => Drop(root);
                fsw.EnableRaisingEvents = true;
                _watchers[root] = fsw;
            }
            foreach (var root in _watchers.Keys.Where(r => !wanted.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList())
            {
                _watchers[root].Dispose();
                _watchers.Remove(root);
            }
        }
    }

    /// <summary>
    /// A watcher stops seeing anything when the folder it watches is deleted and made again, so it is
    /// thrown away here; the next pass makes a new one for the same path.
    /// </summary>
    private void Drop(string root)
    {
        lock (_gate)
        {
            if (_watchers.Remove(root, out var old)) old.Dispose();
            _fullScan = true;
        }
        Kick();
    }

    public event Action<Exception>? Failed;
    public int Processed { get; private set; }

    private void Queue(string fullPath)
    {
        var rel = _writes.Vault.Relative(fullPath);
        var first = rel.Split('/')[0];
        if (Ignored.Contains(first, StringComparer.OrdinalIgnoreCase)) return;
        if (fullPath.EndsWith(AtomicFile.TempSuffix, StringComparison.OrdinalIgnoreCase)) return;
        lock (_gate)
        {
            // A folder rename or delete doesn't report the files inside it; rescan.
            if (!Path.HasExtension(fullPath) || Directory.Exists(fullPath)) _fullScan = true;
            else _pending.Add(fullPath);
        }
        Kick();
    }

    // Sliding: every new event pushes the flush back by the debounce interval.
    private void Kick() { if (!_disposed) _timer.Change(Debounce, Timeout.InfiniteTimeSpan); }

    /// <summary>Processes queued changes now (also used by tests).</summary>
    public void Flush()
    {
        string[] paths;
        bool full;
        lock (_gate)
        {
            paths = [.. _pending];
            _pending.Clear();
            full = _fullScan;
            _fullScan = false;
        }
        if (paths.Length == 0 && !full) return;
        try
        {
            // A folder that was deleted and made again needs a new watcher; the old one sees nothing.
            Watch();
            if (full) paths = [.. ChangedSinceIndexed()];
            // Small chunks, each holding the write lock briefly: a big import (a copied folder of
            // thousands of notes) mustn't hold up the user's own saves until it finishes.
            var first = true;
            foreach (var chunk in paths.Chunk(ChunkSize))
            {
                // The lock isn't fair: pause between chunks so a waiting save gets its turn.
                if (!first) Thread.Sleep(15);
                first = false;
                _writes.Exclusive(() => { foreach (var p in chunk) Process(p); return 0; });
            }
        }
        catch (Exception ex) { Failed?.Invoke(ex); }
    }

    private const int ChunkSize = 10;

    /// <summary>
    /// Files that differ from the index, found by comparing timestamps and sizes (cheap): changed and
    /// new files first, then ones that disappeared, so a moved folder reads as a move, not a delete.
    /// </summary>
    private IEnumerable<string> ChangedSinceIndexed()
    {
        var vault = _writes.Vault;
        var cached = _writes.Cache.AllFiles().ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var abs in vault.EnumerateNodeFiles())
        {
            var rel = vault.Relative(abs);
            seen.Add(rel);
            var info = new FileInfo(abs);
            if (!cached.TryGetValue(rel, out var hit) || hit.MTime != info.LastWriteTimeUtc.Ticks || hit.Size != info.Length) yield return abs;
        }
        foreach (var gone in cached.Keys.Where(p => !seen.Contains(p))) yield return vault.Absolute(gone);
    }

    private void Process(string abs)
    {
        var vault = _writes.Vault;
        var rel = vault.Relative(abs);
        var name = Path.GetFileName(abs);

        if (Vault.ConfigFiles.Contains(name, StringComparer.OrdinalIgnoreCase) && !rel.Contains('/'))
        {
            _writes.Changes.Publish(new ChangeEvent(ChangeKind.Notification, null, rel, Origin: "external", Data: new { config = name }));
            return;
        }
        if (!abs.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || !IsNodeLocation(rel)) return;

        var cached = _writes.Cache.GetFile(rel);
        if (!File.Exists(abs))
        {
            if (cached is null) return;
            _writes.Cache.Remove(rel);
            if (Ulid.IsValid(cached.Id) && _writes.Cache.PathOf(cached.Id) is null)
                _writes.Changes.Publish(new ChangeEvent(ChangeKind.Deleted, cached.Id, null, rel, Origin: "external"));
            Processed++;
            return;
        }

        string content;
        try { content = AtomicFile.ReadAllText(abs); }
        catch (IOException) { lock (_gate) _pending.Add(abs); Kick(); return; } // still being written; retry
        var hash = Hashing.Sha256(content);
        if (cached is not null && cached.Hash == hash) return; // our own write, or no real change

        var node = Node.FromText(rel, content, hash);
        var owner = Ulid.IsValid(node.Id) ? _writes.Cache.PathOf(node.Id) : null;
        var duplicate = owner is not null && !string.Equals(owner, rel, StringComparison.OrdinalIgnoreCase) && File.Exists(vault.Absolute(owner));
        if (node.YamlError is null && (!Ulid.IsValid(node.Id) || duplicate))
        {
            _writes.Adopt(abs, duplicate ? "duplicate id" : "no id");
            Processed++;
            return;
        }
        if (Ulid.IsValid(node.Id)) _writes.History.Record(node.Id, Encoding.UTF8.GetBytes(content), HistoryStore.External);
        _writes.Index(abs, content);
        Processed++;
        var kind = cached is null ? (owner is not null ? ChangeKind.Moved : ChangeKind.Created) : ChangeKind.Updated;
        _writes.Changes.Publish(new ChangeEvent(kind, node.Id, rel, owner, hash, "external"));
    }

    private static bool IsNodeLocation(string rel) =>
        rel.StartsWith("workspaces/", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("templates/", StringComparison.OrdinalIgnoreCase)
        || rel.StartsWith("supertags/", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("synced_blocks/", StringComparison.OrdinalIgnoreCase)
        || rel.StartsWith(Vault.LinkedPrefix + "/", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _disposed = true;
        foreach (var w in _watchers.Values) w.Dispose();
        _watchers.Clear();
        _timer.Dispose();
        _sweep.Dispose();
    }
}
