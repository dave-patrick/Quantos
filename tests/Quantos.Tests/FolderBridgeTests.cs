using System.Text;
using Quantos.Core;
using Quantos.Core.Security;
using Quantos.Core.Sync;

namespace Quantos.Tests;

public sealed class FolderBridgeTests
{
    private sealed class Remote(TestVault server) : ISyncRemote
    {
        public Action? Applied;
        public Task<IReadOnlyList<SyncFile>> Manifest(CancellationToken ct) => Task.FromResult(SyncFiles.Manifest(server.Writes));
        public Task<byte[]?> Read(string path, string? expectedHash, CancellationToken ct) => Task.FromResult(File.Exists(server.Abs(path)) ? File.ReadAllBytes(server.Abs(path)) : null);
        public Task<bool> Apply(SyncMutation m, CancellationToken ct)
        { var result = server.Writes.ApplySyncFile(m, Path.Combine(server.Root, "recovery"), Actor.User); if(result) Applied?.Invoke(); return Task.FromResult(result); }
    }
    private static string Source(TestVault local)
    { var folder = Path.Combine(local.Root, "external"); Directory.CreateDirectory(folder); return folder; }
    private static FolderBridge Bridge(TestVault local, TestVault server, string folder) => new(local.Session, folder, "videos", Path.Combine(local.Root, "bridge-state"), new Remote(server));

    [Fact]
    public async Task External_and_server_edits_round_trip_exactly_without_syncing_other_files()
    {
        using var local = new TestVault(false); using var server = new TestVault();
        var folder = Source(local); var file = Path.Combine(folder, "note.md");
        var original = Encoding.UTF8.GetBytes("---\ncustom: [leave, unchanged]\n---\nExternal bytes\r\n");
        File.WriteAllBytes(file, original); File.WriteAllText(Path.Combine(folder, "settings.json"), "private");
        var bridge = Bridge(local, server, folder);
        await bridge.Run();
        Assert.Equal(original, File.ReadAllBytes(server.Abs("workspaces/videos/note.md")));
        Assert.False(File.Exists(server.Abs("workspaces/videos/settings.json")));
        local.Restart();
        bridge = Bridge(local, server, folder); // persisted checkpoint survives process restart
        var next = Encoding.UTF8.GetBytes("edited in Quantos\n");
        server.Writes.ApplySyncFile(new("workspaces/videos/note.md", SyncFiles.Hash(original), next), Path.Combine(server.Root,"recovery"), Actor.User);
        await bridge.Run(); Assert.Equal(next, File.ReadAllBytes(file));
        server.Writes.ApplySyncFile(new("workspaces/videos/daily/2026/2026-10-25.md", null, Encoding.UTF8.GetBytes("daily capture")), Path.Combine(server.Root,"recovery"), Actor.User);
        await bridge.Run();
        Assert.Equal("daily capture", File.ReadAllText(Path.Combine(folder,"daily/2026/2026-10-25.md")));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(local.Root, "bridge-state/recovery"), "*.bin"));
    }

    [Fact]
    public async Task Concurrent_external_and_server_changes_preserve_both_versions()
    {
        using var local = new TestVault(false); using var server = new TestVault();
        var folder = Source(local); var file = Path.Combine(folder, "note.md"); File.WriteAllText(file, "base");
        var bridge = Bridge(local, server, folder); await bridge.Run();
        File.WriteAllText(file, "outside writer");
        server.Writes.ApplySyncFile(new("workspaces/videos/note.md", SyncFiles.Hash(Encoding.UTF8.GetBytes("base")), Encoding.UTF8.GetBytes("phone edit")), Path.Combine(server.Root,"recovery"), Actor.User);
        Assert.Equal(1, (await bridge.Run()).Conflicts);
        Assert.Equal("outside writer", File.ReadAllText(file));
        var conflict = Assert.Single(bridge.Engine.Conflicts());
        Assert.Equal("phone edit", Encoding.UTF8.GetString(bridge.Engine.Version(conflict.Id, false)!));
        await bridge.Engine.Resolve(conflict.Id, false); Assert.Equal("phone edit", File.ReadAllText(file));
    }

    [Fact]
    public async Task Unavailable_empty_or_mass_removed_folders_pause_without_erasing_the_other_copy()
    {
        using var local = new TestVault(false); using var server = new TestVault();
        var folder = Source(local);
        for(var i=0;i<20;i++) File.WriteAllText(Path.Combine(folder,$"note{i}.md"), "original");
        var bridge = Bridge(local, server, folder); await bridge.Run();
        for(var i=0;i<10;i++) File.Delete(Path.Combine(folder,$"note{i}.md"));
        await Assert.ThrowsAsync<QuantosException>(() => bridge.Run());
        Assert.Equal(20, Directory.GetFiles(server.Abs("workspaces/videos"), "*.md").Length);
        Directory.Move(folder, folder + "-offline");
        await Assert.ThrowsAsync<QuantosException>(() => bridge.Run());
        Assert.Throws<QuantosException>(() => local.Writes.ApplySyncFile(new("workspaces/videos/new.md", null, Encoding.UTF8.GetBytes("do not recreate the missing source")), Path.Combine(local.Root,"recovery"), Actor.User));
        Assert.False(Directory.Exists(folder));
        Directory.CreateDirectory(folder);
        await Assert.ThrowsAsync<QuantosException>(() => bridge.Run());
        Assert.Equal(20, Directory.GetFiles(server.Abs("workspaces/videos"), "*.md").Length);
    }

    [Fact]
    public async Task Outside_rename_keeps_the_note_identity_even_when_the_server_indexes_every_step()
    {
        using var local = new TestVault(false); using var server = new TestVault();
        var folder = Source(local); var id = Ulid.New();
        File.WriteAllText(Path.Combine(folder,"z-last.md"), $"---\nid: {id}\ntitle: Rename check\ntype: note\nworkspace: videos\n---\nkeep identity");
        var remote = new Remote(server) { Applied = () => server.Session.Reconciler.Run() };
        var bridge = new FolderBridge(local.Session, folder, "videos", Path.Combine(local.Root,"bridge-state"), remote);
        await bridge.Run();
        File.Move(Path.Combine(folder,"z-last.md"), Path.Combine(folder,"a-first.md"));
        await bridge.Run();
        Assert.Equal("workspaces/videos/a-first.md", server.Session.Cache.PathOf(id));
        Assert.Empty(bridge.Engine.Conflicts());
        Assert.Single(SyncFiles.Manifest(server.Writes), f => f.Path.EndsWith(".md"));
    }

    [Fact]
    public void Folder_mode_rejects_traversal_other_workspaces_and_non_markdown()
    {
        using var local = new TestVault(false); using var server = new TestVault(); var folder = Source(local);
        _ = Bridge(local, server, folder);
        foreach(var path in new[]{"workspaces/videos/../escape.md", "workspaces/default/nodes/note.md", "workspaces/videos/secret.json", "linked/videos/note.md"})
            Assert.Throws<QuantosException>(() => SyncFiles.Validate(local.Session.Vault, path));
    }
}
