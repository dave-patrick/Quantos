using System.Text.Json;

namespace Quantos.Core.Storage;

public sealed record TrashEntry
{
    public required string EntryId { get; init; }        // folder name under .trash
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Type { get; init; }
    public required string OriginalPath { get; init; }   // vault-relative
    public required string FileName { get; init; }
    public required DateTimeOffset TrashedAt { get; init; }
    public IReadOnlyList<string> Referrers { get; init; } = [];
}

/// <summary>Trash with restore manifests. Trash never expires (BUILD-PLAN decision log).</summary>
public sealed class TrashStore(Vault vault, Clock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Plans where a trashed file goes; the caller moves it (through the journal) and then writes the manifest.</summary>
    public (string EntryDir, string Destination) Plan(string id, string absolutePath)
    {
        var entryId = $"{clock.FileStamp()}_{id}";
        var dir = Path.Combine(vault.Trash, entryId);
        return (dir, Path.Combine(dir, Path.GetFileName(absolutePath)));
    }

    public void WriteManifest(string entryDir, TrashEntry entry) =>
        AtomicFile.WriteAllText(Path.Combine(entryDir, "manifest.json"), JsonSerializer.Serialize(entry, Json));

    public IReadOnlyList<TrashEntry> List()
    {
        if (!Directory.Exists(vault.Trash)) return [];
        var entries = new List<TrashEntry>();
        foreach (var dir in Directory.EnumerateDirectories(vault.Trash))
        {
            var entry = Read(Path.GetFileName(dir));
            if (entry is not null) entries.Add(entry);
        }
        return entries.OrderByDescending(e => e.TrashedAt).ToList();
    }

    public TrashEntry? Read(string entryId)
    {
        if (entryId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || entryId.Contains("..")) return null;
        var manifest = Path.Combine(vault.Trash, entryId, "manifest.json");
        if (!File.Exists(manifest)) return null;
        try { return JsonSerializer.Deserialize<TrashEntry>(AtomicFile.ReadAllText(manifest), Json); }
        catch (JsonException) { return null; }
    }

    public string EntryFile(TrashEntry entry) => Path.Combine(vault.Trash, entry.EntryId, entry.FileName);

    public string EntryDir(string entryId) => Path.Combine(vault.Trash, entryId);

    /// <summary>The most recent trash entry for a node id, if it's in the trash.</summary>
    public TrashEntry? FindById(string id) => List().FirstOrDefault(e => e.Id == id);
}
