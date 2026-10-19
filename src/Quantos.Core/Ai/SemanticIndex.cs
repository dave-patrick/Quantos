using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Quantos.Core.Cache;
using Quantos.Core.Markdown;

namespace Quantos.Core.Ai;

public sealed record SemanticHit(string Id, string Title, string Workspace, string Path, string Text, double Score);

public sealed record SemanticStatus(bool Enabled, string? Model, int Notes, int Indexed, int Pending, bool Working, string? Error);

/// <summary>
/// Meaning search (Release 1.1): notes are cut into passages, each turned into an embedding by the
/// chosen provider and kept in client-data/cache/embeddings.sqlite (derived data, outside the vault,
/// rebuilt if the model changes). Indexing runs in the background, only for notes whose text changed.
/// </summary>
public sealed class SemanticIndex : IDisposable
{
    private const int ChunkChars = 800, BatchSize = 32;
    private static readonly HashSet<string> Skipped = [NodeTypes.Supertag, NodeTypes.Template, "synced_block"];
    private readonly WriteService _writes;
    private readonly Func<AiEndpoint?> _endpoint;
    private readonly Func<string, string?> _key;
    private readonly AiClient _client;
    private readonly SqliteConnection _db;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _stop = new();
    private List<(string Id, string Text, float[] Vector)>? _vectors;
    private int _pending;
    private bool _working;
    private string? _error;

    public SemanticIndex(WriteService writes, string databasePath, Func<AiEndpoint?> endpoint, Func<string, string?> key, AiClient client)
    {
        _writes = writes;
        _endpoint = endpoint;
        _key = key;
        _client = client;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        _db.Open();
        Exec("""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT);
            CREATE TABLE IF NOT EXISTS chunks(node_id TEXT, idx INTEGER, hash TEXT, text TEXT, vector BLOB, PRIMARY KEY(node_id, idx));
            CREATE INDEX IF NOT EXISTS chunks_node ON chunks(node_id);
            """);
        // A different vault means different notes: start over.
        if (Meta("vault") != writes.Vault.Root) { Clear(); SetMeta("vault", writes.Vault.Root); }
        writes.Changes.Changed += OnChanged;
        _ = Task.Run(Loop);
    }

    public SemanticStatus Status()
    {
        var endpoint = _endpoint();
        lock (_gate)
        {
            var indexed = Scalar("SELECT COUNT(DISTINCT node_id) FROM chunks");
            return new SemanticStatus(endpoint is not null, endpoint?.Model, Candidates().Count, indexed, _pending, _working, _error);
        }
    }

    /// <summary>Re-checks every note (after settings change, or by the user).</summary>
    public void Refresh() { _error = null; _wake.Release(); }

    private void OnChanged(ChangeEvent e)
    {
        if (e.Kind is ChangeKind.Created or ChangeKind.Updated or ChangeKind.Moved or ChangeKind.Deleted or ChangeKind.Trashed or ChangeKind.Restored or ChangeKind.Reindexed)
            _wake.Release();
    }

    private List<NodeSummary> Candidates() =>
        _writes.Cache.List(includeArchived: false, limit: 1_000_000).Where(n => !Skipped.Contains(n.Type)).ToList();

    private async Task Loop()
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!_stop.IsCancellationRequested)
        {
            try { await _wake.WaitAsync(TimeSpan.FromMinutes(10), _stop.Token); }
            catch (OperationCanceledException) { return; }
            await Task.Delay(delay, _stop.Token).ContinueWith(_ => { }); // let a burst of saves settle
            while (_wake.CurrentCount > 0) _wake.Wait(0);
            var endpoint = _endpoint();
            if (endpoint is null) continue;
            try { await IndexAll(endpoint); _error = null; delay = TimeSpan.FromSeconds(2); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { _error = ex.Message; delay = TimeSpan.FromSeconds(30); }
            finally { _working = false; _pending = 0; }
        }
    }

    private async Task IndexAll(AiEndpoint endpoint)
    {
        lock (_gate)
            if (Meta("model") != $"{endpoint.Provider}|{endpoint.Url}|{endpoint.Model}") { Clear(); SetMeta("model", $"{endpoint.Provider}|{endpoint.Url}|{endpoint.Model}"); }
        var notes = Candidates();
        var live = notes.Select(n => n.Id).ToHashSet();
        lock (_gate)
        {
            foreach (var gone in Ids().Where(id => !live.Contains(id)).ToList()) Exec("DELETE FROM chunks WHERE node_id=$id", ("$id", gone));
            _vectors = null;
        }
        var todo = new List<(string Id, string Hash, List<string> Chunks)>();
        foreach (var n in notes)
        {
            var node = _writes.Get(n.Id);
            if (node is null) continue;
            var chunks = Chunk(node.Title, node.Body);
            var hash = Hash(string.Join("\u0001", chunks));
            if (StoredHash(n.Id) != hash) todo.Add((n.Id, hash, chunks));
        }
        _pending = todo.Count;
        _working = todo.Count > 0;
        var key = _key(endpoint.Provider);
        // Batches of passages, several notes per request where they fit.
        var queue = new List<(string Id, string Hash, int Index, string Text)>();
        foreach (var (id, hash, chunks) in todo) queue.AddRange(chunks.Select((c, i) => (id, hash, i, c)));
        for (var start = 0; start < queue.Count; start += BatchSize)
        {
            _stop.Token.ThrowIfCancellationRequested();
            var batch = queue.Skip(start).Take(BatchSize).ToList();
            var vectors = await _client.EmbedAsync(endpoint, key, batch.Select(b => b.Text).ToList(), _stop.Token);
            lock (_gate)
            {
                using var tx = _db.BeginTransaction();
                foreach (var id in batch.Where(b => b.Index == 0).Select(b => b.Id)) Exec("DELETE FROM chunks WHERE node_id=$id", ("$id", id));
                for (var i = 0; i < batch.Count; i++)
                    Exec("INSERT OR REPLACE INTO chunks VALUES($id,$i,$h,$t,$v)", ("$id", batch[i].Id), ("$i", batch[i].Index), ("$h", batch[i].Hash), ("$t", batch[i].Text), ("$v", ToBytes(Normalize(vectors[i]))));
                tx.Commit();
                _vectors = null;
            }
            _pending = todo.Count(t => queue.Skip(start + BatchSize).Any(q => q.Id == t.Id));
        }
    }

    /// <summary>The notes whose passages are closest in meaning to the query, best passage per note.</summary>
    public async Task<IReadOnlyList<SemanticHit>> SearchAsync(string query, string? workspace, int limit, CancellationToken ct = default)
    {
        var endpoint = _endpoint() ?? throw new QuantosException("Meaning search isn't set up. Choose an embedding provider in Settings › AI.");
        var q = Normalize((await _client.EmbedAsync(endpoint, _key(endpoint.Provider), [query], ct))[0]);
        List<(string Id, string Text, float[] Vector)> vectors;
        lock (_gate) vectors = _vectors ??= LoadVectors();
        var best = new Dictionary<string, (double Score, string Text)>();
        foreach (var (id, text, v) in vectors)
        {
            if (v.Length != q.Length) continue;
            double dot = 0;
            for (var i = 0; i < v.Length; i++) dot += v[i] * q[i];
            if (!best.TryGetValue(id, out var current) || dot > current.Score) best[id] = (dot, text);
        }
        var hits = new List<SemanticHit>();
        foreach (var (id, (score, text)) in best.OrderByDescending(kv => kv.Value.Score))
        {
            var node = _writes.Cache.Get(id);
            if (node is null || (workspace is not null && node.Workspace != workspace)) continue;
            hits.Add(new SemanticHit(id, node.Title, node.Workspace, node.Path, text, Math.Round(score, 3)));
            if (hits.Count >= limit) break;
        }
        return hits;
    }

    /// <summary>Plain text in passages of about 800 characters, split at paragraphs; each starts with the title for context.</summary>
    public static List<string> Chunk(string title, string body)
    {
        var text = Links.PlainText(body);
        var paragraphs = text.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0);
        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var p in paragraphs)
        {
            foreach (var piece in p.Length <= ChunkChars ? [p] : Enumerable.Range(0, (p.Length + ChunkChars - 1) / ChunkChars).Select(i => p.Substring(i * ChunkChars, Math.Min(ChunkChars, p.Length - i * ChunkChars))))
            {
                if (current.Length > 0 && current.Length + piece.Length > ChunkChars) { chunks.Add(current.ToString()); current.Clear(); }
                if (current.Length > 0) current.Append("\n\n");
                current.Append(piece);
            }
        }
        if (current.Length > 0) chunks.Add(current.ToString());
        if (chunks.Count == 0) chunks.Add("");
        return chunks.Select(c => (title + "\n\n" + c).Trim()).ToList();
    }

    private List<(string, string, float[])> LoadVectors()
    {
        var list = new List<(string, string, float[])>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT node_id, text, vector FROM chunks";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1), FromBytes((byte[])r[2])));
        return list;
    }

    private static float[] Normalize(float[] v)
    {
        double sum = 0;
        foreach (var x in v) sum += x * x;
        var norm = Math.Sqrt(sum);
        if (norm == 0) return v;
        return v.Select(x => (float)(x / norm)).ToArray();
    }

    private static byte[] ToBytes(float[] v) { var b = new byte[v.Length * 4]; Buffer.BlockCopy(v, 0, b, 0, b.Length); return b; }
    private static float[] FromBytes(byte[] b) { var v = new float[b.Length / 4]; Buffer.BlockCopy(b, 0, v, 0, b.Length); return v; }
    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..32];

    private void Clear() { Exec("DELETE FROM chunks"); _vectors = null; }
    private string? StoredHash(string id) { lock (_gate) return ScalarText("SELECT hash FROM chunks WHERE node_id=$id LIMIT 1", ("$id", id)); }
    private List<string> Ids()
    {
        var ids = new List<string>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT node_id FROM chunks";
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetString(0));
        return ids;
    }
    private string? Meta(string key) => ScalarText("SELECT value FROM meta WHERE key=$k", ("$k", key));
    private void SetMeta(string key, string value) => Exec("INSERT OR REPLACE INTO meta VALUES($k,$v)", ("$k", key), ("$v", value));

    private void Exec(string sql, params (string, object?)[] args)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    private int Scalar(string sql) { using var cmd = _db.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt32(cmd.ExecuteScalar()); }
    private string? ScalarText(string sql, params (string, object?)[] args)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd.ExecuteScalar() as string;
    }

    public void Dispose()
    {
        _writes.Changes.Changed -= OnChanged;
        _stop.Cancel();
        lock (_gate) _db.Dispose();
    }
}
