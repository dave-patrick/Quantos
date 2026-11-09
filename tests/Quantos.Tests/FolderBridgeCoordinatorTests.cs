using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Sync;

namespace Quantos.Tests;

public sealed class FolderBridgeCoordinatorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "quantos-tests", Guid.NewGuid().ToString("N"));
    private readonly VaultHost host;
    private readonly FolderBridgeCoordinator bridges;
    private readonly FakeRemote remote = new();
    private readonly string folder;
    public FolderBridgeCoordinatorTests()
    {
        var paths = new QuantosPaths(Path.Combine(root, "client-data")); paths.EnsureCreated();
        host = new(paths, new ClientSettings(), NullLogger<VaultHost>.Instance);
        host.Open(Path.Combine(root, "Vault"));
        folder = Path.Combine(root, "Original"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "note.md"), "# Source note\nOriginal bytes\n");
        Connect();
        bridges = new(host, new FakeFactory(remote));
    }
    private void Connect(string? url = null) => File.WriteAllText(Path.Combine(host.Paths.Settings, "sync.json"),
        JsonSerializer.Serialize(new SyncConnection(url ?? "https://example.invalid", "ENCRYPTED-NOT-RETURNED", "connection", host.Require().Vault.Root), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    private async Task<string> Add()
    {
        await bridges.Add(new(folder, "bridge-tests"), CancellationToken.None);
        return Assert.Single(bridges.Status().Mappings).Id;
    }
    [Fact]
    public async Task Default_workspace_transfers_using_only_the_private_empty_scaffold()
    {
        if (!OperatingSystem.IsWindows()) return;
        await bridges.Add(new(folder, "default"), default);
        var id = Assert.Single(bridges.Status().Mappings).Id;
        await bridges.Start(id, default); await bridges.RunOnce(default);
        Assert.Equal(1, remote.Applied); Assert.Null(Assert.Single(bridges.Status().Mappings).Error);
        await bridges.RunOnce(default);
        Assert.Equal(1, remote.Applied); Assert.Null(Assert.Single(bridges.Status().Mappings).Error);
        Assert.True(Directory.Exists(Path.Combine(host.Require().Vault.Root, "workspaces", "default")));
    }
    [Fact]
    public async Task Save_is_stopped_and_projection_never_returns_saved_credentials()
    {
        if (!OperatingSystem.IsWindows()) return;
        var original = File.ReadAllBytes(Path.Combine(folder, "note.md"));
        await Add(); await bridges.RunOnce(CancellationToken.None);
        Assert.False(Assert.Single(bridges.Status().Mappings).Enabled);
        Assert.Equal(0, remote.Applied);
        Assert.DoesNotContain("ENCRYPTED", JsonSerializer.Serialize(bridges.Status()));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(folder, "note.md")));
    }
    [Fact]
    public async Task Rejects_relative_unavailable_private_and_overlapping_paths_and_duplicate_destinations()
    {
        if (!OperatingSystem.IsWindows()) return;
        await Assert.ThrowsAsync<QuantosException>(() => bridges.Add(new("relative", "test"), default));
        await Assert.ThrowsAsync<QuantosException>(() => bridges.Add(new(Path.Combine(root, "missing"), "test"), default));
        await Assert.ThrowsAsync<QuantosException>(() => bridges.Add(new(root, "test"), default));
        await Assert.ThrowsAsync<QuantosException>(() => bridges.Add(new(host.Require().Vault.Root, "test"), default));
        await Assert.ThrowsAsync<QuantosException>(() => bridges.Add(new(@"\\?\" + host.Require().Vault.Root, "test"), default));
        await Add();
        var child = Path.Combine(folder, "child"); Directory.CreateDirectory(child);
        await Assert.ThrowsAsync<QuantosException>(() => bridges.Add(new(child, "another"), default));
        var other = Path.Combine(root, "Other"); Directory.CreateDirectory(other);
        await Assert.ThrowsAsync<QuantosException>(() => bridges.Add(new(other, "bridge-tests"), default));
    }
    [Fact]
    public async Task Local_link_must_be_unlinked_without_touching_original_files()
    {
        if (!OperatingSystem.IsWindows()) return;
        host.Require().Vault.RegisterLinkedFolder("local", folder);
        var error = await Assert.ThrowsAsync<QuantosException>(Add);
        Assert.Contains("leaving its files intact", error.Message);
        Assert.True(File.Exists(Path.Combine(folder, "note.md")));
    }
    [Fact]
    public async Task Missing_or_changed_connection_blocks_start_and_archive_allows_fresh_relink()
    {
        if (!OperatingSystem.IsWindows()) return;
        var id = await Add();
        Connect("https://different.invalid");
        await Assert.ThrowsAsync<QuantosException>(() => bridges.Start(id, default));
        await bridges.Stop(id, true, default);
        Assert.Empty(bridges.Status().Mappings);
        var replacement = await Add(); Assert.NotEqual(id, replacement);
        File.Delete(Path.Combine(host.Paths.Settings, "sync.json"));
        await Assert.ThrowsAsync<QuantosException>(() => bridges.Start(replacement, default));
    }
    [Theory]
    [InlineData("stop")]
    [InlineData("archive")]
    [InlineData("disconnect")]
    [InlineData("configure")]
    [InlineData("vault")]
    public async Task Lifecycle_changes_cancel_and_drain_real_inflight_engine_work(string action)
    {
        if (!OperatingSystem.IsWindows()) return;
        var id = await Add(); await bridges.Start(id, default);
        remote.Hold = true;
        var run = bridges.RunOnce(default);
        await remote.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var sync = new SyncCoordinator(host, bridges);
        switch (action)
        {
            case "stop": await bridges.Stop(id, false, default); break;
            case "archive": await bridges.Stop(id, true, default); break;
            case "disconnect": await sync.Disconnect(default); break;
            case "configure": await sync.Configure(new("https://replacement.invalid", new string('x', 40)), default); break;
            case "vault": await Task.Run(() => host.Open(Path.Combine(root, "OtherVault"))); break;
        }
        await run;
        Assert.True(remote.Cancelled); Assert.Equal(0, remote.Applied);
        Assert.All(bridges.Status().Mappings, m => { Assert.False(m.Enabled); Assert.False(m.Busy); });
        remote.Hold = false; await bridges.RunOnce(default); Assert.Equal(0, remote.Applied);
    }
    [Fact]
    public async Task Host_shutdown_drains_transfer_and_retains_enabled_mapping_for_restart()
    {
        if (!OperatingSystem.IsWindows()) return;
        var id = await Add(); await bridges.Start(id, default); remote.Hold = true;
        await bridges.StartAsync(default);
        await remote.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await bridges.StopAsync(default);
        Assert.True(remote.Cancelled); Assert.Equal(0, remote.Applied);
        Assert.True(Assert.Single(bridges.Status().Mappings).Enabled);
        Assert.False(Assert.Single(bridges.Status().Mappings).Busy);
    }
    [Fact]
    public async Task Upload_then_restart_retains_checkpoint_and_archive_preserves_recovery_state()
    {
        if (!OperatingSystem.IsWindows()) return;
        var id = await Add(); await bridges.Start(id, default); await bridges.RunOnce(default);
        Assert.Equal(1, remote.Applied);
        var state = Path.Combine(host.Paths.ClientData, "folder-bridges", id, "sync");
        Assert.True(File.Exists(Path.Combine(state, "checkpoint.json")));
        bridges.Dispose();
        using var restarted = new FolderBridgeCoordinator(host, new FakeFactory(remote));
        Assert.True(Assert.Single(restarted.Status().Mappings).Enabled);
        await restarted.RunOnce(default); Assert.Equal(1, remote.Applied);
        await restarted.Stop(id, true, default);
        Assert.True(File.Exists(Path.Combine(state, "checkpoint.json")));
        Assert.Equal("# Source note\nOriginal bytes\n", File.ReadAllText(Path.Combine(folder, "note.md")));
    }
    public void Dispose()
    {
        bridges.Dispose(); host.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(root, true); } catch (IOException) { }
    }
    private sealed class FakeFactory(FakeRemote remote) : FolderBridgeRemoteFactory
    {
        public override ISyncRemote Open(FolderBridgeMapping mapping, string state) => remote;
    }
    private sealed class FakeRemote : ISyncRemote
    {
        public int Applied;
        public bool Hold, Cancelled;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<string, byte[]> files = [];
        public async Task<IReadOnlyList<SyncFile>> Manifest(CancellationToken ct)
        {
            if (Hold)
            {
                Entered.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { Cancelled = true; throw; }
            }
            return files.Select(p => new SyncFile(p.Key, SyncFiles.Hash(p.Value), p.Value.Length)).ToList();
        }
        public Task<byte[]?> Read(string path, string? expectedHash, CancellationToken ct) => Task.FromResult(files.GetValueOrDefault(path));
        public Task<bool> Apply(SyncMutation mutation, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Applied++;
            if (mutation.Content is { } bytes) files[mutation.Path] = bytes; else files.Remove(mutation.Path);
            return Task.FromResult(true);
        }
    }
}
