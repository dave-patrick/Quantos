using Quantos.Core.Security;
using Quantos.Core.Storage;
using Quantos.Core.Sync;

namespace Quantos.Core;

public sealed partial class WriteService
{
    /// <summary>Exact, conditional file replication. All device and server sync writes enter here.
    /// Originals are retained before replacement, including binary attachments and deletions.</summary>
    public bool ApplySyncFile(SyncMutation mutation, string recoveryFolder, Actor actor)
    {
        if (actor.Kind is not (ActorKind.User or ActorKind.System)) throw new QuantosException("Only you can sync a vault.", 403);
        Permissions.Demand(actor, Permission.CreateEdit, "Sync vault content");
        lock (_gate)
        {
            var full = SyncFiles.Validate(Vault, mutation.Path);
            if (mutation.Content?.LongLength > SyncFiles.MaxFileBytes) throw new QuantosException("The sync file exceeds 200 MB.");
            var before = File.Exists(full) ? File.ReadAllBytes(full) : null;
            var hash = before is null ? null : SyncFiles.Hash(before);
            if (hash != mutation.ExpectedHash) return false;
            var nextHash = mutation.Content is null ? null : SyncFiles.Hash(mutation.Content);
            if (hash == nextHash) return true;
            if (before is not null)
            {
                // Recovery is outside the synced vault, so it cannot be removed by another device.
                Directory.CreateDirectory(recoveryFolder);
                var recoveryId = Path.Combine(recoveryFolder, Ulid.New());
                AtomicFile.WriteAllBytes(recoveryId + ".bin", before);
                AtomicFile.WriteAllText(recoveryId + ".json", System.Text.Json.JsonSerializer.Serialize(new { path = mutation.Path, hash, replaced = Clock.Stamp() }));
                if (full.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) SnapshotBefore(full);
            }
            if (mutation.Content is null) { if (File.Exists(full)) File.Delete(full); }
            else AtomicFile.WriteAllBytes(full, mutation.Content);
            return true;
        }
    }
}
