using System.Text.Json;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Quantos.Core;
using Quantos.Core.Ai;
using Quantos.Core.Storage;
using Quantos.Core.Sync;

namespace Quantos.Backend.Hosting;

public sealed record FolderBridgeBody(string Folder, string Workspace);
public sealed record FolderBridgeMapping(string Id, string Folder, string Workspace, SyncConnection Connection, bool Enabled = false, bool Archived = false);
public sealed record FolderBridgeState(string Id, string Folder, string Workspace, bool Enabled, bool Busy, DateTimeOffset? LastSynced, string? Error, SyncReport? Counts);
public sealed record FolderBridgesStatus(bool Supported, bool Connected, string[] Workspaces, FolderBridgeState[] Mappings);

public class FolderBridgeRemoteFactory
{
    public virtual ISyncRemote Open(FolderBridgeMapping mapping, string state)
    {
        var token = AiSettingsStore.KeyProtector.Unprotect(mapping.Connection.ProtectedToken) ?? throw new QuantosException("The saved server key cannot be unlocked.");
        return new HttpSyncRemote(mapping.Connection.Url, token, Path.Combine(state, "server-id"));
    }
}

/// <summary>Opt-in folder transfers run only while this local backend is open. No source reconciliation.</summary>
public sealed class FolderBridgeCoordinator : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly VaultHost host;
    private readonly FolderBridgeRemoteFactory remotes;
    private readonly SemaphoreSlim operations = new(1), cycle = new(1);
    private readonly Lock settingsGate = new();
    private readonly Dictionary<string, FolderBridgeState> states = [];
    private CancellationTokenSource? active;
    private string Config => Path.Combine(host.Paths.Settings, "folder-bridges.json");
    private string Root => Path.Combine(host.Paths.ClientData, "folder-bridges");
    public FolderBridgeCoordinator(VaultHost host, FolderBridgeRemoteFactory? remotes = null)
    {
        this.host = host;
        this.remotes = remotes ?? new();
        host.Changing += BeforeVaultChange;
    }
    private IDisposable BeforeVaultChange() => PauseForChange(CancellationToken.None).GetAwaiter().GetResult();
    private List<FolderBridgeMapping> Load() => File.Exists(Config) ? JsonSerializer.Deserialize<List<FolderBridgeMapping>>(File.ReadAllText(Config), Json) ?? [] : [];
    private void Save(List<FolderBridgeMapping> mappings) => AtomicFile.WriteAllText(Config, JsonSerializer.Serialize(mappings, Json));
    private SyncConnection Connection()
    {
        var file = Path.Combine(host.Paths.Settings, "sync.json");
        var config = File.Exists(file) ? JsonSerializer.Deserialize<SyncConnection>(File.ReadAllText(file), Json) : null;
        if (config is null || !Vault.PathEquals(config.Vault, host.Require().Vault.Root))
            throw new QuantosException("Connect this vault to the shared server first.", 409);
        return config;
    }
    private static void Supported()
    {
        if (!OperatingSystem.IsWindows()) throw new QuantosException("Folder bridges run on Windows while Quantos is open.", 409);
    }
    public FolderBridgesStatus Status()
    {
        var session = host.Session;
        bool connected;
        try { Connection(); connected = true; } catch (QuantosException) { connected = false; }
        lock (settingsGate)
            return new(OperatingSystem.IsWindows(), connected, session?.Vault.WorkspaceNames().ToArray() ?? [],
                Load().Where(m => !m.Archived).Select(m => states.GetValueOrDefault(m.Id) is { } s
                    ? s with { Enabled = m.Enabled } : new(m.Id, m.Folder, m.Workspace, m.Enabled, false, null, null, null)).ToArray());
    }
    public static bool Overlaps(string a, string b)
    {
        a = Path.TrimEndingDirectorySeparator(CanonicalFolder(a));
        b = Path.TrimEndingDirectorySeparator(CanonicalFolder(b));
        return Vault.PathEquals(a, b) || a.StartsWith(b.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || b.StartsWith(a.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);
    private static string CanonicalFolder(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new QuantosException("Use a normal drive or network folder path, without a Windows device prefix.");
        var full = Path.GetFullPath(path);
        if (!OperatingSystem.IsWindows() || !Directory.Exists(full)) return full;
        // A directory handle resolves 8.3 names, SUBST drives and mapped-drive aliases.
        using var handle = CreateFile(full, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new QuantosException("The folder's canonical path could not be resolved.");
        var buffer = new StringBuilder(32768);
        var size = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (size == 0 || size >= buffer.Capacity) throw new QuantosException("The folder's canonical path could not be resolved.");
        var result = buffer.ToString();
        return result.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + result[8..]
            : result.StartsWith(@"\\?\", StringComparison.Ordinal) ? result[4..] : result;
    }
    private void Validate(string folder, string workspace, IEnumerable<FolderBridgeMapping> mappings)
    {
        Vault.ValidateWorkspaceName(workspace);
        if (!Path.IsPathFullyQualified(folder) || !Directory.Exists(folder)) throw new QuantosException("Choose an existing absolute folder path.");
        for (DirectoryInfo? part = new(folder); part is not null; part = part.Parent)
            if (part.LinkTarget is not null || (part.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new QuantosException("Choose a folder without symbolic links or junctions in its path.");
        var vault = host.Require().Vault;
        if (Overlaps(folder, vault.Root) || Overlaps(folder, host.Paths.ClientData))
            throw new QuantosException("Choose a source outside the Quantos vault and private app data.");
        if (vault.IsLinked(workspace) || vault.LinkedRoots.Values.Any(p => Overlaps(folder, p)))
            throw new QuantosException("Unlink the existing local Quantos folder link first, leaving its files intact. Then use the shared workspace to avoid indexing the same notes twice.", 409);
        if (mappings.Any(m => !m.Archived && (Overlaps(folder, m.Folder) || m.Workspace == workspace)))
            throw new QuantosException("That folder or workspace already has a bridge. Remove its link before creating a replacement.", 409);
        CheckExternal(folder, workspace);
    }
    private static void CheckExternal(string folder, string workspace)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Apps", "QuantosFolderBridge");
        if (!Directory.Exists(root)) return;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var file = Path.Combine(directory, "bridge-config.json");
            if (!File.Exists(file)) continue;
            using var config = JsonDocument.Parse(File.ReadAllText(file));
            var value = config.RootElement;
            if ((value.TryGetProperty("Folder", out var source) && source.GetString() is { } path && Overlaps(folder, path))
                || (value.TryGetProperty("Workspace", out var target) && target.GetString() == workspace))
                throw new QuantosException("An externally installed folder bridge already uses this folder or workspace. Stop and migrate that installation before linking here; its state and credentials have been left intact.", 409);
        }
    }
    public async Task Add(FolderBridgeBody body, CancellationToken ct)
    {
        Supported();
        await operations.WaitAsync(ct);
        try
        {
            var connection = Connection();
            var folder = CanonicalFolder(body.Folder);
            // Reject relative inputs before canonicalisation.
            if (!Path.IsPathFullyQualified(body.Folder)) throw new QuantosException("Choose an absolute folder path.");
            lock (settingsGate)
            {
                var mappings = Load();
                Validate(folder, body.Workspace, mappings);
                mappings.Add(new(Guid.NewGuid().ToString("N"), folder, body.Workspace, connection));
                Save(mappings);
            }
        }
        finally { operations.Release(); }
    }
    public async Task Start(string id, CancellationToken ct)
    {
        Supported();
        await operations.WaitAsync(ct);
        try
        {
            var connection = Connection();
            lock (settingsGate)
            {
                var mappings = Load(); var index = Find(mappings, id); var mapping = mappings[index];
                if (mapping.Connection != connection) throw new QuantosException("The shared connection changed. Remove this link and create a new one; recovery data will be kept.", 409);
                Validate(mapping.Folder, mapping.Workspace, mappings.Where(m => m.Id != id));
                mappings[index] = mapping with { Enabled = true }; Save(mappings);
            }
        }
        finally { operations.Release(); }
    }
    private static int Find(List<FolderBridgeMapping> mappings, string id)
    {
        var index = mappings.FindIndex(m => m.Id == id && !m.Archived);
        return index >= 0 ? index : throw new QuantosException("Folder link not found.", 404);
    }
    public Task Stop(string id, bool archive, CancellationToken ct) => Pause(id, archive, ct);
    public Task PauseAll(CancellationToken ct) => Pause(null, false, ct);
    // Hold the mutation gate until the caller has finished replacing its connection/vault.
    public async Task<IDisposable> PauseForChange(CancellationToken ct)
    {
        await operations.WaitAsync(ct);
        try
        {
            lock (settingsGate)
            {
                var mappings = Load();
                if (mappings.Any(m => m.Enabled)) Save(mappings.Select(m => m with { Enabled = false }).ToList());
                active?.Cancel();
            }
            await cycle.WaitAsync(CancellationToken.None); cycle.Release();
            return new Release(operations);
        }
        catch { operations.Release(); throw; }
    }
    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
    private async Task Pause(string? id, bool archive, CancellationToken ct)
    {
        await operations.WaitAsync(ct);
        try
        {
            lock (settingsGate)
            {
                var mappings = Load(); if (id is not null) Find(mappings, id);
                Save(mappings.Select(m => id is null || m.Id == id ? m with { Enabled = false } : m).ToList());
                active?.Cancel();
            }
            // Once disabled is durable, complete the drain even if the HTTP caller goes away.
            await cycle.WaitAsync(CancellationToken.None);
            try
            {
                if (archive) lock (settingsGate)
                {
                    var mappings = Load(); var index = Find(mappings, id!);
                    mappings[index] = mappings[index] with { Archived = true }; Save(mappings);
                }
            }
            finally { cycle.Release(); }
        }
        finally { operations.Release(); }
    }
    public async Task RunOnce(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return;
        SyncConnection? connection;
        try { connection = Connection(); } catch (QuantosException) { connection = null; }
        await cycle.WaitAsync(ct);
        try
        {
            List<FolderBridgeMapping> mappings;
            lock (settingsGate) mappings = Load().Where(m => m.Enabled && !m.Archived).ToList();
            foreach (var mapping in mappings)
            {
                using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
                lock (settingsGate)
                {
                    if (!Load().Any(m => m.Id == mapping.Id && m.Enabled && !m.Archived)) continue;
                    active = cancel;
                    states[mapping.Id] = new(mapping.Id, mapping.Folder, mapping.Workspace, true, true, states.GetValueOrDefault(mapping.Id)?.LastSynced, null, null);
                }
                try
                {
                    if (connection != mapping.Connection) throw new QuantosException("Shared connection unavailable or changed. Stop this link and reconnect before continuing.");
                    // Revalidate host paths and local-link overlap at every cycle; do not create missing source folders.
                    Validate(mapping.Folder, mapping.Workspace, []);
                    var state = Path.Combine(Root, mapping.Id); Directory.CreateDirectory(state);
                    using var single = new FileStream(Path.Combine(state, "bridge.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    using var session = new VaultSession(Path.Combine(state, "bridge-vault"), new QuantosPaths(Path.Combine(state, "client-data")));
                    if (mapping.Workspace == Vault.DefaultWorkspace)
                    {
                        // VaultSession generates an empty default scaffold. This private bridge vault
                        // must instead bind that name to the source; never remove any populated scaffold.
                        var scaffold = Path.Combine(state, "bridge-vault", "workspaces", Vault.DefaultWorkspace);
                        if (Directory.EnumerateFiles(scaffold, "*", SearchOption.AllDirectories).Any())
                            throw new QuantosException("The private default-workspace scaffold contains files. Folder sync is paused for review.");
                        Directory.Delete(scaffold, recursive: true);
                    }
                    var remote = remotes.Open(mapping, state);
                    using var disposeRemote = remote as IDisposable;
                    var bridge = new FolderBridge(session, mapping.Folder, mapping.Workspace, Path.Combine(state, "sync"), remote);
                    var report = await bridge.Run(cancel.Token);
                    lock (settingsGate) states[mapping.Id] = states[mapping.Id] with { LastSynced = DateTimeOffset.Now, Counts = report };
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
                catch (Exception ex) { lock (settingsGate) states[mapping.Id] = states[mapping.Id] with { Error = ex.Message }; }
                finally { lock (settingsGate) { active = null; states[mapping.Id] = states[mapping.Id] with { Busy = false }; } }
            }
        }
        finally { cycle.Release(); }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows()) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnce(stoppingToken); await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
    public override void Dispose() { host.Changing -= BeforeVaultChange; base.Dispose(); }
}
