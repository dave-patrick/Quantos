using System.Text.Json;
using Microsoft.Data.Sqlite;
using Quantos.Core.Markdown;

namespace Quantos.Core.Cache;

public sealed record CachedFile(string Path, string Id, string Hash, long MTime, long Size);

public sealed record NodeSummary(
    string Id, string Path, string Title, string Type, string Workspace, IReadOnlyList<string> Tags,
    string? Created, string? Modified, bool Archived, bool ReadOnly, Dictionary<string, object?> Properties, IReadOnlyList<string> Aliases);

public sealed record SearchHit(string Id, string Title, string Type, string Workspace, string Path, string Snippet);

public sealed record LinkRef(string SourceId, string SourceTitle, string SourceWorkspace, string Target, string Kind, string Context);

/// <summary>
/// Derived data in client-data/cache/cache.sqlite. Nothing here is authoritative; it can be deleted
/// and rebuilt from the vault at any time (BUILD-PLAN section 4).
/// </summary>
public sealed class CacheIndex : IDisposable
{
    public const int Version = 8;
    private readonly SqliteConnection _db;
    private readonly Lock _gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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

    private static void CreateSchema(SqliteConnection db)
    {
        Exec(db, """
            DROP TABLE IF EXISTS files; DROP TABLE IF EXISTS nodes; DROP TABLE IF EXISTS aliases;
            DROP TABLE IF EXISTS links; DROP TABLE IF EXISTS search; DROP TABLE IF EXISTS inline_tasks;
            DROP TABLE IF EXISTS attachment_refs;
            CREATE TABLE files(path TEXT PRIMARY KEY COLLATE NOCASE, id TEXT, hash TEXT, mtime INTEGER, size INTEGER);
            CREATE INDEX files_id ON files(id);
            CREATE TABLE nodes(id TEXT PRIMARY KEY, path TEXT COLLATE NOCASE, title TEXT, type TEXT, workspace TEXT,
                tags TEXT, created TEXT, modified TEXT, archived INTEGER, readonly INTEGER, props TEXT);
            CREATE INDEX nodes_title ON nodes(title COLLATE NOCASE);
            CREATE INDEX nodes_type ON nodes(type, workspace);
            CREATE TABLE aliases(alias TEXT COLLATE NOCASE, id TEXT);
            CREATE INDEX aliases_alias ON aliases(alias);
            CREATE TABLE links(src TEXT, target TEXT, kind TEXT, fragment TEXT, context TEXT);
            CREATE INDEX links_target ON links(target);
            CREATE INDEX links_src ON links(src);
            CREATE TABLE inline_tasks(note_id TEXT, task_id TEXT, checked INTEGER, text TEXT);
            CREATE INDEX inline_tasks_task ON inline_tasks(task_id);
            CREATE INDEX inline_tasks_note ON inline_tasks(note_id);
            CREATE TABLE attachment_refs(node_id TEXT, path TEXT COLLATE NOCASE);
            CREATE INDEX attachment_refs_path ON attachment_refs(path);
            CREATE VIRTUAL TABLE search USING fts5(id UNINDEXED, title, body, tokenize='unicode61 remove_diacritics 2');
            """);
        Exec(db, $"INSERT OR REPLACE INTO meta(key, value) VALUES('cache_version', '{Version}')");
    }

    /// <summary>The cache belongs to one vault; opening it for another vault resets it.</summary>
    public void BindToVault(string vaultRoot)
    {
        lock (_gate)
        {
            var bound = Scalar(_db, "SELECT value FROM meta WHERE key='vault_root'");
            if (bound is not null && string.Equals(bound, vaultRoot, StringComparison.OrdinalIgnoreCase)) return;
            if (bound is not null) { CreateSchema(_db); WasReset = true; }
            Exec(_db, "INSERT OR REPLACE INTO meta(key, value) VALUES('vault_root', $v)", ("$v", vaultRoot));
            if (bound is null && Scalar(_db, "SELECT COUNT(*) FROM files") != "0") WasReset = true;
        }
    }

    public void Clear()
    {
        lock (_gate) { CreateSchema(_db); WasReset = true; }
    }

    // ---- writes -----------------------------------------------------------------------------

    private SqliteTransaction? _batch;

    /// <summary>Groups many index writes into one transaction (start-up scans and rebuilds).</summary>
    public IDisposable Batch()
    {
        lock (_gate) _batch ??= _db.BeginTransaction();
        return new BatchScope(this);
    }

    private sealed class BatchScope(CacheIndex owner) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._gate)
            {
                owner._batch?.Commit();
                owner._batch?.Dispose();
                owner._batch = null;
            }
        }
    }

    public void Upsert(Node node, long mtime, long size)
    {
        lock (_gate)
        {
            using var tx = _batch is null ? _db.BeginTransaction() : null;
            RemoveInternal(node.Path, node.Id);
            Exec(_db, "INSERT INTO files(path, id, hash, mtime, size) VALUES($p, $id, $h, $m, $s)",
                ("$p", node.Path), ("$id", node.Id), ("$h", node.Hash), ("$m", mtime), ("$s", size));
            if (!string.IsNullOrEmpty(node.Id))
            {
                Exec(_db, "INSERT OR REPLACE INTO nodes VALUES($id, $p, $t, $type, $ws, $tags, $c, $mod, $a, $ro, $props)",
                    ("$id", node.Id), ("$p", node.Path), ("$t", node.Title), ("$type", node.Type), ("$ws", node.Workspace),
                    ("$tags", JsonSerializer.Serialize(node.Tags, Json)), ("$c", node.Created), ("$mod", node.Modified),
                    ("$a", node.Archived ? 1 : 0), ("$ro", node.ReadOnly ? 1 : 0), ("$props", JsonSerializer.Serialize(node.Properties, Json)));
                foreach (var alias in node.Aliases)
                    Exec(_db, "INSERT INTO aliases VALUES($a, $id)", ("$a", alias), ("$id", node.Id));
                foreach (var link in Links.Find(node.Body))
                    Exec(_db, "INSERT INTO links VALUES($s, $t, $k, $f, $c)", ("$s", node.Id),
                        ("$t", link.IsResolved ? link.Target : "?" + link.Target), ("$k", link.Kind == LinkKind.Embed ? "embed" : "link"),
                        ("$f", link.Fragment), ("$c", ContextLine(node.Body, link.Start)));
                // Links held in properties (an owner, attendees) count as links too: backlinks, graph, renames.
                foreach (var (key, value) in node.Properties)
                    foreach (var text in PropertyStrings(value))
                        foreach (var link in Links.Find(text).Where(l => l.IsResolved))
                            Exec(_db, "INSERT INTO links VALUES($s, $t, 'link', $f, $c)", ("$s", node.Id), ("$t", link.Target), ("$f", link.Fragment), ("$c", $"{key}: {text}"));
                foreach (var region in Links.SyncRegions(node.Body))
                    Exec(_db, "INSERT INTO links VALUES($s, $t, $k, NULL, $c)", ("$s", node.Id), ("$t", region.Id), ("$k", "sync"), ("$c", ContextLine(region.Content, 0)));
                foreach (var task in Links.InlineTasks(node.Body))
                    Exec(_db, "INSERT INTO inline_tasks VALUES($n, $t, $c, $x)", ("$n", node.Id), ("$t", task.Id), ("$c", task.Checked ? 1 : 0), ("$x", task.Text));
                foreach (var attachment in Attachments.Find(node.Body))
                    Exec(_db, "INSERT INTO attachment_refs VALUES($n, $p)", ("$n", node.Id), ("$p", attachment));
                Exec(_db, "INSERT INTO search(id, title, body) VALUES($id, $t, $b)", ("$id", node.Id), ("$t", node.Title), ("$b", Links.PlainText(node.Body)));
            }
            tx?.Commit();
        }
    }

    public void Remove(string path)
    {
        lock (_gate)
        {
            using var tx = _batch is null ? _db.BeginTransaction() : null;
            RemoveInternal(path, null);
            tx?.Commit();
        }
    }

    private void RemoveInternal(string path, string? id)
    {
        var existingId = Scalar(_db, "SELECT id FROM files WHERE path=$p", ("$p", path));
        foreach (var target in new[] { existingId, id }.Where(x => !string.IsNullOrEmpty(x)).Distinct())
        {
            // Only remove node rows owned by this path (a duplicate id elsewhere keeps its row).
            var owner = Scalar(_db, "SELECT path FROM nodes WHERE id=$id", ("$id", target));
            if (owner is not null && !string.Equals(owner, path, StringComparison.OrdinalIgnoreCase) && target != id) continue;
            foreach (var table in new[] { "aliases WHERE id", "links WHERE src", "inline_tasks WHERE note_id", "attachment_refs WHERE node_id", "search WHERE id", "nodes WHERE id" })
                Exec(_db, $"DELETE FROM {table}=$id", ("$id", target));
        }
        Exec(_db, "DELETE FROM files WHERE path=$p", ("$p", path));
    }

    private static string ContextLine(string body, int index)
    {
        var start = body.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        var end = body.IndexOf('\n', index);
        var line = (end < 0 ? body[start..] : body[start..end]).Trim();
        return line.Length > 240 ? line[..240] + "…" : line;
    }

    // ---- reads ------------------------------------------------------------------------------

    public CachedFile? GetFile(string path)
    {
        lock (_gate)
        {
            using var cmd = Command(_db, "SELECT path, id, hash, mtime, size FROM files WHERE path=$p", ("$p", path));
            using var r = cmd.ExecuteReader();
            return r.Read() ? new CachedFile(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4)) : null;
        }
    }

    public IReadOnlyList<CachedFile> AllFiles()
    {
        lock (_gate)
        {
            using var cmd = Command(_db, "SELECT path, id, hash, mtime, size FROM files");
            using var r = cmd.ExecuteReader();
            var list = new List<CachedFile>();
            while (r.Read()) list.Add(new CachedFile(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4)));
            return list;
        }
    }

    public string? PathOf(string id)
    {
        lock (_gate) return Scalar(_db, "SELECT path FROM nodes WHERE id=$id", ("$id", id));
    }

    public NodeSummary? Get(string id)
    {
        lock (_gate) return QueryNodes("WHERE id=$id", ("$id", id)).FirstOrDefault();
    }

    public IReadOnlyList<NodeSummary> List(string? workspace = null, string? type = null, bool includeArchived = false, int limit = 5000)
    {
        lock (_gate)
        {
            var where = new List<string>();
            var args = new List<(string, object?)>();
            if (workspace is not null) { where.Add("workspace=$ws"); args.Add(("$ws", workspace)); }
            if (type is not null) { where.Add("type=$type"); args.Add(("$type", type)); }
            if (!includeArchived) where.Add("archived=0");
            var clause = (where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "") + $" ORDER BY modified DESC LIMIT {limit}";
            return QueryNodes(clause, [.. args]);
        }
    }

    /// <summary>Ids whose title or alias matches, case-insensitively. Workspace null means all.</summary>
    public IReadOnlyList<NodeSummary> FindByTitle(string title, string? workspace)
    {
        lock (_gate)
        {
            var wsClause = workspace is null ? "" : " AND workspace=$ws";
            return QueryNodes($"WHERE (title=$t COLLATE NOCASE OR id IN (SELECT id FROM aliases WHERE alias=$t)){wsClause} ORDER BY archived, title",
                ("$t", title), ("$ws", workspace));
        }
    }

    /// <summary>Raw SQL over nodes for structured queries; <paramref name="where"/> is built only by trusted code.</summary>
    public IReadOnlyList<NodeSummary> Query(string where, params (string, object?)[] args)
    {
        lock (_gate) return QueryNodes(where, args);
    }

    public IReadOnlyList<SearchHit> Search(string query, string? workspace = null, int limit = 50)
    {
        var fts = ToFtsQuery(query);
        if (fts.Length == 0) return [];
        lock (_gate)
        {
            var sql = """
                SELECT n.id, n.title, n.type, n.workspace, n.path, snippet(search, 2, '[[[', ']]]', '…', 12)
                FROM search JOIN nodes n ON n.id = search.id
                WHERE search MATCH $q
                """ + (workspace is null ? "" : " AND n.workspace=$ws") + " ORDER BY bm25(search, 0, 8.0, 1.0) LIMIT " + limit;
            using var cmd = Command(_db, sql, ("$q", fts), ("$ws", workspace));
            using var r = cmd.ExecuteReader();
            var hits = new List<SearchHit>();
            while (r.Read()) hits.Add(new SearchHit(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)));
            return hits;
        }
    }

    private static string ToFtsQuery(string query)
    {
        var terms = query.Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => new string(t.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '\'').ToArray()).Replace("\"", ""))
            .Where(t => t.Length > 0)
            .Select(t => "\"" + t + "\"*");
        return string.Join(" ", terms);
    }

    public IReadOnlyList<LinkRef> Backlinks(string id)
    {
        lock (_gate)
        {
            using var cmd = Command(_db, """
                SELECT l.src, n.title, n.workspace, l.target, l.kind, l.context FROM links l JOIN nodes n ON n.id = l.src
                WHERE l.target = $id ORDER BY n.modified DESC
                """, ("$id", id));
            return ReadLinks(cmd);
        }
    }

    public IReadOnlyList<LinkRef> OutLinks(string id)
    {
        lock (_gate)
        {
            using var cmd = Command(_db, """
                SELECT l.src, COALESCE(n.title, substr(l.target, 2)), COALESCE(n.workspace, ''), l.target, l.kind, l.context
                FROM links l LEFT JOIN nodes n ON n.id = l.target WHERE l.src = $id
                """, ("$id", id));
            return ReadLinks(cmd);
        }
    }

    /// <summary>Ids of nodes containing a link or embed to <paramref name="id"/>.</summary>
    public IReadOnlyList<string> Referrers(string id)
    {
        lock (_gate)
        {
            using var cmd = Command(_db, "SELECT DISTINCT src FROM links WHERE target=$id UNION SELECT DISTINCT note_id FROM inline_tasks WHERE task_id=$id", ("$id", id));
            using var r = cmd.ExecuteReader();
            var list = new List<string>();
            while (r.Read()) list.Add(r.GetString(0));
            return list;
        }
    }

    /// <summary>Notes holding a typed, unresolved [[Title]] link (optionally [[ws:Title]]).</summary>
    public IReadOnlyList<string> UnresolvedReferrers(string title)
    {
        lock (_gate)
        {
            using var cmd = Command(_db, "SELECT DISTINCT src FROM links WHERE target = $t COLLATE NOCASE OR target LIKE $w COLLATE NOCASE", ("$t", "?" + title), ("$w", "?%:" + title));
            using var r = cmd.ExecuteReader();
            var list = new List<string>();
            while (r.Read()) list.Add(r.GetString(0));
            return list;
        }
    }

    public IReadOnlyList<(string NoteId, bool Checked, string Text)> InlineTaskRefs(string taskId)
    {
        lock (_gate)
        {
            using var cmd = Command(_db, "SELECT note_id, checked, text FROM inline_tasks WHERE task_id=$id", ("$id", taskId));
            using var r = cmd.ExecuteReader();
            var list = new List<(string, bool, string)>();
            while (r.Read()) list.Add((r.GetString(0), r.GetInt64(1) == 1, r.GetString(2)));
            return list;
        }
    }

    public IReadOnlyList<(string Path, int Count)> AttachmentRefCounts()
    {
        lock (_gate)
        {
            using var cmd = Command(_db, "SELECT path, COUNT(*) FROM attachment_refs GROUP BY path");
            using var r = cmd.ExecuteReader();
            var list = new List<(string, int)>();
            while (r.Read()) list.Add((r.GetString(0), r.GetInt32(1)));
            return list;
        }
    }

    /// <summary>How connected each node is: resolved links out plus links in, in one query (for the graph overview).</summary>
    public IReadOnlyDictionary<string, int> LinkDegrees()
    {
        lock (_gate)
        {
            using var cmd = Command(_db, "SELECT id, COUNT(*) FROM (SELECT src AS id FROM links WHERE target NOT LIKE '?%' UNION ALL SELECT target FROM links) GROUP BY id");
            using var r = cmd.ExecuteReader();
            var map = new Dictionary<string, int>();
            while (r.Read()) map[r.GetString(0)] = r.GetInt32(1);
            return map;
        }
    }

    /// <summary>The text values of a property: a string, or the strings in a list.</summary>
    public static IEnumerable<string> PropertyStrings(object? value) => value switch
    {
        string s => [s],
        System.Collections.IEnumerable list and not IDictionary<string, object?> and not IDictionary<object, object> => list.OfType<string>(),
        _ => []
    };

    /// <summary>The first image each node references, in body order: the default cover on cards.</summary>
    public IReadOnlyDictionary<string, string> FirstImages()
    {
        lock (_gate)
        {
            using var cmd = Command(_db, "SELECT node_id, path FROM attachment_refs WHERE lower(path) GLOB '*.png' OR lower(path) GLOB '*.jpg' OR lower(path) GLOB '*.jpeg' " +
                "OR lower(path) GLOB '*.gif' OR lower(path) GLOB '*.webp' OR lower(path) GLOB '*.svg' ORDER BY rowid");
            using var r = cmd.ExecuteReader();
            var map = new Dictionary<string, string>();
            while (r.Read()) map.TryAdd(r.GetString(0), r.GetString(1));
            return map;
        }
    }

    public IReadOnlyList<(string Key, int Count)> PropertyCounts(string? workspace = null)
    {
        lock (_gate)
        {
            using var cmd = Command(_db, "SELECT j.key, COUNT(*) FROM nodes, json_each(nodes.props) j" +
                (workspace is null ? "" : " WHERE nodes.workspace=$ws") + " GROUP BY j.key ORDER BY COUNT(*) DESC", ("$ws", workspace));
            using var r = cmd.ExecuteReader();
            var list = new List<(string, int)>();
            while (r.Read()) list.Add((r.GetString(0), r.GetInt32(1)));
            return list;
        }
    }

    public int Count()
    {
        lock (_gate) return int.Parse(Scalar(_db, "SELECT COUNT(*) FROM nodes") ?? "0");
    }

    private static List<LinkRef> ReadLinks(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var list = new List<LinkRef>();
        while (r.Read()) list.Add(new LinkRef(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.IsDBNull(5) ? "" : r.GetString(5)));
        return list;
    }

    private List<NodeSummary> QueryNodes(string clause, params (string, object?)[] args)
    {
        using var cmd = Command(_db, "SELECT id, path, title, type, workspace, tags, created, modified, archived, readonly, props, (SELECT group_concat(alias, char(31)) FROM aliases a WHERE a.id = nodes.id) FROM nodes " + clause, args);
        using var r = cmd.ExecuteReader();
        var list = new List<NodeSummary>();
        while (r.Read())
        {
            list.Add(new NodeSummary(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                JsonSerializer.Deserialize<List<string>>(r.GetString(5), Json) ?? [],
                r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.GetInt64(8) == 1, r.GetInt64(9) == 1,
                JsonSerializer.Deserialize<Dictionary<string, object?>>(r.GetString(10), Json) ?? new(),
                r.IsDBNull(11) ? [] : r.GetString(11).Split('\u001f')));
        }
        return list;
    }

    // ---- sqlite helpers ---------------------------------------------------------------------

    private static SqliteCommand Command(SqliteConnection db, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            if (sql.Contains(name)) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
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
        lock (_gate) _db.Dispose();
    }
}
