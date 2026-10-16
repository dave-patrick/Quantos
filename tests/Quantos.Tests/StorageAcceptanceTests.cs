using Quantos.Core;
using Quantos.Core.Security;
using Quantos.Core.Storage;

namespace Quantos.Tests;

/// <summary>BUILD-PLAN section 13, "Storage and recovery", plus the Stage 1 exit criteria.</summary>
public class StorageAcceptanceTests
{
    private static Node NewNote(TestVault v, string title, string body = "Hello") =>
        v.Writes.Create(new CreateNodeRequest { Title = title, Body = body }, Actor.User);

    [Fact]
    public void Create_edit_restart_and_read_back()
    {
        using var v = new TestVault();
        var node = NewNote(v, "First note");
        Assert.Equal("workspaces/default/nodes/first_note.md", node.Path);
        var saved = v.Writes.Save(node.Id, node.Content.Replace("Hello", "Hello again"), node.Hash, Actor.User);
        Assert.Equal(SaveStatus.Saved, saved.Status);
        v.Restart();
        var reread = v.Writes.Require(node.Id);
        Assert.Contains("Hello again", reread.Body);
        Assert.Equal("First note", reread.Title);
    }

    [Fact] // 13.1
    public void Delete_the_cache_and_rebuild_all_derived_data()
    {
        using var v = new TestVault();
        var a = NewNote(v, "Alpha");
        var b = NewNote(v, "Beta", "Links to [[Alpha]]");
        Assert.Contains($"[[{a.Id}|Alpha]]", b.Body);
        v.Session.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.Combine(v.ClientData, "cache"))) File.Delete(f);
        var report = v.Restart();
        Assert.Equal(2, report.Added);
        Assert.Equal(2, v.Session.Cache.Count());
        Assert.Single(v.Session.Cache.Backlinks(a.Id));
        Assert.Single(v.Session.Cache.Search("alpha"), h => h.Id == a.Id);
    }

    [Fact] // 13.2
    public void An_interrupted_save_leaves_the_old_file_intact()
    {
        using var v = new TestVault();
        var node = NewNote(v, "Safe");
        var abs = v.Abs(node.Path);
        // Simulate a crash after the temp file was written but before it replaced the original.
        File.WriteAllText(abs + AtomicFile.TempSuffix, "partial garbage");
        var report = v.Restart();
        Assert.Equal(1, report.TempFilesRemoved);
        Assert.False(File.Exists(abs + AtomicFile.TempSuffix));
        Assert.Equal(node.Content, File.ReadAllText(abs));
    }

    [Fact] // 13.3
    public void An_interrupted_multi_file_rename_is_completed_on_restart()
    {
        using var v = new TestVault();
        var target = NewNote(v, "Old name");
        var r1 = NewNote(v, "Ref one", "See [[Old name]]");
        var r2 = NewNote(v, "Ref two", "Also [[Old name]]");
        v.Writes.Journal.CrashAfterSteps = 2; // title written and file moved; references not yet rewritten
        Assert.Throws<SimulatedCrashException>(() => v.Writes.Rename(target.Id, "New name", Actor.User));
        var report = v.Restart();
        Assert.Single(report.Recovered, r => r.Outcome == "completed");
        var renamed = v.Writes.Require(target.Id);
        Assert.Equal("New name", renamed.Title);
        Assert.EndsWith("new_name.md", renamed.Path);
        Assert.Contains($"[[{target.Id}|New name]]", v.Writes.Require(r1.Id).Body);
        Assert.Contains($"[[{target.Id}|New name]]", v.Writes.Require(r2.Id).Body);
    }

    [Fact]
    public void An_interrupted_rename_rolls_back_if_a_file_was_changed_meanwhile()
    {
        using var v = new TestVault();
        var target = NewNote(v, "Keep");
        var r1 = NewNote(v, "Ref", "See [[Keep]]");
        v.Writes.Journal.CrashAfterSteps = 2;
        Assert.Throws<SimulatedCrashException>(() => v.Writes.Rename(target.Id, "Changed", Actor.User));
        File.AppendAllText(v.Abs(r1.Path), "\nedited by another tool");
        var report = v.Restart();
        Assert.Contains(report.Recovered, r => r.Outcome == "rolled back");
        var node = v.Writes.Require(target.Id);
        Assert.Equal("Keep", node.Title);
        Assert.EndsWith("keep.md", node.Path);
        Assert.Contains("edited by another tool", v.Writes.Require(r1.Id).Body);
    }

    [Fact] // 13.4
    public void Restoring_into_a_taken_name_picks_a_free_one()
    {
        using var v = new TestVault();
        var original = NewNote(v, "Report");
        var entry = v.Writes.Trash(original.Id, Actor.User);
        var replacement = NewNote(v, "Report");
        Assert.Equal(original.Path, replacement.Path);
        var restored = v.Writes.Restore(entry.EntryId, Actor.User);
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal("workspaces/default/nodes/report_2.md", restored.Path);
        Assert.Empty(v.Writes.TrashBin.List());
    }

    [Fact] // 13.5 (storage half; task placeholders are covered in Stage 3)
    public void Restoring_a_referenced_node_reconnects_the_same_identity()
    {
        using var v = new TestVault();
        var task = v.Writes.Create(new CreateNodeRequest { Title = "Ship it", Type = NodeTypes.Task }, Actor.User);
        var n1 = NewNote(v, "Daily A", "Doing [[Ship it]]");
        var n2 = NewNote(v, "Daily B", "Also [[Ship it]]");
        var entry = v.Writes.Trash(task.Id, Actor.User);
        Assert.Equal(2, entry.Referrers.Count);
        Assert.Null(v.Session.Cache.PathOf(task.Id));
        v.Writes.Restore(entry.EntryId, Actor.User);
        Assert.Equal(2, v.Session.Cache.Backlinks(task.Id).Count);
        Assert.Contains($"[[{task.Id}|Ship it]]", v.Writes.Require(n1.Id).Body);
        Assert.Contains($"[[{task.Id}|Ship it]]", v.Writes.Require(n2.Id).Body);
    }

    [Fact] // 13.6
    public void Trash_never_expires()
    {
        using var v = new TestVault();
        var node = NewNote(v, "Old");
        v.Writes.Trash(node.Id, Actor.User);
        // Age the entry by years; nothing purges it.
        foreach (var dir in Directory.GetDirectories(Path.Combine(v.VaultPath, ".trash")))
            Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow.AddYears(-10));
        v.Restart();
        Assert.Single(v.Writes.TrashBin.List());
    }

    [Fact]
    public void Permanent_delete_requires_an_approval_from_the_windows_client()
    {
        using var v = new TestVault();
        var node = NewNote(v, "Gone");
        var entry = v.Writes.Trash(node.Id, Actor.User);
        var ex = Assert.Throws<ApprovalRequiredException>(() => v.Writes.DeletePermanently(entry.EntryId, Actor.Browser, null));
        Assert.Single(v.Writes.TrashBin.List());
        v.Session.Approvals.Decide(ex.Approval.Id, approve: true);
        v.Writes.DeletePermanently(entry.EntryId, Actor.Browser, ex.Approval.Id);
        Assert.Empty(v.Writes.TrashBin.List());
        // Approvals are single use.
        var again = v.Writes.Trash(NewNote(v, "Gone 2").Id, Actor.User);
        Assert.Throws<ApprovalRequiredException>(() => v.Writes.DeletePermanently(again.EntryId, Actor.User, ex.Approval.Id));
    }

    [Fact] // 13.7
    public void History_is_still_readable_after_the_node_moves()
    {
        using var v = new TestVault();
        var node = NewNote(v, "Wanderer", "version one");
        var saved = v.Writes.Save(node.Id, node.Content.Replace("version one", "version two"), node.Hash, Actor.User).Node!;
        v.Writes.MoveToWorkspace(node.Id, "work", Actor.User);
        v.Writes.Rename(node.Id, "Settled", Actor.User);
        var versions = v.Writes.History.List(node.Id);
        Assert.Contains(versions, ver => v.Writes.History.Read(node.Id, ver.Version)!.Contains("version one"));
        var moved = v.Writes.Require(node.Id);
        Assert.Equal("workspaces/work/nodes/settled.md", moved.Path);
        var rolled = v.Writes.Rollback(node.Id, versions.Last().Version, Actor.User);
        Assert.Contains("version one", rolled.Body);
        _ = saved;
    }

    [Fact] // 13.8
    public void The_cache_does_not_exist_inside_the_vault()
    {
        using var v = new TestVault();
        NewNote(v, "Anything");
        Assert.Empty(Directory.GetFiles(v.VaultPath, "*.sqlite*", SearchOption.AllDirectories));
        Assert.True(File.Exists(Path.Combine(v.ClientData, "cache", "cache.sqlite")));
    }

    [Fact] // 13.9
    public void A_onedrive_vault_path_shows_the_warning()
    {
        Assert.Equal("OneDrive", Vault.SyncedFolderService(@"C:\Users\me\OneDrive - KPMG\Quantos\Vault"));
        Assert.Equal("Dropbox", Vault.SyncedFolderService(@"D:\Dropbox\Vault"));
        Assert.Null(Vault.SyncedFolderService(@"C:\Users\me\Quantos\Vault"));
    }

    [Fact] // 13.10
    public void Restore_from_a_manual_backup_and_rebuild_the_cache()
    {
        using var v = new TestVault();
        var keep = NewNote(v, "Precious", "irreplaceable");
        v.Session.Dispose();
        var backup = Path.Combine(v.Root, "Backup");
        CopyDir(v.VaultPath, backup);
        Directory.Delete(v.VaultPath, true);
        CopyDir(backup, v.VaultPath);
        var report = v.Restart();
        Assert.Equal(1, report.Scanned);
        v.Session.RebuildCache();
        Assert.Contains("irreplaceable", v.Writes.Require(keep.Id).Body);
    }

    [Fact]
    public void Edits_made_outside_the_app_while_stopped_are_picked_up_with_history()
    {
        using var v = new TestVault();
        var node = NewNote(v, "External", "app version");
        v.Session.Dispose();
        File.WriteAllText(v.Abs(node.Path), node.Content.Replace("app version", "notepad version"));
        File.WriteAllText(v.Abs("workspaces/default/nodes/dropped_in.md"), "# Plain markdown\nNo frontmatter.\n");
        var report = v.Restart();
        Assert.Equal(1, report.Changed);
        Assert.Equal(1, report.Adopted);
        Assert.Contains("notepad version", v.Writes.Require(node.Id).Body);
        var versions = v.Writes.History.List(node.Id).Select(ver => v.Writes.History.Read(node.Id, ver.Version)!).ToList();
        Assert.Contains(versions, c => c.Contains("app version"));
        Assert.Contains(versions, c => c.Contains("notepad version"));
        var adopted = v.Session.Cache.FindByTitle("dropped_in", null).Single();
        Assert.StartsWith("---\nid: ", File.ReadAllText(v.Abs(adopted.Path)));
        Assert.EndsWith("# Plain markdown\nNo frontmatter.\n", File.ReadAllText(v.Abs(adopted.Path)));
    }

    [Fact]
    public void A_copied_file_with_a_duplicate_id_gets_its_own_identity()
    {
        using var v = new TestVault();
        var node = NewNote(v, "Original");
        v.Session.Dispose();
        File.Copy(v.Abs(node.Path), v.Abs("workspaces/default/nodes/original_copy.md"));
        var report = v.Restart();
        Assert.Equal(1, report.Adopted);
        Assert.Equal(node.Path, v.Session.Cache.PathOf(node.Id));
        Assert.Equal(2, v.Session.Cache.Count());
    }

    [Fact]
    public void A_stale_base_hash_is_a_conflict_and_nothing_is_overwritten()
    {
        using var v = new TestVault();
        var node = NewNote(v, "Contested", "start");
        File.WriteAllText(v.Abs(node.Path), node.Content.Replace("start", "changed on disk"));
        var result = v.Writes.Save(node.Id, node.Content.Replace("start", "changed in app"), node.Hash, Actor.User);
        Assert.Equal(SaveStatus.Conflict, result.Status);
        Assert.Contains("changed on disk", result.DiskContent);
        Assert.Contains("changed on disk", File.ReadAllText(v.Abs(node.Path)));
    }

    [Fact]
    public void Unknown_yaml_and_unsupported_markdown_survive_an_app_save()
    {
        using var v = new TestVault();
        var node = NewNote(v, "Mixed");
        var custom = node.Content.Replace("schema: 1\n", "schema: 1\nweird_key: { a: [1, 2] }  # keep me\n") + "\n%% obsidian comment %%\n:::callout\n";
        File.WriteAllText(v.Abs(node.Path), custom);
        v.Restart();
        var current = v.Writes.Require(node.Id);
        var result = v.Writes.UpdateFields(node.Id, current.Hash, new Dictionary<string, object?> { ["status"] = "draft" }, null, Actor.User);
        Assert.Equal(SaveStatus.Saved, result.Status);
        var text = File.ReadAllText(v.Abs(node.Path));
        Assert.Contains("weird_key: { a: [1, 2] }  # keep me\n", text);
        Assert.Contains("%% obsidian comment %%\n:::callout\n", text);
        Assert.Contains("status: draft\n", text);
    }

    [Fact]
    public void A_second_backend_cannot_take_the_same_vault()
    {
        using var v = new TestVault();
        Assert.Throws<VaultLockedException>(() => new VaultSession(v.VaultPath, new QuantosPaths(Path.Combine(v.Root, "other-client"))));
    }

    [Fact]
    public void Plugins_are_denied_and_safe_mode_blocks_them_entirely()
    {
        using var v = new TestVault();
        var plugin = new Actor(ActorKind.Plugin, "sample");
        Assert.Throws<PermissionDeniedException>(() => v.Writes.Create(new CreateNodeRequest { Title = "x" }, plugin));
        v.Session.Permissions.Set(ActorKind.Plugin, "sample", [Permission.Read, Permission.CreateEdit]);
        v.Writes.Create(new CreateNodeRequest { Title = "x" }, plugin);
        v.Session.Permissions.SafeMode = true;
        Assert.Throws<PermissionDeniedException>(() => v.Writes.Create(new CreateNodeRequest { Title = "y" }, plugin));
    }

    private static void CopyDir(string from, string to)
    {
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(from, to));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == "backend.lock") continue;
            File.Copy(file, file.Replace(from, to));
        }
    }

    [Fact] // Broken YAML from an outside edit keeps the note visible, read-only, and fixable in source mode
    public void A_typo_in_frontmatter_keeps_the_note_and_its_links()
    {
        using var v = new TestVault();
        var target = v.Writes.Create(new CreateNodeRequest { Title = "Target", Body = "Body stays.\n" }, Actor.User);
        var linker = v.Writes.Create(new CreateNodeRequest { Title = "Linker", Body = $"See [[{target.Id}|Target]].\n" }, Actor.User);
        var broken = target.Content.Replace("type: note", "type: note\ntags: [unclosed");
        File.WriteAllText(v.Abs(target.Path), broken);

        var report = v.Restart();
        var node = v.Writes.Require(target.Id);
        Assert.True(node.ReadOnly);
        Assert.Equal("Target", node.Title);
        Assert.Contains(report.Diagnostics, d => d.Code == "yaml" && d.Id == target.Id);
        Assert.Single(v.Session.Cache.Backlinks(target.Id));

        // Fixing it in source mode saves and lifts read-only.
        var fixedText = broken.Replace("tags: [unclosed", "tags: [fixed]");
        var result = v.Writes.Save(target.Id, fixedText, node.Hash, Actor.User);
        Assert.Equal(SaveStatus.Saved, result.Status);
        Assert.False(v.Writes.Require(target.Id).ReadOnly);
        Assert.Equal(["fixed"], v.Writes.Require(target.Id).Tags);
        Assert.NotNull(linker);
    }
}