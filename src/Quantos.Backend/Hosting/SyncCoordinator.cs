using System.Text.Json;
using Quantos.Core;
using Quantos.Core.Ai;
using Quantos.Core.Storage;
using Quantos.Core.Sync;

namespace Quantos.Backend.Hosting;

public sealed record SyncConnection(string Url, string ProtectedToken, string Id, string Vault);
public sealed record SyncConnectionBody(string Url, string? Token);
public sealed record SyncStatus(string? Url, bool Busy, DateTimeOffset? LastSynced, string? Error, IReadOnlyList<SyncConflict> Conflicts);

/// <summary>The UI always talks to its local backend. Network loss only pauses this worker.</summary>
public sealed class SyncCoordinator(VaultHost host) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1);
    private DateTimeOffset? lastSynced;
    private string? error;
    private volatile bool busy;
    private CancellationTokenSource? activeSync;
    private void CancelActive()
    {
        try { activeSync?.Cancel(); } catch (ObjectDisposedException) { }
    }
    private async Task EnterForChange(CancellationToken ct)
    {
        CancelActive();
        // A queued/manual run can acquire the gate just after the first cancellation.
        // Cancel that run too rather than waiting for a disconnected server's timeout.
        while (!await gate.WaitAsync(TimeSpan.FromMilliseconds(100), ct)) CancelActive();
    }
    private string ConfigFile => Path.Combine(host.Paths.Settings, "sync.json");
    private SyncConnection? Load() => File.Exists(ConfigFile) ? JsonSerializer.Deserialize<SyncConnection>(File.ReadAllText(ConfigFile), Json) : null;
    private string State(SyncConnection connection) => Path.Combine(host.Paths.ClientData, "sync", connection.Id);

    private (HttpSyncRemote Remote, SyncEngine Engine) Open(SyncConnection config)
    {
        var session = host.Require();
        if (!Vault.PathEquals(session.Vault.Root, config.Vault)) throw new QuantosException("Sync is configured for another local vault. Open it to resume syncing.");
        var token = AiSettingsStore.KeyProtector.Unprotect(config.ProtectedToken) ?? throw new QuantosException("The server key could not be unlocked. Enter it again.");
        var state = State(config);
        Directory.CreateDirectory(state);
        var remote = new HttpSyncRemote(config.Url, token, Path.Combine(state, "server-id"));
        return (remote, new SyncEngine(session.Writes, state, remote));
    }
    public SyncStatus Status()
    {
        var config = Load();
        if (config is null) return new(null, busy, lastSynced, error, []);
        try
        {
            var (remote, engine) = Open(config);
            using (remote) return new(config.Url, busy, lastSynced, error, engine.Conflicts());
        }
        catch (Exception ex) { return new(config.Url, busy, lastSynced, ex.Message, []); }
    }
    public async Task Configure(SyncConnectionBody body, CancellationToken ct)
    {
        await EnterForChange(ct);
        try
        {
            var old = Load();
            var token = !string.IsNullOrWhiteSpace(body.Token) ? body.Token.Trim() : old is null ? null : AiSettingsStore.KeyProtector.Unprotect(old.ProtectedToken);
            if (token is null || token.Length < 32) throw new QuantosException("Enter the server key (at least 32 characters).");
            using var remote = new HttpSyncRemote(body.Url, token, Path.Combine(host.Paths.ClientData, "sync", "validation-unused"));
            var config = new SyncConnection(body.Url.TrimEnd('/'), AiSettingsStore.KeyProtector.Protect(token), old?.Id ?? Guid.NewGuid().ToString("N"), host.Require().Vault.Root);
            // Changing the address retains the checkpoint and pinned identity: moving servers is safe.
            if (old is not null && !Vault.PathEquals(old.Vault, config.Vault)) throw new QuantosException("Disconnect the other vault before connecting this one.");
            AtomicFile.WriteAllText(ConfigFile, JsonSerializer.Serialize(config, Json));
            error = null;
        }
        finally { gate.Release(); }
    }
    public async Task Disconnect(CancellationToken ct)
    {
        await EnterForChange(ct);
        try { if (File.Exists(ConfigFile)) File.Delete(ConfigFile); error = null; lastSynced = null; }
        finally { gate.Release(); }
    }
    public async Task<SyncReport?> SyncNow(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        CancellationTokenSource? cancellation = null;
        try
        {
            var config = Load();
            if (config is null) return null;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            activeSync = cancellation;
            busy = true;
            var (remote, engine) = Open(config);
            using (remote)
            {
                var report = await engine.Run(cancellation.Token);
                if (report.Downloaded > 0)
                {
                    host.Require().Reconciler.Run();
                    host.Require().Changes.Publish(new ChangeEvent(ChangeKind.Reindexed, null, null));
                }
                lastSynced = DateTimeOffset.Now; error = null;
                return report;
            }
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true && !ct.IsCancellationRequested) { return null; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { error = "The server did not respond in time. Your local changes are kept."; throw new QuantosException(error, 503); }
        catch (Exception ex) when (ex is not OperationCanceledException) { error = ex.Message; throw; }
        finally { activeSync = null; cancellation?.Dispose(); busy = false; gate.Release(); }
    }
    public async Task Resolve(string id, bool local, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var (remote, engine) = Open(Load() ?? throw new QuantosException("No server is connected."));
            using (remote) await engine.Resolve(id, local, ct);
            host.Require().Reconciler.Run();
            host.Require().Changes.Publish(new ChangeEvent(ChangeKind.Reindexed, null, null));
        }
        finally { gate.Release(); }
    }
    public byte[]? Version(string id, bool local)
    {
        var (remote, engine) = Open(Load() ?? throw new QuantosException("No server is connected."));
        using (remote) return engine.Version(id, local);
    }
    public async Task<object> McpSettings(bool? writeAccess, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var config = Load();
            if (config is null) return new { connected = false };
            var (remote, _) = Open(config);
            using (remote)
            {
                var settings = await remote.McpSettings(writeAccess, ct);
                return new { connected = true, endpoint = config.Url.TrimEnd('/') + "/mcp", settings };
            }
        }
        finally { gate.Release(); }
    }
    public async Task<object> McpConnection(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var config = Load() ?? throw new QuantosException("Connect the shared server first.");
            var (remote, _) = Open(config);
            using (remote) return new { url = config.Url.TrimEnd('/') + "/mcp", headers = new { Authorization = "Bearer " + await remote.McpConnectionKey(ct) } };
        }
        finally { gate.Release(); }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SyncNow(stoppingToken); }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is not OperationCanceledException) { error = ex.Message; }
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
