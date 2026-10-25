using System.Text;
using Quantos.Core;
using Quantos.Core.Security;
using Quantos.Core.Sync;

namespace Quantos.Tests;

public sealed class SyncTests
{
    private sealed class Remote(TestVault server) : ISyncRemote
    {
        public bool Offline;
        public Action? AfterManifest;
        public Task<IReadOnlyList<SyncFile>> Manifest(CancellationToken ct)
        { if (Offline) throw new HttpRequestException("offline"); var result = SyncFiles.Manifest(server.Writes); AfterManifest?.Invoke(); return Task.FromResult(result); }
        public Task<byte[]?> Read(string path, string? expected, CancellationToken ct)
        {
            var bytes = File.Exists(server.Abs(path)) ? File.ReadAllBytes(server.Abs(path)) : null;
            if ((bytes is null ? null : SyncFiles.Hash(bytes)) != expected) throw new QuantosException("changed", 409);
            return Task.FromResult(bytes);
        }
        public Task<bool> Apply(SyncMutation mutation, CancellationToken ct) => Task.FromResult(server.Writes.ApplySyncFile(mutation, Path.Combine(server.ClientData, "sync-recovery"), Actor.User));
    }
    private static SyncEngine Engine(TestVault device, ISyncRemote remote) => new(device.Writes, Path.Combine(device.ClientData, "sync"), remote);
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public async Task Two_devices_keep_editing_offline_and_converge_after_restart()
    {
        using var server = new TestVault(); using var a = new TestVault(); using var b = new TestVault();
        var remote = new Remote(server);
        var note = a.Writes.Create(new() { Title = "Shared note", Body = "first" }, Actor.User);
        await Engine(a, remote).Run(); await Engine(b, remote).Run(); b.Session.Reconciler.Run();
        Assert.Equal("first", b.Writes.Require(note.Id).Body.Trim());
        remote.Offline = true;
        var existing = b.Writes.Require(note.Id);
        b.Writes.UpdateFields(note.Id, existing.Hash, null, null, Actor.User, body: "written offline");
        await Assert.ThrowsAsync<HttpRequestException>(() => Engine(b, remote).Run());
        b.Restart(); remote.Offline = false;
        await Engine(b, remote).Run(); await Engine(a, remote).Run(); a.Session.Reconciler.Run();
        Assert.Equal("written offline", a.Writes.Require(note.Id).Body.Trim());
    }

    [Fact]
    public async Task Concurrent_changes_keep_both_versions_and_require_a_choice()
    {
        using var server = new TestVault(); using var a = new TestVault(); using var b = new TestVault();
        var remote = new Remote(server);
        var note = a.Writes.Create(new() { Title = "Decisions", Body = "base" }, Actor.User);
        await Engine(a, remote).Run(); await Engine(b, remote).Run(); b.Session.Reconciler.Run();
        a.Writes.UpdateFields(note.Id, null, null, null, Actor.User, body: "A changes");
        b.Writes.UpdateFields(note.Id, null, null, null, Actor.User, body: "B changes");
        await Engine(a, remote).Run();
        var engine = Engine(b, remote); var report = await engine.Run();
        Assert.Equal(1, report.Conflicts);
        Assert.Contains("B changes", b.Writes.Require(note.Id).Body);
        var conflict = Assert.Single(engine.Conflicts());
        Assert.Contains("A changes", Encoding.UTF8.GetString(engine.Version(conflict.Id, false)!));
        Assert.Contains("B changes", Encoding.UTF8.GetString(engine.Version(conflict.Id, true)!));
        await engine.Resolve(conflict.Id, keepLocal: false);
        Assert.Empty(engine.Conflicts()); b.Session.Reconciler.Run();
        Assert.Contains("A changes", b.Writes.Require(note.Id).Body);
        Assert.Single(Directory.GetFiles(Path.Combine(b.ClientData, "sync/conflicts"), "*.resolved"));
    }

    [Fact]
    public async Task Delete_versus_edit_is_a_conflict_not_data_loss()
    {
        using var server = new TestVault(); using var a = new TestVault(); using var b = new TestVault();
        var remote = new Remote(server);
        var note = a.Writes.Create(new() { Title = "Keep my changes", Body = "base" }, Actor.User);
        await Engine(a, remote).Run(); await Engine(b, remote).Run(); b.Session.Reconciler.Run();
        a.Writes.Trash(note.Id, Actor.User); await Engine(a, remote).Run();
        b.Writes.UpdateFields(note.Id, null, null, null, Actor.User, body: "unsynced edit");
        var engine = Engine(b, remote); await engine.Run();
        Assert.Contains("unsynced edit", b.Writes.Require(note.Id).Body);
        Assert.Contains(engine.Conflicts(), c => c.Path == note.Path && c.RemoteHash is null);
    }

    [Fact]
    public async Task Attachments_and_unknown_frontmatter_survive_byte_for_byte()
    {
        using var server = new TestVault(); using var a = new TestVault(); using var b = new TestVault();
        var path = "workspaces/default/nodes/exact.md";
        var text = Bytes("---\nid: 01K8BFGFK2JTDP98GW23V7E20H\ntitle: Exact\ntype: note\nworkspace: default\ncustom: {a: [1, 2]} # preserve me\n---\n## Body\n");
        a.Writes.ApplySyncFile(new(path, null, text), Path.Combine(a.ClientData, "recovery"), Actor.User);
        var attachment = new byte[] { 0, 255, 12, 4, 8 };
        a.Writes.ApplySyncFile(new("attachments/ab/test.png", null, attachment), Path.Combine(a.ClientData, "recovery"), Actor.User);
        await Engine(a, new Remote(server)).Run(); await Engine(b, new Remote(server)).Run();
        Assert.Equal(text, File.ReadAllBytes(b.Abs(path)));
        Assert.Equal(attachment, File.ReadAllBytes(b.Abs("attachments/ab/test.png")));
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData(".state/backend.lock")]
    [InlineData("linked/folder/note.md")]
    [InlineData("linked-folders.json")]
    [InlineData("client-data/settings/ai-keys.json")]
    [InlineData("workspaces/default/workspace-state.json")]
    [InlineData("workspaces/../../outside.md")]
    [InlineData("workspaces\\default\\note.md")]
    public void Device_secrets_and_outside_paths_never_sync(string path)
    {
        using var vault = new TestVault();
        Assert.Throws<QuantosException>(() => SyncFiles.Validate(vault.Session.Vault, path));
    }

    [Fact]
    public async Task An_edit_during_download_is_preserved_and_a_changed_review_can_be_resolved()
    {
        using var server = new TestVault(); using var a = new TestVault(); using var b = new TestVault();
        var remote = new Remote(server);
        var note = a.Writes.Create(new() { Title = "Race", Body = "base" }, Actor.User);
        await Engine(a, remote).Run(); await Engine(b, remote).Run(); b.Session.Reconciler.Run();
        a.Writes.UpdateFields(note.Id, null, null, null, Actor.User, body: "server change"); await Engine(a, remote).Run();
        remote.AfterManifest = () => { b.Writes.UpdateFields(note.Id, null, null, null, Actor.User, body: "typed during download"); remote.AfterManifest = null; };
        var engine = Engine(b, remote);
        Assert.Equal(0, (await engine.Run()).Downloaded);
        Assert.Contains("typed during download", b.Writes.Require(note.Id).Body);
        await engine.Run(); var old = Assert.Single(engine.Conflicts());
        b.Writes.UpdateFields(note.Id, null, null, null, Actor.User, body: "one more edit");
        await engine.Run(); var latest = Assert.Single(engine.Conflicts());
        Assert.NotEqual(old.Id, latest.Id);
        await engine.Resolve(latest.Id, true);
        Assert.Empty(engine.Conflicts());
        Assert.Contains("one more edit", File.ReadAllText(server.Abs(note.Path)));
    }

    [Fact]
    public void Conditional_writes_refuse_stale_bases_and_preserve_deleted_bytes()
    {
        using var vault = new TestVault(); var recovery = Path.Combine(vault.ClientData, "recovery");
        var path = "attachments/ab/test.bin"; var content = Bytes("original");
        Assert.True(vault.Writes.ApplySyncFile(new(path, null, content), recovery, Actor.User));
        Assert.False(vault.Writes.ApplySyncFile(new(path, "stale", Bytes("overwrite")), recovery, Actor.User));
        Assert.Equal(content, File.ReadAllBytes(vault.Abs(path)));
        Assert.True(vault.Writes.ApplySyncFile(new(path, SyncFiles.Hash(content), null), recovery, Actor.User));
        Assert.Equal(content, File.ReadAllBytes(Assert.Single(Directory.GetFiles(recovery, "*.bin"))));
    }
}
