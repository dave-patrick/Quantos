using Microsoft.Data.Sqlite;

namespace Quantos.Core;

/// <summary>
/// Derived data only (BUILD-PLAN.md 4). Lives at client-data/cache/cache.sqlite, never in the vault.
/// Safe to delete at any time: <see cref="WriteService.RebuildCache"/> recreates it from the vault files.
/// </summary>
public sealed class NodeCache
{
    private const string CacheSchema = "1";
    private readonly string _connectionString;

    public NodeCache(string cacheFilePath)
    {
        FilePath = Path.GetFullPath(cacheFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = FilePath, Pooling = false }.ToString();
    }

    public string FilePath { get; }

    /// <summary>Creates tables if needed. Returns false if the cache was missing or from an older schema (needs a rebuild).</summary>
    public bool EnsureSchema()
    {
        using var c = Open();
        Exec(c, "CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)");
        var version = Scalar(c, "SELECT value FROM meta WHERE key = 'schema'") as string;
        if (version == CacheSchema) return true;

        Exec(c, """
            DROP TABLE IF EXISTS nodes;
            DROP TABLE IF EXISTS problems;
            CREATE TABLE nodes (
                id        TEXT PRIMARY KEY,
                path      TEXT NOT NULL UNIQUE,
                workspace TEXT NOT NULL,
                title     TEXT NOT NULL,
                state     TEXT NOT NULL CHECK (state IN ('active', 'archived', 'trashed')),
                modified  TEXT NOT NULL,
                version   TEXT NOT NULL,
                trash_id  TEXT
            );
            CREATE INDEX nodes_workspace ON nodes (workspace, state, modified);
            CREATE TABLE problems (path TEXT NOT NULL, problem TEXT NOT NULL);
            INSERT OR REPLACE INTO meta (key, value) VALUES ('schema', '1');
            """);
        return false;
    }

    /// <summary>Deletes the cache file completely. Used by rebuild.</summary>
    public void DeleteFile()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            if (File.Exists(FilePath + suffix)) File.Delete(FilePath + suffix);
    }

    public void Upsert(CachedNode n)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        // A file can't hold two ids; drop any stale row for the same path first.
        cmd.CommandText = """
            DELETE FROM nodes WHERE path = $path AND id <> $id;
            INSERT INTO nodes (id, path, workspace, title, state, modified, version, trash_id)
            VALUES ($id, $path, $workspace, $title, $state, $modified, $version, $trash)
            ON CONFLICT (id) DO UPDATE SET path = excluded.path, workspace = excluded.workspace, title = excluded.title,
                state = excluded.state, modified = excluded.modified, version = excluded.version, trash_id = excluded.trash_id;
            """;
        Bind(cmd, n);
        cmd.ExecuteNonQuery();
    }

    public void AddProblem(string path, string problem)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO problems (path, problem) VALUES ($p, $m)";
        cmd.Parameters.AddWithValue("$p", path);
        cmd.Parameters.AddWithValue("$m", problem);
        cmd.ExecuteNonQuery();
    }

    public CachedNode? Get(string id) => Query("SELECT * FROM nodes WHERE id = $a", id).FirstOrDefault();

    public CachedNode? GetByPath(string path) => Query("SELECT * FROM nodes WHERE path = $a", path).FirstOrDefault();

    public IReadOnlyList<CachedNode> List(string workspace, string state = "active", int limit = 500) =>
        Query("SELECT * FROM nodes WHERE workspace = $a AND state = $b ORDER BY modified DESC LIMIT $c", workspace, state, limit);

    public IReadOnlyList<CachedNode> ListTrashed() =>
        Query("SELECT * FROM nodes WHERE state = 'trashed' ORDER BY modified DESC");

    public IReadOnlyList<(string Path, string Problem)> Problems()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT path, problem FROM problems ORDER BY path";
        using var r = cmd.ExecuteReader();
        var list = new List<(string, string)>();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
        return list;
    }

    public Dictionary<string, long> CountsByState()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT state, COUNT(*) FROM nodes GROUP BY state";
        using var r = cmd.ExecuteReader();
        var d = new Dictionary<string, long>();
        while (r.Read()) d[r.GetString(0)] = r.GetInt64(1);
        return d;
    }

    private IReadOnlyList<CachedNode> Query(string sql, params object[] args)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        for (var i = 0; i < args.Length; i++) cmd.Parameters.AddWithValue("$" + (char)('a' + i), args[i]);
        using var r = cmd.ExecuteReader();
        var list = new List<CachedNode>();
        while (r.Read())
        {
            list.Add(new CachedNode(
                r.GetString(r.GetOrdinal("id")), r.GetString(r.GetOrdinal("path")), r.GetString(r.GetOrdinal("workspace")),
                r.GetString(r.GetOrdinal("title")), r.GetString(r.GetOrdinal("state")), r.GetString(r.GetOrdinal("modified")),
                r.GetString(r.GetOrdinal("version")), r.IsDBNull(r.GetOrdinal("trash_id")) ? null : r.GetString(r.GetOrdinal("trash_id"))));
        }
        return list;
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        Exec(c, "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 3000;");
        return c;
    }

    private static void Bind(SqliteCommand cmd, CachedNode n)
    {
        cmd.Parameters.AddWithValue("$id", n.Id);
        cmd.Parameters.AddWithValue("$path", n.Path);
        cmd.Parameters.AddWithValue("$workspace", n.Workspace);
        cmd.Parameters.AddWithValue("$title", n.Title);
        cmd.Parameters.AddWithValue("$state", n.State);
        cmd.Parameters.AddWithValue("$modified", n.Modified);
        cmd.Parameters.AddWithValue("$version", n.Version);
        cmd.Parameters.AddWithValue("$trash", (object?)n.TrashId ?? DBNull.Value);
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}

/// <summary>One cached row. <see cref="Path"/> is vault-relative with forward slashes.</summary>
public sealed record CachedNode(string Id, string Path, string Workspace, string Title, string State, string Modified, string Version, string? TrashId);
