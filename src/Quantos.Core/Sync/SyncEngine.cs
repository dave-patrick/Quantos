using System.Text.Json;
using Quantos.Core.Security;
using Quantos.Core.Storage;

namespace Quantos.Core.Sync;

public interface ISyncRemote
{
    Task<IReadOnlyList<SyncFile>> Manifest(CancellationToken ct);
    Task<byte[]?> Read(string path, string? expectedHash, CancellationToken ct);
    Task<bool> Apply(SyncMutation mutation, CancellationToken ct);
}

/// <summary>A persisted three-way checkpoint. A failed or interrupted transfer is safe to retry.
/// Concurrent changes never overwrite one another; both versions are kept for the person.</summary>
public sealed class SyncEngine(WriteService writes, string stateFolder, ISyncRemote remote, bool deletionsFirst = false)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim gate = new(1);
    private string Checkpoint => Path.Combine(stateFolder, "checkpoint.json");
    private string ConflictFolder => Path.Combine(stateFolder, "conflicts");

    public IReadOnlyList<SyncConflict> Conflicts() => !Directory.Exists(ConflictFolder) ? [] :
        Directory.EnumerateFiles(ConflictFolder, "*.json").Select(f => JsonSerializer.Deserialize<SyncConflict>(File.ReadAllText(f), Json)!).ToList();

    public async Task<SyncReport> Run(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(stateFolder);
            var baseline = File.Exists(Checkpoint) ? JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(Checkpoint), Json)! : [];
            foreach (var deviceState in baseline.Keys.Where(SyncFiles.IsDeviceState).ToList()) baseline.Remove(deviceState);
            var local = SyncFiles.Manifest(writes).ToDictionary(f => f.Path, f => f.Hash, StringComparer.Ordinal);
            var server = (await remote.Manifest(ct)).Where(f => !SyncFiles.IsDeviceState(f.Path)).ToDictionary(f => f.Path, f => f.Hash, StringComparer.Ordinal);
            // Case-only path differences cannot be represented safely on Windows.
            var paths = local.Keys.Concat(server.Keys).Concat(baseline.Keys).Distinct(StringComparer.Ordinal).Order().ToList();
            if (deletionsFirst)
                paths = paths.OrderBy(path =>
                {
                    var b = baseline.GetValueOrDefault(path); var l = local.GetValueOrDefault(path); var r = server.GetValueOrDefault(path);
                    return b is not null && (l is null && r == b || r is null && l == b) ? 0 : 1;
                }).ThenBy(path => path, StringComparer.Ordinal).ToList();
            if (paths.GroupBy(p => p, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                throw new QuantosException("The shared vault contains filenames that differ only by case. Rename them before syncing to Windows.");
            var pending = Conflicts().ToDictionary(c => c.Path);
            var up = 0; var down = 0;
            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                SyncFiles.Validate(writes.Vault, path);
                var l = local.GetValueOrDefault(path); var r = server.GetValueOrDefault(path); var b = baseline.GetValueOrDefault(path);
                if (pending.TryGetValue(path, out var review))
                {
                    if (review.LocalHash == l && review.RemoteHash == r) continue;
                    var reviewFile = Path.Combine(ConflictFolder, review.Id + ".json");
                    File.Move(reviewFile, reviewFile + ".superseded");
                }
                if (l == r) { baseline[path] = l; continue; }
                if (l == b)
                {
                    var content = await remote.Read(path, r, ct);
                    Verify(content, r);
                    if (writes.ApplySyncFile(new(path, l, content), Path.Combine(stateFolder, "recovery"), Actor.User))
                    { baseline[path] = r; down++; }
                }
                else if (r == b)
                {
                    var content = ReadLocal(path, l);
                    if (await remote.Apply(new(path, r, content), ct)) { baseline[path] = l; up++; }
                }
                else
                {
                    var ours = ReadLocal(path, l);
                    var theirs = await remote.Read(path, r, ct);
                    Verify(theirs, r);
                    var conflict = new SyncConflict(Ulid.New(), path, b, l, r, DateTimeOffset.UtcNow);
                    Directory.CreateDirectory(ConflictFolder);
                    if (ours is not null) AtomicFile.WriteAllBytes(Path.Combine(ConflictFolder, conflict.Id + ".local"), ours);
                    if (theirs is not null) AtomicFile.WriteAllBytes(Path.Combine(ConflictFolder, conflict.Id + ".remote"), theirs);
                    AtomicFile.WriteAllText(Path.Combine(ConflictFolder, conflict.Id + ".json"), JsonSerializer.Serialize(conflict, Json));
                }
                // Persist progress after every transfer. A lost response is detected by equal hashes next time.
                AtomicFile.WriteAllText(Checkpoint, JsonSerializer.Serialize(baseline, Json));
            }
            AtomicFile.WriteAllText(Checkpoint, JsonSerializer.Serialize(baseline, Json));
            return new SyncReport(up, down, Conflicts().Count);
        }
        finally { gate.Release(); }
    }

    public async Task Resolve(string id, bool keepLocal, CancellationToken ct = default)
    {
        if (!Ulid.IsValid(id)) throw new QuantosException("Invalid conflict.");
        await gate.WaitAsync(ct);
        try
        {
            var file = Path.Combine(ConflictFolder, id + ".json");
            var c = JsonSerializer.Deserialize<SyncConflict>(File.ReadAllText(file), Json)!;
            // Refuse to apply a stale decision if either version has changed since the review.
            ReadLocal(c.Path, c.LocalHash);
            var current = await remote.Read(c.Path, c.RemoteHash, ct); Verify(current, c.RemoteHash);
            if (keepLocal)
            {
                if (!await remote.Apply(new(c.Path, c.RemoteHash, Version(id, true)), ct)) throw new QuantosException("The server copy changed. Sync again before resolving.", 409);
            }
            else if (!writes.ApplySyncFile(new(c.Path, c.LocalHash, Version(id, false)), Path.Combine(stateFolder, "recovery"), Actor.User))
                throw new QuantosException("Your device copy changed. Sync again before resolving.", 409);
            var baseline = File.Exists(Checkpoint) ? JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(Checkpoint), Json)! : [];
            baseline[c.Path] = keepLocal ? c.LocalHash : c.RemoteHash;
            AtomicFile.WriteAllText(Checkpoint, JsonSerializer.Serialize(baseline, Json));
            // Keep the full conflict as a recovery record after the choice, too.
            File.Move(file, file + ".resolved");
        }
        finally { gate.Release(); }
    }

    public byte[]? Version(string id, bool local)
    {
        if (!Ulid.IsValid(id)) throw new QuantosException("Invalid conflict.");
        var conflict = JsonSerializer.Deserialize<SyncConflict>(File.ReadAllText(Path.Combine(ConflictFolder, id + ".json")), Json)!;
        var expected = local ? conflict.LocalHash : conflict.RemoteHash;
        var bytes = expected is null ? null : File.ReadAllBytes(Path.Combine(ConflictFolder, id + (local ? ".local" : ".remote")));
        Verify(bytes, expected);
        return bytes;
    }
    private byte[]? ReadLocal(string path, string? expected)
    {
        return writes.Exclusive(() =>
        {
            var full = SyncFiles.Validate(writes.Vault, path);
            var bytes = File.Exists(full) ? File.ReadAllBytes(full) : null;
            Verify(bytes, expected);
            return bytes;
        });
    }
    private static void Verify(byte[]? bytes, string? expected)
    {
        if ((bytes is null ? null : SyncFiles.Hash(bytes)) != expected)
            throw new QuantosException("A file changed during sync. Your changes are safe; retry sync.", 409);
    }
}
