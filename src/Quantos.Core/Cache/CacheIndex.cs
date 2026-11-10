using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Quantos.Core.Markdown;

namespace Quantos.Core.Cache;

public sealed record CachedFile(string Path, string Id, string Hash, long MTime, long Size);

public sealed record NodeSummary(
    string Id, string Path, string Title, string Type, string Workspace, IReadOnlyList<string> Tags,
    string? Created, string? Modified, bool Archived, bool ReadOnly, Dictionary<string, object?> Properties, IReadOnlyList<string> Aliases);

/// <summary>Conditions for <see cref="CacheIndex.Find"/>. Null means "any".</summary>
public sealed record NodeFilter
{
    public IReadOnlyCollection<string>? Ids { get; init; }
    public IReadOnlyCollection<string>? Workspaces { get; init; }
    /// <summary>With <see cref="Workspaces"/>: also allow templates and supertags kept outside any workspace.</summary>
    public bool SchemaRecordsInAnyWorkspace { get; init; }
    public IReadOnlyCollection<string>? Types { get; init; }
    public IReadOnlyCollection<string>? ExcludeTypes { get; init; }
    public bool IncludeArchived { get; init; }
    /// <summary>Path prefix, ending in '/'.</summary>
    public string? FolderPrefix { get; init; }
    /// <summary>Each set must match at least one of the node's tags (a set containing "task" also matches tasks).</summary>
    public IReadOnlyList<IReadOnlyCollection<string>>? TagSets { get; init; }
    /// <summary>Direct children of this task id.</summary>
    public string? Parent { get; init; }
    public int Limit { get; init; } = -1;
    public int Offset { get; init; }
}

public sealed record SearchHit(string Id, string Title, string Type, string Workspace, string Path, string Snippet);

public sealed record LinkRef(string SourceId, string SourceTitle, string SourceWorkspace, string Target, string Kind, string Context);

/// <summary>
/// Derived data in client-data/cache/cache.sqlite. Nothing here is authoritative; it can be deleted
/// and rebuilt from the vault at any time (BUILD-PLAN section 4).
/// <para>
/// One writer connection (behind <c>_gate</c>) and a small pool of read-only connections. SQLite's
/// WAL mode lets readers see the last committed state while a write or a long rebuild runs, so a
/// reindex no longer makes every list and search wait. The thread that is writing (holding the gate,
/// or owning an open batch) reads through the writer connection so it always sees its own changes.
/// </para>
/// </summary>
public sealed partial class CacheIndex : IDisposable
{
    public const int Version = 13; // node_tags and nodes.parent: tag, folder and subtask filters run in SQL
    private readonly SqliteConnection _db;
    private readonly Lock _gate = new();
    private long _generation;

    /// <summary>Goes up on every change to the cache, so callers can keep derived data until it moves.</summary>
    public long Generation => Interlocked.Read(ref _generation);

    private void Changed() => Interlocked.Increment(ref _generation);
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string DatabasePath { get; }

    public CacheIndex(string databasePath)
    {
        DatabasePath = databasePath;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(databasePath)!);
        _db = OpenOrReset(databasePath);
    }

    /// <summary>True when the cache was created empty (new, deleted, or version mismatch) and needs a full rebuild.</summary>
    public bool WasReset { get; private set; }

    private SqliteConnection OpenOrReset(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            try
            {
                db.Open();
                Exec(db, "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=OFF;");
                Exec(db, "CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT)");
                var version = Scalar(db, "SELECT value FROM meta WHERE key='cache_version'");
                if (version != Version.ToString())
                {
                    CreateSchema(db);
                    WasReset = true;
                }
                Exec(db, "PRAGMA quick_check");
                return db;
            }
            catch (SqliteException) when (attempt == 0)
            {
                // Any read error: the cache is disposable, so delete and rebuild.
                db.Dispose();
                SqliteConnection.ClearAllPools();
                DeleteFiles(path);
            }
        }
    }

    private static void DeleteFiles(string path)
    {
        foreach (var f in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(f)) File.Delete(f);
    }

    private static void CreateSchema(SqliteConnection db, bool ownTransaction = true)
    {
        // One transaction, so a reader on another connection sees the old tables or the new ones, never none.
        if (ownTransaction) Exec(db, "BEGIN");
        Exec(db, """
            DROP TABLE IF EXISTS files; DROP TABLE IF EXISTS nodes; DROP TABLE IF EXISTS aliases;
            DROP TABLE IF EXISTS links; DROP TABLE IF EXISTS search; DROP TABLE IF EXISTS inline_tasks;
            DROP TABLE IF EXISTS attachment_refs; DROP TABLE IF EXISTS search_map; DROP TABLE IF EXISTS node_tags;
            CREATE TABLE files(path TEXT PRIMARY KEY COLLATE NOCASE, id TEXT, hash TEXT, mtime INTEGER, size INTEGER);
            CREATE INDEX files_id ON files(id);
            CREATE TABLE nodes(id TEXT PRIMARY KEY, path TEXT COLLATE NOCASE, title TEXT, type TEXT, workspace TEXT,
                tags TEXT, created TEXT, modified TEXT, archived INTEGER, readonly INTEGER, props TEXT, parent TEXT);
            CREATE INDEX nodes_title ON nodes(title COLLATE NOCASE);
            CREATE INDEX nodes_modified ON nodes(modified DESC, id);
            CREATE INDEX nodes_parent ON nodes(parent);
            CREATE TABLE node_tags(node_id TEXT, tag TEXT); -- tag lower-cased (invariant), one row per tag
            CREATE INDEX node_tags_tag ON node_tags(tag, node_id);
            CREATE INDEX node_tags_node ON node_tags(node_id);
            CREATE INDEX nodes_type ON nodes(type, workspace);
            CREATE TABLE aliases(alias TEXT COLLATE NOCASE, id TEXT);
            CREATE INDEX aliases_alias ON aliases(alias);
            CREATE INDEX aliases_id ON aliases(id);
            CREATE TABLE links(src TEXT, target TEXT, kind TEXT, fragment TEXT, context TEXT);
            CREATE INDEX links_target ON links(target);
            CREATE INDEX links_src ON links(src);
            CREATE TABLE inline_tasks(note_id TEXT, task_id TEXT, checked INTEGER, text TEXT);
            CREATE INDEX inline_tasks_task ON inline_tasks(task_id);
            CREATE INDEX inline_tasks_note ON inline_tasks(note_id);
            CREATE TABLE attachment_refs(node_id TEXT, path TEXT COLLATE NOCASE);
            CREATE INDEX attachment_refs_path ON attachment_refs(path);
            CREATE INDEX attachment_refs_node ON attachment_refs(node_id);
            CREATE VIRTUAL TABLE search USING fts5(id UNINDEXED, title, body, tokenize='unicode61 remove_diacritics 2');
            -- FTS5 can't index the UNINDEXED id column, so deleting by id scanned every row on every save
            -- (quadratic on a first open). This maps each node to its full-text rowid instead.
            CREATE TABLE search_map(id TEXT PRIMARY KEY, rid INTEGER);
            """);
        Exec(db, $"INSERT OR REPLACE INTO meta(key, value) VALUES('cache_version', '{Version}')");
        if (ownTransaction) Exec(db, "COMMIT");
    }

    /// <summary>The cache belongs to one vault; opening it for another vault resets it.</summary>
    public void BindToVault(string vaultRoot)
    {
        lock (_gate)
        {
            var bound = Scalar(_db, "SELECT value FROM meta WHERE key='vault_root'");
            if (bound is not null && string.Equals(bound, vaultRoot, StringComparison.OrdinalIgnoreCase)) return;
            if (bound is not null) { DropStatements(); CreateSchema(_db, _batch is null); WasReset = true; Changed(); }
            Exec(_db, "INSERT OR REPLACE INTO meta(key, value) VALUES('vault_root', $v)", ("$v", vaultRoot));
            if (bound is null && Scalar(_db, "SELECT COUNT(*) FROM files") != "0") WasReset = true;
        }
    }

    public void Clear()
    {
        lock (_gate) { DropStatements(); CreateSchema(_db, _batch is null); WasReset = true; Changed(); }
    }

    // ---- writes -----------------------------------------------------------------------------

    private SqliteTransaction? _batch;
    /// <summary>True while a start-up scan or rebuild is writing in one batch.</summary>
    public bool InBatch => _batch is not null;
    private int _batchOwner; // managed thread id of the thread that opened the batch
    private int _batchDepth;

    /// <summary>Groups many index writes into one transaction (start-up scans and rebuilds).</summary>
    public IDisposable Batch()
    {
        lock (_gate)
        {
            if (_batch is null)
            {
                _batch = _db.BeginTransaction();
                _batchOwner = Environment.CurrentManagedThreadId;
                _batchDepth = 1;
            }
            else if (_batchOwner == Environment.CurrentManagedThreadId)
            {
                _batchDepth++;
            }
        }
        return new BatchScope(this);
    }

    private sealed class BatchScope(CacheIndex owner) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._gate)
            {
                if (owner._batch is not null && owner._batchOwner == Environment.CurrentManagedThreadId)
                {
                    owner._batchDepth--;
                    if (owner._batchDepth <= 0)
                    {
                        owner._batch.Commit();
                        owner._batch.Dispose();
                        owner._batch = null;
                        owner._batchOwner = 0;
                        owner._batchDepth = 0;
                        owner.Changed();
                    }
                }
            }
        }
    }

    public void Upsert(Node node, long mtime, long size)
    {
        lock (_gate)
        {
            Changed();
            using var tx = _batch is null ? _db.BeginTransaction() : null;
            _local = tx;
            try
            {
                RemoveInternal(node.Path, node.Id);
                Run("INSERT INTO files(path, id, hash, mtime, size) VALUES($p, $id, $h, $m, $s)",
                    ("$p", node.Path), ("$id", node.Id), ("$h", node.Hash), ("$m", mtime), ("$s", size));
                if (!string.IsNullOrEmpty(node.Id))
                {
                    Run("INSERT OR REPLACE INTO nodes VALUES($id, $p, $t, $type, $ws, $tags, $c, $mod, $a, $ro, $props, $parent)",
                        ("$id", node.Id), ("$p", node.Path), ("$t", node.Title), ("$type", node.Type), ("$ws", node.Workspace),
                        ("$tags", JsonSerializer.Serialize(node.Tags, Json)), ("$c", node.Created), ("$mod", node.Modified),
                        ("$a", node.Archived ? 1 : 0), ("$ro", node.ReadOnly ? 1 : 0), ("$props", JsonSerializer.Serialize(node.Properties, Json)),
                        ("$parent", Tasks.Props.LinkId(Tasks.Props.Str(node.Properties, Tasks.TaskKeys.Parent))));
                    foreach (var tag in node.Tags.Select(t => t.ToLowerInvariant()).Distinct())
                        Run("INSERT INTO node_tags VALUES($id, $tag)", ("$id", node.Id), ("$tag", tag));
                    foreach (var alias in node.Aliases)
                        Run("INSERT INTO aliases VALUES($a, $id)", ("$a", alias), ("$id", node.Id));
                    foreach (var link in Links.Find(node.Body))
                        Run("INSERT INTO links VALUES($s, $t, $k, $f, $c)", ("$s", node.Id),
                            ("$t", link.IsResolved ? link.Target : "?" + link.Target), ("$k", link.Kind == LinkKind.Embed ? "embed" : "link"),
                            ("$f", link.Fragment), ("$c", ContextLine(node.Body, link.Start)));
                    // Links held in properties (an owner, attendees) count as links too: backlinks, graph, renames.
                    foreach (var (key, value) in node.Properties)
                        foreach (var text in PropertyStrings(value))
                            foreach (var link in Links.Find(text).Where(l => l.IsResolved))
                                Run("INSERT INTO links VALUES($s, $t, 'link', $f, $c)", ("$s", node.Id), ("$t", link.Target), ("$f", link.Fragment), ("$c", $"{key}: {text}"));
                    foreach (var region in Links.SyncRegions(node.Body))
                        Run("INSERT INTO links VALUES($s, $t, $k, NULL, $c)", ("$s", node.Id), ("$t", region.Id), ("$k", "sync"), ("$c", ContextLine(region.Content, 0)));
                    foreach (var task in Links.InlineTasks(node.Body))
                        Run("INSERT INTO inline_tasks VALUES($n, $t, $c, $x)", ("$n", node.Id), ("$t", task.Id), ("$c", task.Checked ? 1 : 0), ("$x", task.Text));
                    foreach (var attachment in Attachments.Find(node.Body))
                        Run("INSERT INTO attachment_refs VALUES($n, $p)", ("$n", node.Id), ("$p", attachment));
                    Run("INSERT INTO search(id, title, body) VALUES($id, $t, $b)", ("$id", node.Id), ("$t", node.Title), ("$b", Links.PlainText(node.Body)));
                    Run("INSERT OR REPLACE INTO search_map(id, rid) VALUES($id, last_insert_rowid())", ("$id", node.Id));
                }
                tx?.Commit();
            }
            finally
            {
                _local = null;
            }
        }
    }

    public void Remove(string path)
    {
        lock (_gate)
        {
            Changed();
            using var tx = _batch is null ? _db.BeginTransaction() : null;
            _local = tx;
            try
            {
                RemoveInternal(path, null);
                tx?.Commit();
            }
            finally
            {
                _local = null;
            }
        }
    }

    private void RemoveInternal(string path, string? id)
    {
        var existingId = RunScalar("SELECT id FROM files WHERE path=$p", ("$p", path));
        foreach (var target in new[] { existingId, id }.Where(x => !string.IsNullOrEmpty(x)).Distinct())
        {
            // Only remove node rows owned by this path (a duplicate id elsewhere keeps its row).
            var owner = RunScalar("SELECT path FROM nodes WHERE id=$id", ("$id", target));
            if (owner is not null && !string.Equals(owner, path, StringComparison.OrdinalIgnoreCase) && target != id) continue;
            Run("DELETE FROM search WHERE rowid IN (SELECT rid FROM search_map WHERE id=$id)", ("$id", target));
            foreach (var table in new[] { "aliases WHERE id", "links WHERE src", "inline_tasks WHERE note_id", "attachment_refs WHERE node_id", "search_map WHERE id", "node_tags WHERE node_id", "nodes WHERE id" })
                Run($"DELETE FROM {table}=$id", ("$id", target));
        }
        Run("DELETE FROM files WHERE path=$p", ("$p", path));
    }

    private static string ContextLine(string body, int index)
    {
        var start = body.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        var end = body.IndexOf('\n', index);
        var line = (end < 0 ? body[start..] : body[start..end]).Trim();
        return line.Length > 240 ? line[..240] + "…" : line;
    }

    // ---- reads ------------------------------------------------------------------------------

    private readonly ConcurrentBag<SqliteConnection> _readers = new();
    private volatile bool _disposed;
    private const int KeptReaders = 4;

    /// <summary>True when this thread must read through the writer: it holds the gate or owns the open batch.</summary>
    private bool OnWriter => _gate.IsHeldByCurrentThread || (_batch is not null && _batchOwner == Environment.CurrentManagedThreadId);

    /// <summary>
    /// Runs a read. Other threads get a pooled read-only connection and never wait for writes; the
    /// writing thread uses the writer connection so it sees what it just wrote.
    /// </summary>
    private T Read<T>(Func<SqliteConnection, T> read)
    {
        if (OnWriter) lock (_gate) return read(_db);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_readers.TryTake(out var conn))
        {
            conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
            conn.Open();
            Exec(conn, "PRAGMA query_only=1;");
        }
        try { return read(conn); }
        finally
        {
            if (!_disposed && _readers.Count < KeptReaders) _readers.Add(conn);
            else conn.Dispose();
        }
    }

    public CachedFile? GetFile(string path) => Read(db =>
    {
        using var cmd = Command(db, "SELECT path, id, hash, mtime, size FROM files WHERE path=$p", ("$p", path));
        using var r = cmd.ExecuteReader();
        return r.Read() ? new CachedFile(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4)) : null;
    });

    public IReadOnlyList<CachedFile> AllFiles() => Read(db =>
    {
        using var cmd = Command(db, "SELECT path, id, hash, mtime, size FROM files");
        using var r = cmd.ExecuteReader();
        var list = new List<CachedFile>();
        while (r.Read()) list.Add(new CachedFile(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4)));
        return list;
    });

    public string? PathOf(string id) => Read(db => Scalar(db, "SELECT path FROM nodes WHERE id=$id", ("$id", id)));

    public NodeSummary? Get(string id) => Read(db => QueryNodes(db, "WHERE id=$id", ("$id", id)).FirstOrDefault());

    /// <summary>Nodes newest first. <paramref name="limit"/> −1 (the default) means every match: a silent
    /// cap made large vaults lose notes from lists and link lookups.</summary>
    public IReadOnlyList<NodeSummary> List(string? workspace = null, string? type = null, bool includeArchived = false, int limit = -1, int offset = 0) =>
        Find(new NodeFilter
        {
            Workspaces = workspace is null ? null : [workspace],
            Types = type is null ? null : [type],
            IncludeArchived = includeArchived,
            Limit = limit,
            Offset = offset
        });

    /// <summary>
    /// Nodes matching every given condition, newest first. Each condition narrows in SQL using the
    /// indexes; anything SQL can't decide exactly (non-ASCII case folding) is left for the caller to
    /// check, so the result is always a superset of what the caller's own test accepts.
    /// </summary>
    public IReadOnlyList<NodeSummary> Find(NodeFilter f) => Read(db =>
    {
        var where = new List<string>();
        var args = new List<(string, object?)>();
        string Json(IEnumerable<string> values) => JsonSerializer.Serialize(values, CacheIndex.Json);
        if (f.Ids is not null) { where.Add("n.id IN (SELECT value FROM json_each($ids))"); args.Add(("$ids", Json(f.Ids))); }
        if (f.Workspaces is not null)
        {
            var clause = "n.workspace IN (SELECT value FROM json_each($ws))";
            if (f.SchemaRecordsInAnyWorkspace) clause = $"({clause} OR (n.workspace = '' AND n.type IN ('template', 'supertag')))";
            where.Add(clause);
            args.Add(("$ws", Json(f.Workspaces)));
        }
        if (f.Types is not null)
        {
            // Types compare case-insensitively; they're ASCII names, which NOCASE folds exactly.
            where.Add("n.type COLLATE NOCASE IN (SELECT value FROM json_each($types))");
            args.Add(("$types", Json(f.Types)));
        }
        if (f.ExcludeTypes is not null)
        {
            where.Add("n.type NOT IN (SELECT value FROM json_each($xtypes))");
            args.Add(("$xtypes", Json(f.ExcludeTypes)));
        }
        if (!f.IncludeArchived) where.Add("n.archived=0");
        if (f.Parent is not null) { where.Add("n.parent=$parent"); args.Add(("$parent", f.Parent)); }
        if (f.FolderPrefix is { Length: > 0 } folder && IsAscii(folder))
        {
            where.Add("substr(n.path, 1, $flen) = $folder COLLATE NOCASE");
            args.Add(("$folder", folder)); args.Add(("$flen", folder.Length));
        }
        var tagIndex = 0;
        foreach (var set in f.TagSets ?? [])
        {
            if (set.Any(t => !IsAscii(t))) continue; // the caller's own check decides these
            var name = "$tags" + tagIndex++;
            var clause = $"EXISTS (SELECT 1 FROM node_tags t WHERE t.node_id = n.id AND t.tag IN (SELECT value FROM json_each({name})))";
            if (set.Contains("task", StringComparer.OrdinalIgnoreCase)) clause = $"({clause} OR n.type = 'task')";
            where.Add(clause);
            args.Add((name, Json(set.Select(t => t.ToLowerInvariant()))));
        }
        var sql = (where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")
            // The id breaks ties so paging through the vault can neither skip nor repeat a note.
            + $" ORDER BY n.modified DESC, n.id ASC LIMIT {(f.Limit < 0 ? -1 : f.Limit)} OFFSET {Math.Max(0, f.Offset)}";
        return QueryNodes(db, sql, [.. args]);
    });

    private static bool IsAscii(string s) => s.All(char.IsAscii);

    /// <summary>Every task under <paramref name="parentId"/> at any depth, parents before children (loops are cut).</summary>
    public IReadOnlyList<string> Descendants(string parentId) => Read(db =>
    {
        using var cmd = Command(db, """
            WITH RECURSIVE d(id, depth) AS (
                SELECT id, 1 FROM nodes WHERE parent = $p AND type = 'task'
                UNION
                SELECT n.id, d.depth + 1 FROM nodes n JOIN d ON n.parent = d.id WHERE n.type = 'task' AND d.depth < 1000)
            SELECT id FROM d GROUP BY id ORDER BY MIN(depth), id
            """, ("$p", parentId));
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) { var id = r.GetString(0); if (!string.Equals(id, parentId, StringComparison.OrdinalIgnoreCase)) list.Add(id); }
        return list;
    });

    /// <summary>Ids whose title or alias matches, case-insensitively. Workspace null means all.</summary>
    public IReadOnlyList<NodeSummary> FindByTitle(string title, string? workspace) => Read(db =>
    {
        var wsClause = workspace is null ? "" : " AND workspace=$ws";
        return QueryNodes(db, $"WHERE (title=$t COLLATE NOCASE OR id IN (SELECT id FROM aliases WHERE alias=$t)){wsClause} ORDER BY archived, title",
            ("$t", title), ("$ws", workspace));
    });

    /// <summary>Raw SQL over nodes for structured queries; <paramref name="where"/> is built only by trusted code.</summary>
    public IReadOnlyList<NodeSummary> Query(string where, params (string, object?)[] args) => Read(db => QueryNodes(db, where, args));

    public IReadOnlyList<SearchHit> Search(string query, string? workspace = null, int limit = 50)
    {
        var fts = ToFtsQuery(query);
        if (fts.Length == 0) return [];
        var hits = RunSearch(fts, workspace, limit);
        // Nothing found: try again with each word's ending trimmed, so a typo ("lauch") still finds "launch".
        if (hits.Count == 0 && ToFtsQuery(query, loose: true) is { Length: > 0 } loose && loose != fts) hits = RunSearch(loose, workspace, limit);
        return hits;
    }

    /// <summary>
    /// Every node id matching a full-text query, with no cap (for query filters, where a cap would
    /// silently drop matches and give a wrong total). Same typo fallback as <see cref="Search"/>.
    /// </summary>
    public HashSet<string> SearchIds(string query)
    {
        var fts = ToFtsQuery(query);
        if (fts.Length == 0) return [];
        var ids = RunSearchIds(fts);
        if (ids.Count == 0 && ToFtsQuery(query, loose: true) is { Length: > 0 } loose && loose != fts) ids = RunSearchIds(loose);
        return ids;
    }

    private HashSet<string> RunSearchIds(string fts) => Read(db =>
    {
        using var cmd = Command(db, "SELECT id FROM search WHERE search MATCH $q", ("$q", fts));
        using var r = cmd.ExecuteReader();
        var ids = new HashSet<string>();
        while (r.Read()) ids.Add(r.GetString(0));
        return ids;
    });

    private List<SearchHit> RunSearch(string fts, string? workspace, int limit) => Read(db =>
    {
        var sql = """
            SELECT n.id, n.title, n.type, n.workspace, n.path, snippet(search, 2, '[[[', ']]]', '…', 12)
            FROM search JOIN nodes n ON n.id = search.id
            WHERE search MATCH $q
            """ + (workspace is null ? "" : " AND n.workspace=$ws") + " ORDER BY bm25(search, 0, 8.0, 1.0) LIMIT " + limit;
        using var cmd = Command(db, sql, ("$q", fts), ("$ws", workspace));
        using var r = cmd.ExecuteReader();
        var hits = new List<SearchHit>();
        while (r.Read()) hits.Add(new SearchHit(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)));
        return hits;
    });

    /// <summary>
    /// Search words to an FTS5 query. Every word matches as a prefix; "quoted words" match as a phrase;
    /// OR between words means either; a leading minus excludes a word. Nothing typed can break the syntax.
    /// </summary>
    public static string ToFtsQuery(string query, bool loose = false)
    {
        static string Clean(string t) => new string(t.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '\'' or ' ').ToArray()).Trim();
        string Trim(string t) => loose && t.Length >= 5 ? t[..Math.Max(3, t.Length - 2)] : t;
        var parts = new List<string>();
        var excluded = new List<string>();
        var pendingOr = false;
        foreach (Match m in SearchToken().Matches(query))
        {
            if (m.Groups["phrase"].Success)
            {
                var phrase = Clean(m.Groups["phrase"].Value);
                if (phrase.Length > 0) { parts.Add((pendingOr && parts.Count > 0 ? "OR " : "") + "\"" + phrase + "\""); pendingOr = false; }
                continue;
            }
            var word = m.Groups["word"].Value;
            if (word == "OR") { pendingOr = true; continue; }
            var negative = word.Length > 1 && word[0] == '-';
            var term = Clean(negative ? word[1..] : word);
            if (term.Length == 0) continue;
            if (negative) { excluded.Add("\"" + term + "\""); continue; }
            parts.Add((pendingOr && parts.Count > 0 ? "OR " : "") + "\"" + Trim(term) + "\"*");
            pendingOr = false;
        }
        if (parts.Count == 0) return "";
        var include = string.Join(" ", parts);
        return excluded.Count == 0 ? include : $"({include}) NOT ({string.Join(" OR ", excluded)})";
    }

    [GeneratedRegex("\"(?<phrase>[^\"]*)\"|(?<word>\\S+)")]
    private static partial Regex SearchToken();

    public IReadOnlyList<LinkRef> Backlinks(string id) => Read(db =>
    {
        using var cmd = Command(db, """
            SELECT l.src, n.title, n.workspace, l.target, l.kind, l.context FROM links l JOIN nodes n ON n.id = l.src
            WHERE l.target = $id ORDER BY n.modified DESC
            """, ("$id", id));
        return ReadLinks(cmd);
    });

    public IReadOnlyList<LinkRef> OutLinks(string id) => Read(db =>
    {
        using var cmd = Command(db, """
            SELECT l.src, COALESCE(n.title, substr(l.target, 2)), COALESCE(n.workspace, ''), l.target, l.kind, l.context
            FROM links l LEFT JOIN nodes n ON n.id = l.target WHERE l.src = $id
            """, ("$id", id));
        return ReadLinks(cmd);
    });

    /// <summary>All resolved links across the vault in one query (used by global knowledge graph).</summary>
    public IReadOnlyList<(string Src, string Target, string Kind)> AllResolvedLinks() => Read(db =>
    {
        using var cmd = Command(db, "SELECT src, target, kind FROM links WHERE target NOT LIKE '?%' AND src != target");
        using var r = cmd.ExecuteReader();
        var list = new List<(string, string, string)>();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
        return list;
    });

    /// <summary>Ids of nodes containing a link or embed to <paramref name="id"/>.</summary>
    public IReadOnlyList<string> Referrers(string id) => Read(db =>
    {
        using var cmd = Command(db, "SELECT DISTINCT src FROM links WHERE target=$id UNION SELECT DISTINCT note_id FROM inline_tasks WHERE task_id=$id", ("$id", id));
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    });

    /// <summary>Notes holding a typed, unresolved [[Title]] link (optionally [[ws:Title]]).</summary>
    public IReadOnlyList<string> UnresolvedReferrers(string title) => Read(db =>
    {
        using var cmd = Command(db, "SELECT DISTINCT src FROM links WHERE target = $t COLLATE NOCASE OR target LIKE $w COLLATE NOCASE", ("$t", "?" + title), ("$w", "?%:" + title));
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    });

    public IReadOnlyList<(string NoteId, bool Checked, string Text)> InlineTaskRefs(string taskId) => Read(db =>
    {
        using var cmd = Command(db, "SELECT note_id, checked, text FROM inline_tasks WHERE task_id=$id", ("$id", taskId));
        using var r = cmd.ExecuteReader();
        var list = new List<(string, bool, string)>();
        while (r.Read()) list.Add((r.GetString(0), r.GetInt64(1) == 1, r.GetString(2)));
        return list;
    });

    public IReadOnlyList<(string Path, int Count)> AttachmentRefCounts() => Read(db =>
    {
        using var cmd = Command(db, "SELECT path, COUNT(*) FROM attachment_refs GROUP BY path");
        using var r = cmd.ExecuteReader();
        var list = new List<(string, int)>();
        while (r.Read()) list.Add((r.GetString(0), r.GetInt32(1)));
        return list;
    });

    /// <summary>The start of every note's plain text, for lists that show a line of each note (the notes browser).</summary>
    public IReadOnlyDictionary<string, string> Excerpts(int length = 200) => Read(db =>
    {
        // The body lives in the full-text table; the nodes table has no body column.
        using var cmd = Command(db, "SELECT id, substr(body, 1, $n) FROM search", ("$n", length));
        using var r = cmd.ExecuteReader();
        var map = new Dictionary<string, string>();
        while (r.Read()) if (!r.IsDBNull(1)) map[r.GetString(0)] = System.Text.RegularExpressions.Regex.Replace(r.GetString(1), @"\s+", " ").Trim();
        return map;
    });

    /// <summary>How connected each node is: resolved links out plus links in, in one query (for the graph overview).</summary>
    public IReadOnlyDictionary<string, int> LinkDegrees() => Read(db =>
    {
        using var cmd = Command(db, "SELECT id, COUNT(*) FROM (SELECT src AS id FROM links WHERE target NOT LIKE '?%' UNION ALL SELECT target FROM links) GROUP BY id");
        using var r = cmd.ExecuteReader();
        var map = new Dictionary<string, int>();
        while (r.Read()) map[r.GetString(0)] = r.GetInt32(1);
        return map;
    });

    /// <summary>The text values of a property: a string, or the strings in a list.</summary>
    public static IEnumerable<string> PropertyStrings(object? value) => value switch
    {
        string s => [s],
        System.Collections.IEnumerable list and not IDictionary<string, object?> and not IDictionary<object, object> => list.OfType<string>(),
        _ => []
    };

    /// <summary>The first image each node references, in body order: the default cover on cards.</summary>
    public IReadOnlyDictionary<string, string> FirstImages() => Read(db =>
    {
        using var cmd = Command(db, "SELECT node_id, path FROM attachment_refs WHERE lower(path) GLOB '*.png' OR lower(path) GLOB '*.jpg' OR lower(path) GLOB '*.jpeg' " +
            "OR lower(path) GLOB '*.gif' OR lower(path) GLOB '*.webp' OR lower(path) GLOB '*.svg' ORDER BY rowid");
        using var r = cmd.ExecuteReader();
        var map = new Dictionary<string, string>();
        while (r.Read()) map.TryAdd(r.GetString(0), r.GetString(1));
        return map;
    });

    /// <summary>First images for a specific set of nodes, avoiding a full table scan.</summary>
    public IReadOnlyDictionary<string, string> FirstImages(IEnumerable<string> nodeIds)
    {
        var ids = nodeIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, string>();
        return Read(db =>
        {
            // Build an IN clause with parameters
            var paramNames = ids.Select((_, i) => $"$id{i}").ToList();
            var sql = "SELECT node_id, path FROM attachment_refs WHERE (" +
                "lower(path) GLOB '*.png' OR lower(path) GLOB '*.jpg' OR lower(path) GLOB '*.jpeg' " +
                "OR lower(path) GLOB '*.gif' OR lower(path) GLOB '*.webp' OR lower(path) GLOB '*.svg') " +
                $"AND node_id IN ({string.Join(", ", paramNames)}) ORDER BY rowid";
            var args = ids.Select((id, i) => ($"$id{i}", (object?)id)).ToArray();
            using var cmd = Command(db, sql, args);
            using var r = cmd.ExecuteReader();
            var map = new Dictionary<string, string>();
            while (r.Read()) map.TryAdd(r.GetString(0), r.GetString(1));
            return map;
        });
    }

    public IReadOnlyList<(string Key, int Count)> PropertyCounts(string? workspace = null) => Read(db =>
    {
        using var cmd = Command(db, "SELECT j.key, COUNT(*) FROM nodes, json_each(nodes.props) j" +
            (workspace is null ? "" : " WHERE nodes.workspace=$ws") + " GROUP BY j.key ORDER BY COUNT(*) DESC", ("$ws", workspace));
        using var r = cmd.ExecuteReader();
        var list = new List<(string, int)>();
        while (r.Read()) list.Add((r.GetString(0), r.GetInt32(1)));
        return list;
    });

    public int Count() => Read(db => int.Parse(Scalar(db, "SELECT COUNT(*) FROM nodes") ?? "0"));

    private static List<LinkRef> ReadLinks(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var list = new List<LinkRef>();
        while (r.Read()) list.Add(new LinkRef(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.IsDBNull(5) ? "" : r.GetString(5)));
        return list;
    }

    private static List<NodeSummary> QueryNodes(SqliteConnection db, string clause, params (string, object?)[] args)
    {
        using var cmd = Command(db, $"""
            SELECT n.id, n.path, n.title, n.type, n.workspace, n.tags, n.created, n.modified,
                   n.archived, n.readonly, n.props,
                   COALESCE((SELECT group_concat(a.alias, char(31)) FROM aliases a WHERE a.id = n.id), '') AS aliases
            FROM nodes n {clause}
""", args);
        using var r = cmd.ExecuteReader();
        var list = new List<NodeSummary>();
        while (r.Read())
        {
            list.Add(new NodeSummary(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.IsDBNull(5) ? [] : JsonSerializer.Deserialize<List<string>>(r.GetString(5), Json) ?? [],
                r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.GetInt64(8) == 1, r.GetInt64(9) == 1,
                JsonSerializer.Deserialize<Dictionary<string, object?>>(r.GetString(10), Json) ?? new(),
                r.IsDBNull(11) ? [] : r.GetString(11).Split('\u001f', StringSplitOptions.RemoveEmptyEntries)));
        }
        return list;
    }

    // ---- sqlite helpers ---------------------------------------------------------------------

    private static SqliteCommand Command(SqliteConnection db, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    // Index writes run the same dozen statements for every note. Keeping their commands lets SQLite
    // reuse each compiled statement instead of re-parsing the SQL thousands of times on first open.
    private readonly Dictionary<string, SqliteCommand> _statements = new(StringComparer.Ordinal);
    private SqliteTransaction? _local;

    private SqliteCommand Statement(string sql, (string Name, object? Value)[] args)
    {
        if (!_statements.TryGetValue(sql, out var cmd))
        {
            cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            _statements[sql] = cmd;
        }
        cmd.Transaction = _batch ?? _local; // a cached command must be told which transaction is open
        cmd.Parameters.Clear();
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    /// <summary>Executes a cached statement. Callers hold <c>_gate</c>.</summary>
    private void Run(string sql, params (string, object?)[] args) => Statement(sql, args).ExecuteNonQuery();

    private string? RunScalar(string sql, params (string, object?)[] args)
    {
        var result = Statement(sql, args).ExecuteScalar();
        return result is null or DBNull ? null : Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private void DropStatements()
    {
        foreach (var cmd in _statements.Values) cmd.Dispose();
        _statements.Clear();
    }

    private static void Exec(SqliteConnection db, string sql, params (string, object?)[] args)
    {
        using var cmd = Command(db, sql, args);
        cmd.ExecuteNonQuery();
    }

    private static string? Scalar(SqliteConnection db, string sql, params (string, object?)[] args)
    {
        using var cmd = Command(db, sql, args);
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? null : Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        _disposed = true;
        while (_readers.TryTake(out var reader)) reader.Dispose();
        lock (_gate) { DropStatements(); _db.Dispose(); }
    }
}
