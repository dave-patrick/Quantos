using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Quantos.Core;

/// <summary>
/// The single write path (BUILD-PLAN.md 6). Every change to a vault file goes through here.
///
/// Write steps, in order:
///   1. Identify the target and the version the caller expects.
///   2. (Permissions are checked by the caller's host before calling in.)
///   3. Re-read the file and compare versions; on mismatch nothing is written.
///   4. Save the current bytes to .history/&lt;id&gt;/ (recoverable history).
///   5. Write a temp file next to the target, flush to disk, then atomically replace.
///   6. Update the derived cache.
///   7. Raise <see cref="Changed"/> so connected clients can refresh.
///   8. Return only after all of the above finished.
///
/// One write at a time per vault (single user, low volume); reads never block.
/// </summary>
public sealed partial class WriteService
{
    internal const string TempSuffix = ".qtmp";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IClock _clock;
    private readonly Action<string> _log;

    public WriteService(VaultLayout layout, NodeCache cache, IClock? clock = null, Action<string>? log = null)
    {
        Layout = layout;
        Cache = cache;
        _clock = clock ?? SystemClock.Instance;
        _log = log ?? (_ => { });
    }

    public VaultLayout Layout { get; }
    public NodeCache Cache { get; }

    /// <summary>Raised after a successful change: (nodeId, kind).</summary>
    public event Action<string, string>? Changed;

    /// <summary>Test hook: runs after the temp file is written and before it replaces the target.</summary>
    internal Action<string>? BeforeCommit { get; set; }

    // ---------------------------------------------------------------- startup

    /// <summary>Creates missing vault folders, cleans up interrupted operations and makes sure the cache is usable.</summary>
    public void Start()
    {
        Layout.EnsureCreated();
        RecoverInterruptedWrites();
        // Stage 1 has no file watcher yet, so re-index on every start to pick up edits made outside the app.
        RebuildCache();
    }

    /// <summary>
    /// A crash between "write temp file" and "replace target" leaves a *.qtmp file and an intact original.
    /// The temp file is moved to .state/recovered/ (never silently deleted). A trash entry whose file
    /// never arrived (crash mid-trash) or already left (crash mid-restore) is removed; the node file is
    /// still in its live location.
    /// </summary>
    internal void RecoverInterruptedWrites()
    {
        var recovered = Path.Combine(Layout.State, "recovered");
        foreach (var tmp in Directory.EnumerateFiles(Layout.Root, "*" + TempSuffix, SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(recovered);
            var name = Path.GetFileName(tmp)[..^TempSuffix.Length].TrimStart('.');
            var dest = Path.Combine(recovered, $"{Timestamps.FileStamp(_clock.Now)}_{name}.recovered");
            File.Move(tmp, dest);
            _log($"Recovered an interrupted save: {Layout.Relative(tmp)} -> {Layout.Relative(dest)}");
        }

        if (!Directory.Exists(Layout.Trash)) return;
        foreach (var dir in Directory.GetDirectories(Layout.Trash))
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            var hasNodeFile = Directory.EnumerateFiles(dir, "*.md").Any();
            if (File.Exists(manifestPath) && !hasNodeFile)
            {
                Directory.Delete(dir, recursive: true);
                _log($"Removed an incomplete trash entry: {Layout.Relative(dir)}");
            }
        }
    }

    // ---------------------------------------------------------------- reads

    public NodeSnapshot Read(string id)
    {
        var cached = Locate(id, allowRebuild: false);
        var full = Layout.ResolveInside(cached.Path);
        var bytes = File.ReadAllBytes(full);
        var doc = NodeDocument.Parse(bytes);
        return Snapshot(cached.Path, doc, bytes, cached.State, cached.TrashId);
    }

    public IReadOnlyList<CachedNode> List(string workspace) => Cache.List(workspace);

    public IReadOnlyList<HistoryEntry> History(string id)
    {
        var dir = Path.Combine(Layout.History, SafeId(id));
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*.md")
            .Select(f => new HistoryEntry(Path.GetFileNameWithoutExtension(f), new FileInfo(f).Length))
            .OrderByDescending(e => e.Entry, StringComparer.Ordinal)
            .ToList();
    }

    public string ReadHistory(string id, string entry)
    {
        if (!HistoryEntryName().IsMatch(entry)) throw new ValidationException("Unknown history entry.");
        var path = Path.Combine(Layout.History, SafeId(id), entry + ".md");
        if (!File.Exists(path)) throw new NotFoundException("History entry");
        return File.ReadAllText(path);
    }

    // ---------------------------------------------------------------- writes

    public Task<NodeSnapshot> CreateAsync(string workspace, string title, string body, string folder = "nodes") =>
        Locked(() => Create(workspace, title, body, folder, fileStem: null));

    /// <summary>Returns today's (or any day's) daily note, creating it if it doesn't exist.</summary>
    public Task<NodeSnapshot> GetOrCreateDailyAsync(string workspace, DateOnly date) => Locked(() =>
    {
        var stem = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var rel = $"workspaces/{workspace}/daily/{stem}.md";
        var existing = Cache.GetByPath(rel);
        if (existing is not null) return Read(existing.Id);
        if (File.Exists(Layout.ResolveInside(rel)))
        {
            IndexFile(Layout.ResolveInside(rel));
            var indexed = Cache.GetByPath(rel) ?? throw new ValidationException($"{rel} exists but isn't a valid Quantos note. See diagnostics.");
            return Read(indexed.Id);
        }
        return Create(workspace, stem, "", "daily", stem);
    });

    public Task<NodeSnapshot> UpdateAsync(string id, string expectedVersion, string? title, string? body) => Locked(() =>
    {
        if (title is null && body is null) throw new ValidationException("Nothing to change.");
        if (title is not null) ValidateTitle(title);

        var cached = Locate(id);
        if (cached.State == "trashed") throw new ValidationException("Restore this note from the trash before editing it.");
        var full = Layout.ResolveInside(cached.Path);
        var (doc, bytes) = ReadChecked(id, full, expectedVersion);

        if (title is not null) doc.SetScalar("title", title);
        if (body is not null && body != doc.Body) doc.Body = body;
        doc.SetScalar("modified", Timestamps.Format(_clock.Now));

        SaveHistory(id, bytes);
        var newBytes = doc.ToBytes();
        AtomicWrite(full, newBytes, overwrite: true);
        return Committed(id, "updated", cached.Path, doc, newBytes, "active", null);
    });

    /// <summary>Moves a node to .trash/&lt;trashId&gt;/ with a restore manifest. Trash never expires.</summary>
    public Task<TrashEntry> TrashAsync(string id, string expectedVersion) => Locked(() =>
    {
        var cached = Locate(id);
        if (cached.State == "trashed") throw new ValidationException("This note is already in the trash.");
        var full = Layout.ResolveInside(cached.Path);
        var (doc, bytes) = ReadChecked(id, full, expectedVersion);
        var header = doc.ReadHeader();

        SaveHistory(id, bytes);
        var trashId = $"{Timestamps.FileStamp(_clock.Now)}_{SafeId(id)[..Math.Min(8, SafeId(id).Length)]}";
        var dir = Path.Combine(Layout.Trash, trashId);
        Directory.CreateDirectory(dir);
        var entry = new TrashEntry(trashId, id, header.Title, cached.Workspace, cached.Path, Timestamps.Format(_clock.Now));

        // Manifest first, then the file. Recovery removes a manifest whose file never arrived.
        AtomicWrite(Path.Combine(dir, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions), overwrite: false);
        File.Move(full, Path.Combine(dir, Path.GetFileName(full)));

        var rel = Layout.Relative(Path.Combine(dir, Path.GetFileName(full)));
        Committed(id, "trashed", rel, doc, bytes, "trashed", trashId);
        return entry;
    });

    public IReadOnlyList<TrashEntry> ListTrash()
    {
        if (!Directory.Exists(Layout.Trash)) return [];
        var list = new List<TrashEntry>();
        foreach (var dir in Directory.GetDirectories(Layout.Trash))
        {
            var manifest = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifest)) continue;
            var entry = JsonSerializer.Deserialize<TrashEntry>(File.ReadAllBytes(manifest), JsonOptions);
            if (entry is not null) list.Add(entry);
        }
        return list.OrderByDescending(e => e.TrashId, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Restores a trashed node to its original location. If that name is taken now, the node is
    /// restored next to it under a new name; nothing is overwritten. The node keeps its id.
    /// </summary>
    public Task<RestoreResult> RestoreAsync(string trashId) => Locked(() =>
    {
        if (!TrashIdPattern().IsMatch(trashId)) throw new ValidationException("Unknown trash entry.");
        var dir = Path.Combine(Layout.Trash, trashId);
        var manifestPath = Path.Combine(dir, "manifest.json");
        if (!File.Exists(manifestPath)) throw new NotFoundException("Trash entry");
        var entry = JsonSerializer.Deserialize<TrashEntry>(File.ReadAllBytes(manifestPath), JsonOptions)
                    ?? throw new ValidationException("The trash manifest is unreadable.");
        var source = Directory.GetFiles(dir, "*.md").SingleOrDefault() ?? throw new NotFoundException("Trashed file");

        var original = Layout.ResolveInside(entry.OriginalPath);
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        var target = original;
        for (var n = 2; File.Exists(target); n++)
            target = Path.Combine(Path.GetDirectoryName(original)!, $"{Path.GetFileNameWithoutExtension(original)}_restored_{n}.md");

        File.Move(source, target);
        Directory.Delete(dir, recursive: true);

        var bytes = File.ReadAllBytes(target);
        var doc = NodeDocument.Parse(bytes);
        var rel = Layout.Relative(target);
        var snapshot = Committed(entry.NodeId, "restored", rel, doc, bytes, "active", null);
        return new RestoreResult(snapshot, Renamed: target != original);
    });

    // ---------------------------------------------------------------- cache

    /// <summary>Deletes the cache and rebuilds it from the vault files. Never changes vault files.</summary>
    public Task<RebuildReport> RebuildCacheAsync() => Locked(RebuildCache);

    internal RebuildReport RebuildCache()
    {
        Cache.DeleteFile();
        Cache.EnsureSchema();
        var indexed = 0;
        if (Directory.Exists(Layout.Workspaces))
        {
            foreach (var file in Directory.EnumerateFiles(Layout.Workspaces, "*.md", SearchOption.AllDirectories))
                if (IndexFile(file)) indexed++;
        }
        foreach (var entry in ListTrash())
        {
            var dir = Path.Combine(Layout.Trash, entry.TrashId);
            var file = Directory.GetFiles(dir, "*.md").SingleOrDefault();
            if (file is not null && IndexFile(file, "trashed", entry.TrashId)) indexed++;
        }
        var problems = Cache.Problems().Count;
        _log($"Cache rebuilt: {indexed} nodes indexed, {problems} problems.");
        return new RebuildReport(indexed, problems);
    }

    public VaultDiagnostics Diagnostics() => new(
        Layout.Root,
        Cache.FilePath,
        Cache.CountsByState(),
        DirectorySize(Layout.History),
        DirectorySize(Layout.Trash),
        Cache.Problems().Select(p => new VaultProblem(p.Path, p.Problem)).ToList());

    // ---------------------------------------------------------------- internals

    private NodeSnapshot Create(string workspace, string title, string body, string folder, string? fileStem)
    {
        VaultLayout.ValidateWorkspaceName(workspace);
        ValidateTitle(title);
        if (!VaultLayout.WorkspaceFolders.Contains(folder)) throw new ValidationException("Unknown folder.");
        Layout.EnsureWorkspace(workspace);

        var now = Timestamps.Format(_clock.Now);
        var id = Guid.CreateVersion7().ToString();
        var doc = NodeDocument.CreateNew(new NodeHeader(id, title, workspace, VaultLayout.SchemaVersion, now, now, "active", []), body);
        var bytes = doc.ToBytes();

        var dir = Path.Combine(Layout.Workspace(workspace), folder);
        var stem = fileStem ?? Slug(title);
        for (var n = 1; ; n++)
        {
            var target = Path.Combine(dir, n == 1 ? $"{stem}.md" : $"{stem}_{n}.md");
            if (File.Exists(target)) continue;
            try
            {
                AtomicWrite(target, bytes, overwrite: false);
                return Committed(id, "created", Layout.Relative(target), doc, bytes, "active", null);
            }
            catch (IOException) when (File.Exists(target))
            {
                // Someone created that name between our check and our write; try the next one.
            }
        }
    }

    private (NodeDocument Doc, byte[] Bytes) ReadChecked(string id, string full, string expectedVersion)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion)) throw new ValidationException("expectedVersion is required.");
        var bytes = File.ReadAllBytes(full);
        var current = NodeDocument.VersionOf(bytes);
        if (!string.Equals(current, expectedVersion, StringComparison.OrdinalIgnoreCase))
            throw new VersionConflictException(id, expectedVersion, current);
        var doc = NodeDocument.Parse(bytes);
        var header = doc.ReadHeader();
        if (header.Id != id) throw new VersionConflictException(id, expectedVersion, current);
        return (doc, bytes);
    }

    /// <summary>Write to a temp file in the same folder, flush to disk, then move into place.</summary>
    private void AtomicWrite(string target, byte[] bytes, bool overwrite)
    {
        var dir = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}{TempSuffix}");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(bytes);
                fs.Flush(flushToDisk: true);
            }
            BeforeCommit?.Invoke(target);
            File.Move(tmp, target, overwrite);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    private void SaveHistory(string id, byte[] bytes)
    {
        var name = $"{Timestamps.FileStamp(_clock.Now)}_{NodeDocument.VersionOf(bytes)[..12]}.md";
        var path = Path.Combine(Layout.History, SafeId(id), name);
        if (!File.Exists(path)) AtomicWrite(path, bytes, overwrite: false);
    }

    private NodeSnapshot Committed(string id, string kind, string relPath, NodeDocument doc, byte[] bytes, string state, string? trashId)
    {
        var snapshot = Snapshot(relPath, doc, bytes, state, trashId);
        try
        {
            Cache.Upsert(new CachedNode(id, relPath, snapshot.Header.Workspace, snapshot.Header.Title, state, snapshot.Header.Modified, snapshot.Version, trashId));
        }
        catch (Exception ex)
        {
            // The vault file is already safely written; the cache is derived and can be rebuilt.
            _log($"Cache update failed after a successful save of {relPath}: {ex.Message}. Run a cache rebuild.");
        }
        Changed?.Invoke(id, kind);
        return snapshot;
    }

    private static NodeSnapshot Snapshot(string relPath, NodeDocument doc, byte[] bytes, string state, string? trashId) =>
        new(doc.ReadHeader(), relPath, NodeDocument.VersionOf(bytes), doc.Body, doc.FrontmatterText, state, trashId);

    /// <summary>Finds a node by id. If the cache is stale (file moved outside the app), rebuilds once and retries.</summary>
    private CachedNode Locate(string id, bool allowRebuild = true)
    {
        var cached = Cache.Get(id);
        if (cached is not null && File.Exists(Layout.ResolveInside(cached.Path))) return cached;
        if (!allowRebuild) throw new NotFoundException("Note");
        RebuildCache();
        return Cache.Get(id) ?? throw new NotFoundException("Note");
    }

    private bool IndexFile(string file, string state = "active", string? trashId = null)
    {
        var rel = Layout.Relative(file);
        try
        {
            var bytes = File.ReadAllBytes(file);
            var header = NodeDocument.Parse(bytes).ReadHeader();
            if (string.IsNullOrWhiteSpace(header.Id))
            {
                Cache.AddProblem(rel, "No 'id' in the frontmatter, so Quantos can't track this file yet.");
                return false;
            }
            var existing = Cache.Get(header.Id);
            if (existing is not null && existing.Path != rel)
            {
                Cache.AddProblem(rel, $"Duplicate id {header.Id}; it's also used by {existing.Path}.");
                return false;
            }
            var workspace = string.IsNullOrEmpty(header.Workspace) ? WorkspaceFromPath(rel) : header.Workspace;
            var nodeState = state == "trashed" ? "trashed" : header.State == "archived" ? "archived" : "active";
            Cache.Upsert(new CachedNode(header.Id, rel, workspace, header.Title, nodeState, header.Modified, NodeDocument.VersionOf(bytes), trashId));
            return true;
        }
        catch (QuantosException ex)
        {
            Cache.AddProblem(rel, ex.Message);
            return false;
        }
    }

    private async Task<T> Locked<T>(Func<T> action)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return action(); }
        finally { _gate.Release(); }
    }

    private static string WorkspaceFromPath(string rel)
    {
        var parts = rel.Split('/');
        return parts.Length > 2 && parts[0] == "workspaces" ? parts[1] : VaultLayout.DefaultWorkspace;
    }

    /// <summary>Lowercase with underscores (BUILD-PLAN.md 4, naming).</summary>
    public static string Slug(string title)
    {
        var sb = new StringBuilder();
        foreach (var ch in title.Normalize(NormalizationForm.FormKD).ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }
        var slug = sb.ToString().Trim('_');
        if (slug.Length > 80) slug = slug[..80].TrimEnd('_');
        return slug.Length == 0 ? "untitled" : slug;
    }

    private static void ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ValidationException("A title is required.");
        if (title.Length > 300 || title.Contains('\n') || title.Contains('\r')) throw new ValidationException("Titles are one line, up to 300 characters.");
    }

    private static string SafeId(string id)
    {
        if (!Guid.TryParse(id, out var g)) throw new NotFoundException("Note");
        return g.ToString();
    }

    private static long DirectorySize(string path) =>
        Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [GeneratedRegex(@"^\d{8}T\d{9}Z_[0-9a-f]{12}$")] private static partial Regex HistoryEntryName();
    [GeneratedRegex(@"^\d{8}T\d{9}Z_[0-9a-f-]{1,8}$")] private static partial Regex TrashIdPattern();
}

public sealed record NodeSnapshot(NodeHeader Header, string Path, string Version, string Body, string Frontmatter, string State, string? TrashId);
public sealed record TrashEntry(string TrashId, string NodeId, string Title, string Workspace, string OriginalPath, string TrashedAt);
public sealed record RestoreResult(NodeSnapshot Node, bool Renamed);
public sealed record HistoryEntry(string Entry, long Bytes);
public sealed record RebuildReport(int Indexed, int Problems);
public sealed record VaultProblem(string Path, string Problem);
public sealed record VaultDiagnostics(string VaultPath, string CachePath, Dictionary<string, long> Nodes, long HistoryBytes, long TrashBytes, IReadOnlyList<VaultProblem> Problems);
