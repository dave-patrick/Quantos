using Quantos.Core.Cache;
using Quantos.Core.Security;
using Quantos.Core.Storage;

namespace Quantos.Core;

/// <summary>Everything that belongs to one open vault: the lock, cache, write service and reconciler.</summary>
public sealed class VaultSession : IDisposable
{
    private readonly VaultLock? _lock;

    public VaultSession(string vaultPath, QuantosPaths paths, Clock? clock = null, bool takeLock = true)
    {
        Paths = paths;
        paths.EnsureCreated();
        Vault = new Vault(vaultPath);
        Vault.EnsureLayout();
        if (takeLock) _lock = VaultLock.Acquire(Vault);
        Clock = clock ?? new Clock();
        Cache = new CacheIndex(paths.CacheDatabase);
        Cache.BindToVault(Vault.Root);
        Approvals = new ApprovalService(Clock.Provider);
        Permissions = new PermissionService(paths.PermissionsFile, Approvals);
        Changes = new ChangeHub();
        Writes = new WriteService(Vault, Cache, Permissions, Changes, Clock);
        Reconciler = new Reconciler(Writes);
        Config = new ConfigStore(Vault, Permissions, Changes);
        Tasks = new Tasks.TaskService(Writes);
        Capture = new CaptureService(Writes, Tasks);
        Notifications = new NotificationStore(Vault);
        Reminders = new ReminderService(Writes, Tasks, Notifications);
        Supertags = new Structure.SupertagService(Writes);
        Templates = new Structure.TemplateService(Writes, Supertags);
        Queries = new Structure.QueryService(Writes, Supertags);
        SyncedBlocks = new Structure.SyncedBlockService(Writes);
        Automations = new Extensibility.AutomationService(Writes, Tasks, Notifications);
        Exports = new Export.ExportService(Writes, Queries);
        Attachments = new AttachmentStore(Writes);
        Ai = new Ai.AiService(Writes, paths.Settings, paths.Cache);
        Backups = new Backups.BackupService(Writes, paths.Settings, Path.Combine(paths.ClientData, "backups"));
    }

    public Backups.BackupService Backups { get; }

    public Ai.AiService Ai { get; }

    public AttachmentStore Attachments { get; }

    public Export.ExportService Exports { get; }

    public Extensibility.AutomationService Automations { get; }

    public Structure.SyncedBlockService SyncedBlocks { get; }

    public Structure.SupertagService Supertags { get; }
    public Structure.TemplateService Templates { get; }
    public Structure.QueryService Queries { get; }

    public ConfigStore Config { get; }
    public Tasks.TaskService Tasks { get; }
    public CaptureService Capture { get; }
    public NotificationStore Notifications { get; }
    public ReminderService Reminders { get; }
    public VaultWatcher? Watcher { get; private set; }

    /// <summary>Starts watching for outside edits (after the startup scan).</summary>
    public void StartWatching()
    {
        Watcher ??= new VaultWatcher(Writes);
    }

    public QuantosPaths Paths { get; }
    public Vault Vault { get; }
    public Clock Clock { get; }
    public CacheIndex Cache { get; }
    public ApprovalService Approvals { get; }
    public PermissionService Permissions { get; }
    public ChangeHub Changes { get; }
    public WriteService Writes { get; }
    public Reconciler Reconciler { get; }
    public ReconcileReport? LastReconcile { get; private set; }

    /// <summary>Recovers and reconciles. A reset cache (deleted or version change) is rebuilt in full.</summary>
    public ReconcileReport Start()
    {
        LastReconcile = Reconciler.Run(fullRebuild: Cache.WasReset);
        return LastReconcile;
    }

    public ReconcileReport RebuildCache()
    {
        LastReconcile = Reconciler.Run(fullRebuild: true);
        return LastReconcile;
    }

    /// <summary>Synced-folder warning for the vault location (BUILD-PLAN section 4).</summary>
    public string? SyncedFolderWarning =>
        Vault.SyncedFolderService(Vault.Root) is { } service
            ? $"This vault is inside {service}. Sync services can lock files while Quantos is saving, create conflict copies, and in rare cases corrupt files. Choose a folder that isn't synced, and back up by copying the vault instead."
            : null;

    public void Dispose()
    {
        Watcher?.Dispose();
        Automations.Dispose();
        Ai.Dispose();
        Backups.Dispose();
        Cache.Dispose();
        _lock?.Dispose();
    }
}
