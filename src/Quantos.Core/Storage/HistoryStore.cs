namespace Quantos.Core.Storage;

public sealed record HistoryVersion(string Version, DateTimeOffset SavedAt, long Size, string Kind);

/// <summary>
/// Version snapshots keyed by node id, so history survives renames and moves (STORAGE-SPEC section 5).
/// A version is recorded for the content before a write if it isn't already the newest snapshot
/// (covers files created or edited outside the app), and the content after each save. Saves within
/// <see cref="CoalesceWindow"/> of the previous save replace it, so autosave doesn't produce a
/// version per keystroke. Each note keeps its newest <see cref="KeepVersions"/> versions
/// (Vault/settings.json "historyKeep", default 10, 0 = keep all); older ones are deleted.
/// </summary>
public sealed class HistoryStore(Vault vault, Clock clock)
{
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromMinutes(2);
    public const int DefaultKeep = 10, MaxKeep = 10_000;
    private (DateTime Stamp, long Size, int Keep)? _keepCache;

    /// <summary>Versions kept per note; 0 means all. Read from Vault/settings.json, re-read only when it changes.</summary>
    public int KeepVersions
    {
        get
        {
            var path = Path.Combine(vault.Root, "settings.json");
            try
            {
                if (!File.Exists(path)) return DefaultKeep;
                var info = new FileInfo(path);
                if (_keepCache is { } c && c.Stamp == info.LastWriteTimeUtc && c.Size == info.Length) return c.Keep;
                var keep = DefaultKeep;
                using (var doc = System.Text.Json.JsonDocument.Parse(AtomicFile.ReadAllText(path)))
                    if (doc.RootElement.TryGetProperty("historyKeep", out var k) && k.TryGetInt32(out var n)) keep = Math.Clamp(n, 0, MaxKeep);
                _keepCache = (info.LastWriteTimeUtc, info.Length, keep);
                return keep;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { return DefaultKeep; }
        }
    }

    /// <summary>Deletes this note's versions beyond the newest <see cref="KeepVersions"/>. Returns how many were removed.</summary>
    public int Prune(string id)
    {
        var keep = KeepVersions;
        if (keep == 0 || !Ulid.IsValid(id)) return 0;
        var dir = Path.Combine(vault.History, id);
        if (!Directory.Exists(dir)) return 0;
        var removed = 0;
        foreach (var old in Directory.EnumerateFiles(dir, "*.md").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).Skip(keep))
        {
            try { File.Delete(old); removed++; }
            catch (IOException) { } catch (UnauthorizedAccessException) { } // in use: tried again on the next save
        }
        return removed;
    }

    /// <summary>Applies the limit to every note's history (after the setting changes, and once per start).</summary>
    public int PruneAll(CancellationToken ct = default)
    {
        if (KeepVersions == 0 || !Directory.Exists(vault.History)) return 0;
        var removed = 0;
        foreach (var dir in Directory.EnumerateDirectories(vault.History))
        {
            if (ct.IsCancellationRequested) break;
            removed += Prune(Path.GetFileName(dir));
        }
        return removed;
    }
    public const string Save = "save", Before = "before", External = "external", Restore = "restore", Conflict = "conflict";

    /// <summary>Records <paramref name="content"/> unless it equals the newest snapshot. Returns the version id, or null if unchanged.</summary>
    public string? Record(string id, byte[] content, string kind)
    {
        if (!Ulid.IsValid(id)) return null;
        var dir = Path.Combine(vault.History, id);
        Directory.CreateDirectory(dir);
        var latest = Newest(dir);
        if (latest is not null && Hashing.FileHash(latest.FullName) == Hashing.Sha256(content)) return null;

        if (kind == Save && latest is not null && KindOf(latest.Name) == Save
            && ParseStamp(latest.Name) is { } at && clock.UtcNow - at < CoalesceWindow)
        {
            // Replace the recent autosave snapshot, but only if an older version exists to fall back on.
            if (Directory.EnumerateFiles(dir, "*.md").Count() > 1)
            {
                AtomicFile.WriteAllBytes(latest.FullName, content);
                return Path.GetFileNameWithoutExtension(latest.Name);
            }
        }

        var stamp = clock.FileStamp();
        var path = Path.Combine(dir, $"{stamp}-{kind}.md");
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(dir, $"{stamp}_{n}-{kind}.md");
        AtomicFile.WriteAllBytes(path, content);
        Prune(id);
        return Path.GetFileNameWithoutExtension(path);
    }

    public IReadOnlyList<HistoryVersion> List(string id)
    {
        if (!Ulid.IsValid(id)) return [];
        var dir = Path.Combine(vault.History, id);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.md")
            .Select(f => new FileInfo(f))
            .Select(f => new HistoryVersion(Path.GetFileNameWithoutExtension(f.Name), ParseStamp(f.Name) ?? f.LastWriteTimeUtc, f.Length, KindOf(f.Name)))
            .OrderByDescending(v => v.Version, StringComparer.Ordinal)
            .ToList();
    }

    public string? Read(string id, string version)
    {
        if (!Ulid.IsValid(id) || version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || version.Contains("..")) return null;
        var path = Path.Combine(vault.History, id, version + ".md");
        return File.Exists(path) ? AtomicFile.ReadAllText(path) : null;
    }

    public string? Latest(string id)
    {
        if (!Ulid.IsValid(id)) return null;
        var dir = Path.Combine(vault.History, id);
        var newest = Directory.Exists(dir) ? Newest(dir) : null;
        return newest is null ? null : AtomicFile.ReadAllText(newest.FullName);
    }

    private static FileInfo? Newest(string dir) =>
        Directory.EnumerateFiles(dir, "*.md").Select(f => new FileInfo(f)).MaxBy(f => f.Name, StringComparer.Ordinal);

    private static string KindOf(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var dash = stem.LastIndexOf('-');
        return dash > 0 ? stem[(dash + 1)..] : Save;
    }

    private static DateTimeOffset? ParseStamp(string name) =>
        name.Length >= 18 && DateTimeOffset.TryParseExact(name[..18], "yyyyMMdd'T'HHmmssfff",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}
