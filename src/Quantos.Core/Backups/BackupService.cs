using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Quantos.Core.Markdown;
using Quantos.Core.Security;
using Quantos.Core.Storage;

namespace Quantos.Core.Backups;

/// <summary>Per computer (client-data): the folder may be a local drive, OneDrive or a network share.</summary>
public sealed record BackupSettings
{
    public bool Enabled { get; init; } = true;
    /// <summary>Null means client-data\backups.</summary>
    public string? Folder { get; init; }
    public int IntervalHours { get; init; } = 24;
    /// <summary>How many backups of this vault to keep; older ones are deleted after a successful backup.</summary>
    public int Keep { get; init; } = 14;
    /// <summary>Version history can be large, so it's left out unless asked for.</summary>
    public bool IncludeHistory { get; init; }
    /// <summary>
    /// Folders linked in from elsewhere on the computer are outside the vault, so they are left out
    /// unless this is on. When it is, their files go into the backup under "linked/&lt;name&gt;/".
    /// </summary>
    public bool IncludeLinked { get; init; } = true;
}

public sealed record BackupInfo(string Name, string Path, DateTimeOffset Created, long Size, int Files);
public sealed record BackupFile(string Path, long Size, string Sha256);
public sealed record BackupFolder(string Name, string Path, int Files);
public sealed record BackupManifest(string Vault, DateTimeOffset Created, string App, bool History, List<BackupFile> Files, List<BackupFolder>? Linked = null);
public sealed record BackupNote(string Path, string? Id, string Title, bool ExistsNow);
public sealed record BackupStatus(BackupSettings Settings, string Folder, DateTimeOffset? Last, DateTimeOffset? Next, bool Running, string? Error, IReadOnlyList<BackupInfo> Backups, IReadOnlyList<BackupFolder> LinkedFolders);

/// <summary>
/// Rolling backups (Release 1.1): a zip of the vault with a manifest of file hashes, written to a
/// temporary file and renamed when complete, on a schedule and on request. Retention removes only
/// this vault's own backup files beyond the number to keep. Restoring never overwrites the open
/// vault: a whole backup goes to a new folder, and a single note comes back as a new version.
/// </summary>
public sealed partial class BackupService : IDisposable
{
    public const string ManifestName = "quantos-backup.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    // Not part of a backup: rebuildable or transient things, the lock, and exports (copies of notes).
    private static readonly string[] Skip = [".git", "exports", ".state/journal", ".state/backend.lock"];
    private readonly WriteService _writes;
    private readonly string _settingsFile, _defaultFolder;
    private readonly Timer _timer;
    private readonly SemaphoreSlim _running = new(1, 1);
    private string? _error;

    public BackupService(WriteService writes, string settingsFolder, string defaultFolder)
    {
        _writes = writes;
        _settingsFile = Path.Combine(settingsFolder, "backup.json");
        _defaultFolder = defaultFolder;
        // First check two minutes after opening (start-up is busy), then every quarter hour.
        _timer = new Timer(_ => _ = RunIfDueAsync(), null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(15));
    }

    public BackupSettings Settings
    {
        get
        {
            try { return File.Exists(_settingsFile) ? JsonSerializer.Deserialize<BackupSettings>(AtomicFile.ReadAllText(_settingsFile), Json) ?? new() : new(); }
            catch (JsonException) { return new(); }
        }
    }

    public void SaveSettings(BackupSettings settings)
    {
        if (settings.IntervalHours is < 1 or > 24 * 30) throw new QuantosException("Back up every 1 hour to 30 days.");
        if (settings.Keep is < 1 or > 1000) throw new QuantosException("Keep between 1 and 1,000 backups.");
        if (!string.IsNullOrWhiteSpace(settings.Folder))
        {
            var folder = Path.GetFullPath(settings.Folder);
            if (IsInside(folder, _writes.Vault.Root)) throw new QuantosException("Choose a folder outside the vault, or the backups would back themselves up.");
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new QuantosException($"Can't use that folder: {ex.Message}"); }
            settings = settings with { Folder = folder };
        }
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
        AtomicFile.WriteAllText(_settingsFile, JsonSerializer.Serialize(settings, Json));
    }

    public string Folder => string.IsNullOrWhiteSpace(Settings.Folder) ? _defaultFolder : Settings.Folder!;
    // Name plus a short hash of the vault's location: two vaults both called "Vault" sharing a backup
    // folder never see (or prune) each other's backups.
    private string Prefix
    {
        get
        {
            var root = Path.GetFullPath(_writes.Vault.Root).TrimEnd('\\', '/');
            var tag = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(root.ToLowerInvariant())))[..6].ToLowerInvariant();
            return "quantos-" + Vault.Slug(Path.GetFileName(root)) + "-" + tag + "-";
        }
    }

    public BackupStatus Status()
    {
        var s = Settings;
        var backups = List();
        DateTimeOffset? last = backups.Count > 0 ? backups[0].Created : null;
        DateTimeOffset? next = s.Enabled ? (last ?? DateTimeOffset.Now).AddHours(last is null ? 0 : s.IntervalHours) : null;
        return new BackupStatus(s, Folder, last, next, _running.CurrentCount == 0, _error, backups, LinkedFolders());
    }

    /// <summary>The linked folders and how many files each holds, for the settings page.</summary>
    public List<BackupFolder> LinkedFolders()
    {
        var list = new List<BackupFolder>();
        foreach (var (name, path) in _writes.Vault.LinkedRoots)
        {
            var count = 0;
            try { count = Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count() : 0; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            list.Add(new BackupFolder(name, path, count));
        }
        return list;
    }

    /// <summary>This vault's backups, newest first.</summary>
    public List<BackupInfo> List()
    {
        if (!Directory.Exists(Folder)) return [];
        return Directory.EnumerateFiles(Folder, Prefix + "*.zip")
            .Select(f => (File: new FileInfo(f), Stamp: StampOf(Path.GetFileName(f))))
            .Where(x => x.Stamp is not null)
            .OrderByDescending(x => x.Stamp)
            .Select(x => new BackupInfo(x.File.Name, x.File.FullName, x.Stamp!.Value, x.File.Length, -1))
            .ToList();
    }

    private DateTimeOffset? StampOf(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var m = StampPattern().Match(name[Prefix.Length..]);
        return m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var t)
            ? new DateTimeOffset(t) : null;
    }

    private async Task RunIfDueAsync()
    {
        var s = Settings;
        if (!s.Enabled) return;
        var last = List().FirstOrDefault()?.Created;
        if (last is not null && DateTimeOffset.Now - last < TimeSpan.FromHours(s.IntervalHours)) return;
        try { await BackUpAsync(Actor.System); } catch { /* recorded in _error */ }
    }

    /// <summary>Makes a backup now. Returns it once complete and checked.</summary>
    public async Task<BackupInfo> BackUpAsync(Actor actor, CancellationToken ct = default)
    {
        _writes.Permissions.Demand(actor, Permission.Read, "Back up the vault");
        if (!await _running.WaitAsync(0, ct)) throw new QuantosException("A backup is already running.", 409);
        try
        {
            var result = await Task.Run(() => Write(ct), ct);
            Prune();
            _error = null;
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _error = ex is IOException or UnauthorizedAccessException ? $"Couldn't write the backup to {Folder}: {ex.Message}" : ex.Message;
            throw new QuantosException(_error);
        }
        finally { _running.Release(); }
    }

    private BackupInfo Write(CancellationToken ct)
    {
        var s = Settings;
        Directory.CreateDirectory(Folder);
        var created = DateTimeOffset.Now;
        var name = Prefix + created.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".zip";
        var final = Path.Combine(Folder, name);
        var partial = final + ".partial";
        var root = _writes.Vault.Root;
        var files = new List<BackupFile>();
        var linked = new List<BackupFolder>();
        using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
        {
            foreach (var abs in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(root, abs).Replace('\\', '/');
                if (Skip.Any(x => rel.Equals(x, StringComparison.OrdinalIgnoreCase) || rel.StartsWith(x + "/", StringComparison.OrdinalIgnoreCase))) continue;
                if (!s.IncludeHistory && rel.StartsWith(".history/", StringComparison.OrdinalIgnoreCase)) continue;
                byte[] bytes;
                // Writes replace files whole (temporary file then rename), so a read sees a complete version.
                try { bytes = File.ReadAllBytes(abs); }
                catch (IOException) { Thread.Sleep(200); bytes = File.ReadAllBytes(abs); }
                var entry = zip.CreateEntry(rel, CompressionLevel.Optimal);
                entry.LastWriteTime = File.GetLastWriteTime(abs);
                using (var stream = entry.Open()) stream.Write(bytes);
                files.Add(new BackupFile(rel, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
            }
            // Folders linked in from elsewhere on the computer hold notes too, so they are backed up
            // with the vault, under the same "linked/<name>/" names the vault uses for them.
            if (s.IncludeLinked)
            {
                foreach (var (folder, path) in _writes.Vault.LinkedRoots)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!Directory.Exists(path)) continue;
                    var count = 0;
                    foreach (var abs in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        var name2 = Path.GetFileName(abs);
                        if (name2.StartsWith('.') || name2.EndsWith(AtomicFile.TempSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                        var rel = "linked/" + folder + "/" + Path.GetRelativePath(path, abs).Replace('\\', '/');
                        byte[] bytes;
                        try { bytes = File.ReadAllBytes(abs); }
                        catch (IOException) { continue; }
                        catch (UnauthorizedAccessException) { continue; }
                        var entry = zip.CreateEntry(rel, CompressionLevel.Optimal);
                        entry.LastWriteTime = File.GetLastWriteTime(abs);
                        using (var stream = entry.Open()) stream.Write(bytes);
                        files.Add(new BackupFile(rel, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
                        count++;
                    }
                    linked.Add(new BackupFolder(folder, path, count));
                }
            }
            var manifest = new BackupManifest(root, created, "Quantos " + typeof(BackupService).Assembly.GetName().Version, s.IncludeHistory, files, linked);
            using var m = zip.CreateEntry(ManifestName).Open();
            JsonSerializer.Serialize(m, manifest, Json);
        }
        var problems = Verify(partial);
        if (problems.Count > 0) { File.Delete(partial); throw new QuantosException("The backup didn't check out: " + problems[0]); }
        File.Move(partial, final, overwrite: false);
        return new BackupInfo(name, final, created, new FileInfo(final).Length, files.Count);
    }

    /// <summary>Re-reads a backup and compares every file with its recorded hash. Empty means sound.</summary>
    public List<string> Verify(string path)
    {
        var problems = new List<string>();
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var manifestEntry = zip.GetEntry(ManifestName);
            if (manifestEntry is null) return ["It has no manifest, so it isn't a Quantos backup."];
            BackupManifest? manifest;
            using (var m = manifestEntry.Open()) manifest = JsonSerializer.Deserialize<BackupManifest>(m, Json);
            foreach (var f in manifest?.Files ?? [])
            {
                var e = zip.GetEntry(f.Path);
                if (e is null) { problems.Add($"{f.Path} is missing."); continue; }
                using var s = e.Open();
                if (Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant() != f.Sha256) problems.Add($"{f.Path} is damaged.");
            }
        }
        catch (InvalidDataException) { problems.Add("The file isn't a readable zip."); }
        return problems;
    }

    public List<string> Verify(string name, Actor actor)
    {
        _writes.Permissions.Demand(actor, Permission.Read, "Check a backup");
        return Verify(Resolve(name));
    }

    /// <summary>Keeps the newest backups; deletes only this vault's own backup files beyond that number.</summary>
    private void Prune()
    {
        foreach (var old in List().Skip(Settings.Keep))
            try { File.Delete(old.Path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (var stale in Directory.EnumerateFiles(Folder, Prefix + "*.zip.partial").Where(f => DateTime.Now - File.GetLastWriteTime(f) > TimeSpan.FromHours(1)))
            try { File.Delete(stale); } catch (IOException) { }
    }

    private string Resolve(string name)
    {
        if (name != Path.GetFileName(name) || StampOf(name) is null) throw new QuantosException("That isn't one of this vault's backups.", 404);
        var path = Path.Combine(Folder, name);
        return File.Exists(path) ? path : throw new QuantosException("That backup is no longer there.", 404);
    }

    /// <summary>The notes in a backup, to pick one to restore.</summary>
    public List<BackupNote> Notes(string name, Actor actor)
    {
        _writes.Permissions.Demand(actor, Permission.Read, "Look inside a backup");
        using var zip = ZipFile.OpenRead(Resolve(name));
        var notes = new List<BackupNote>();
        foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !e.FullName.StartsWith(".")))
        {
            using var reader = new StreamReader(e.Open());
            var doc = FrontmatterDocument.Parse(reader.ReadToEnd());
            var id = doc.GetString(NodeKeys.Id);
            var title = doc.GetString(NodeKeys.Title) ?? Path.GetFileNameWithoutExtension(e.FullName);
            notes.Add(new BackupNote(e.FullName, id, title, id is not null && _writes.Get(id) is not null));
        }
        return notes.OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Brings one note back. If it still exists, the backup's text is saved as a new version (the
    /// current text stays in history); if not, it's recreated where it was.
    /// </summary>
    public Node RestoreNote(string name, string path, Actor actor)
    {
        _writes.Permissions.Demand(actor, Permission.CreateEdit, "Restore a note from a backup");
        string content;
        using (var zip = ZipFile.OpenRead(Resolve(name)))
        {
            var e = zip.GetEntry(path) ?? throw new QuantosException("That note isn't in the backup.", 404);
            using var reader = new StreamReader(e.Open());
            content = reader.ReadToEnd();
        }
        var id = FrontmatterDocument.Parse(content).GetString(NodeKeys.Id);
        if (id is not null && _writes.Get(id) is not null)
        {
            return _writes.ReplaceContent(id, content, actor);
        }
        // A note that lived in a linked folder comes back into the vault: the other app's own copy
        // is left alone, and the file that was there is not overwritten.
        if (path.StartsWith(Vault.LinkedPrefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            var doc = FrontmatterDocument.Parse(content);
            var title = doc.GetString(NodeKeys.Title) ?? Path.GetFileNameWithoutExtension(path);
            return _writes.Create(new CreateNodeRequest
            {
                Title = title + " (restored)",
                Type = NodeTypes.Note,
                Workspace = _writes.Vault.WorkspaceNames().FirstOrDefault() ?? Vault.DefaultWorkspace,
                Body = doc.Body
            }, actor);
        }
        var target = _writes.Vault.Absolute(path);
        if (File.Exists(target)) target = Vault.UniquePath(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target));
        var ws = Vault.WorkspaceOf(_writes.Vault.Relative(target));
        if (ws.Length > 0) _writes.Vault.EnsureWorkspace(ws);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        AtomicFile.WriteAllText(target, content);
        var node = _writes.Index(target, content);
        _writes.Changes.Publish(new ChangeEvent(ChangeKind.Created, node.Id, node.Path, Origin: actor.ToString()));
        return node;
    }

    /// <summary>Unpacks a whole backup into a new folder (never over an existing one) to open as a vault.</summary>
    public string RestoreToFolder(string name, string? folder, Actor actor)
    {
        _writes.Permissions.Demand(actor, Permission.Read, "Restore a backup");
        var source = Resolve(name);
        var target = string.IsNullOrWhiteSpace(folder)
            ? Path.Combine(Path.GetDirectoryName(_writes.Vault.Root.TrimEnd('\\', '/'))!, $"{Path.GetFileName(_writes.Vault.Root.TrimEnd('\\', '/'))} (restored {StampOf(name)!.Value:yyyy-MM-dd HHmm})")
            : Path.GetFullPath(folder);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()) throw new QuantosException($"{target} already exists and isn't empty. Choose another folder.");
        var problems = Verify(source);
        if (problems.Count > 0) throw new QuantosException("This backup is damaged: " + problems[0]);
        Directory.CreateDirectory(target);
        using var zip = ZipFile.OpenRead(source);
        foreach (var e in zip.Entries.Where(e => e.FullName != ManifestName && !e.FullName.EndsWith('/')))
        {
            var dest = Path.GetFullPath(Path.Combine(target, e.FullName));
            if (!IsInside(dest, target)) continue; // a path that climbs out of the folder isn't ours
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, overwrite: false);
        }
        PointLinkedFoldersAtTheRestoredCopies(target, source);
        return target;
    }

    /// <summary>
    /// The restored vault carries its own copies of the linked folders, so its settings point at
    /// those copies rather than at the folders on the computer the backup came from.
    /// </summary>
    private void PointLinkedFoldersAtTheRestoredCopies(string target, string source)
    {
        List<BackupFolder>? linked;
        try
        {
            using var zip = ZipFile.OpenRead(source);
            var entry = zip.GetEntry(ManifestName);
            if (entry is null) return;
            using var m = entry.Open();
            linked = JsonSerializer.Deserialize<BackupManifest>(m, Json)?.Linked;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException) { return; }
        if (linked is null || linked.Count == 0) return;
        var folders = new System.Text.Json.Nodes.JsonArray();
        foreach (var f in linked)
        {
            var copy = Path.Combine(target, "linked", f.Name);
            if (!Directory.Exists(copy)) continue;
            folders.Add(new System.Text.Json.Nodes.JsonObject { ["name"] = f.Name, ["path"] = copy });
        }
        if (folders.Count == 0) return;
        var file = Path.Combine(target, Vault.LinkedFoldersFile);
        AtomicFile.WriteAllText(file, new System.Text.Json.Nodes.JsonObject { ["folders"] = folders }.ToJsonString(Json));
    }

    private static bool IsInside(string path, string folder)
    {
        var f = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^(\d{8}-\d{6})\.zip$")]
    private static partial Regex StampPattern();

    public void Dispose() => _timer.Dispose();
}
