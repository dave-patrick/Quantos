using Quantos.Core;
using Quantos.Core.Storage;

namespace Quantos.Backend.Hosting;

public sealed record VaultCheck(string Path, bool Exists, bool Empty, bool LooksLikeVault, bool Writable, string? SyncedService, string? Warning, string? Error);

/// <summary>Owns the open vault session. The backend can start with no vault so first run can choose one.</summary>
public sealed class VaultHost : IDisposable
{
    private readonly Lock _gate = new();
    private readonly ILogger<VaultHost> _log;
    private VaultSession? _session;

    public VaultHost(QuantosPaths paths, ClientSettings settings, ILogger<VaultHost> log)
    {
        Paths = paths;
        Settings = settings;
        _log = log;
    }

    public QuantosPaths Paths { get; }
    public ClientSettings Settings { get; }
    public VaultSession? Session => _session;

    /// <summary>Forwards change events from whichever session is open.</summary>
    public event Action<ChangeEvent>? Changed;

    public VaultSession Require() => _session ?? throw new QuantosException("No vault is open. Choose one in the app.", 409);

    public static VaultCheck Check(string path)
    {
        string full;
        try { full = System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'))); }
        catch (Exception ex) { return new VaultCheck(path, false, false, false, false, null, null, ex.Message); }
        var exists = Directory.Exists(full);
        var empty = !exists || !Directory.EnumerateFileSystemEntries(full).Any();
        var looksLikeVault = exists && Directory.Exists(System.IO.Path.Combine(full, "workspaces"));
        var service = Vault.SyncedFolderService(full);
        var writable = CanWrite(exists ? full : NearestExisting(full));
        var warning = service is null ? null :
            $"This folder is inside {service}. Sync services can lock files while Quantos is saving, create conflict copies, and in rare cases corrupt files. A folder that isn't synced is safer.";
        var error = writable ? null : "Quantos can't write to this folder.";

        return new VaultCheck(full, exists, empty, looksLikeVault, writable, service, warning, error);
    }

    public ReconcileReport Open(string path)
    {
        var check = Check(path);
        if (check.Error is not null) throw new QuantosException(check.Error);
        lock (_gate)
        {
            if (_session is not null && Vault.PathEquals(_session.Vault.Root, check.Path)) return _session.LastReconcile ?? _session.Start();
            var session = new VaultSession(check.Path, Paths);
            ReconcileReport report;
            try { report = session.Start(); session.StartWatching(); }
            catch { session.Dispose(); throw; }
            var old = _session;
            if (old is not null) old.Changes.Changed -= Forward;
            _session = session;
            session.Changes.Changed += Forward;
            session.Approvals.Requested += a => Changed?.Invoke(new ChangeEvent(ChangeKind.Approval, null, null, Data: a));
            old?.Dispose();
            Settings.VaultPath = check.Path;
            Settings.RecentVaults.RemoveAll(v => Vault.PathEquals(v, check.Path));
            Settings.RecentVaults.Insert(0, check.Path);
            if (Settings.RecentVaults.Count > 8) Settings.RecentVaults.RemoveRange(8, Settings.RecentVaults.Count - 8);
            Settings.Save(Paths);
            _log.LogInformation("Opened vault {Vault}: scanned {Scanned}, changed {Changed}, added {Added}, removed {Removed}, adopted {Adopted} in {Elapsed}",
                check.Path, report.Scanned, report.Changed, report.Added, report.Removed, report.Adopted, report.Elapsed);
            Changed?.Invoke(new ChangeEvent(ChangeKind.Reindexed, null, null, Data: new { vault = check.Path }));
            return report;
        }
    }

    private void Forward(ChangeEvent e) => Changed?.Invoke(e);

    private static string NearestExisting(string path)
    {
        var dir = new DirectoryInfo(path);
        while (dir is not null && !dir.Exists) dir = dir.Parent;
        return dir?.FullName ?? path;
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            var probe = System.IO.Path.Combine(dir, $".quantos-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        lock (_gate) { _session?.Dispose(); _session = null; }
    }
}
