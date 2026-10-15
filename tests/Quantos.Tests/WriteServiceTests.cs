using System.Text;
using Quantos.Core;

namespace Quantos.Tests;

/// <summary>Stage 1 "done when": create, edit, restart, restore and rebuild without losing authoritative content.</summary>
public class WriteServiceTests
{
    [Fact]
    public async Task Create_writes_a_readable_markdown_file_with_universal_fields()
    {
        using var v = new TestVault();
        var node = await v.Writes.CreateAsync("default", "Project Kickoff!", "Hello\n");

        Assert.Equal("workspaces/default/nodes/project_kickoff.md", node.Path);
        var text = File.ReadAllText(v.FullPath(node.Path));
        Assert.StartsWith("---\nid: " + node.Header.Id + "\ntitle: \"Project Kickoff!\"\nworkspace: default\nsupertags: []\nschema: 1\n", text);
        Assert.EndsWith("state: active\n---\nHello\n", text);
        Assert.True(Guid.TryParse(node.Header.Id, out var id) && id.Version == 7);
    }

    [Fact]
    public async Task Duplicate_titles_get_distinct_files_and_ids()
    {
        using var v = new TestVault();
        var a = await v.Writes.CreateAsync("default", "Same", "");
        var b = await v.Writes.CreateAsync("default", "Same", "");
        Assert.NotEqual(a.Header.Id, b.Header.Id);
        Assert.Equal("workspaces/default/nodes/same_2.md", b.Path);
    }

    [Fact]
    public async Task Update_with_stale_version_is_rejected_and_changes_nothing()
    {
        using var v = new TestVault();
        var node = await v.Writes.CreateAsync("default", "Note", "v1\n");
        var before = File.ReadAllBytes(v.FullPath(node.Path));

        var ex = await Assert.ThrowsAsync<VersionConflictException>(() => v.Writes.UpdateAsync(node.Header.Id, "deadbeef", null, "v2\n"));
        Assert.Equal(node.Version, ex.CurrentVersion);
        Assert.Equal(before, File.ReadAllBytes(v.FullPath(node.Path)));
    }

    [Fact]
    public async Task Edit_outside_the_app_is_detected_as_a_conflict_and_both_versions_survive()
    {
        using var v = new TestVault();
        var node = await v.Writes.CreateAsync("default", "Note", "original\n");
        var path = v.FullPath(node.Path);
        File.WriteAllText(path, File.ReadAllText(path).Replace("original", "edited in Notepad"));

        await Assert.ThrowsAsync<VersionConflictException>(() => v.Writes.UpdateAsync(node.Header.Id, node.Version, null, "my app edit\n"));
        Assert.Contains("edited in Notepad", File.ReadAllText(path));

        // The app keeps the user's edit as a separate note (what the frontend's "save as copy" does).
        var copy = await v.Writes.CreateAsync("default", "Note (copy)", "my app edit\n");
        Assert.Contains("my app edit", File.ReadAllText(v.FullPath(copy.Path)));
    }

    [Fact]
    public async Task Update_saves_the_previous_version_to_history()
    {
        using var v = new TestVault();
        var n1 = await v.Writes.CreateAsync("default", "Note", "first\n");
        var n2 = await v.Writes.UpdateAsync(n1.Header.Id, n1.Version, "Renamed", "second\n");
        await v.Writes.UpdateAsync(n2.Header.Id, n2.Version, null, "third\n");

        var history = v.Writes.History(n1.Header.Id);
        Assert.Equal(2, history.Count);
        var oldest = v.Writes.ReadHistory(n1.Header.Id, history[^1].Entry);
        Assert.Contains("first", oldest);
        Assert.Equal(n1.Version, NodeDocument.VersionOf(Encoding.UTF8.GetBytes(oldest)));
    }

    [Fact]
    public async Task Interrupted_save_leaves_the_original_intact_and_recovers_the_temp_file()
    {
        using var v = new TestVault();
        var node = await v.Writes.CreateAsync("default", "Note", "safe\n");
        var path = v.FullPath(node.Path);
        var before = File.ReadAllBytes(path);

        // Simulate a crash after the temp file is written but before it replaces the note.
        string? leftover = null;
        v.Writes.BeforeCommit = target =>
        {
            if (target != path) return;
            leftover = Directory.GetFiles(Path.GetDirectoryName(path)!, "*" + WriteService.TempSuffix).Single();
            File.Copy(leftover, leftover + ".crash"); // keep a copy, as a real crash would leave it behind
            throw new IOException("simulated power loss");
        };
        await Assert.ThrowsAsync<IOException>(() => v.Writes.UpdateAsync(node.Header.Id, node.Version, null, "lost?\n"));
        File.Move(leftover! + ".crash", leftover!);
        v.Writes.BeforeCommit = null;

        Assert.Equal(before, File.ReadAllBytes(path));

        // "Restart": recovery moves the temp file aside instead of deleting it.
        v.Writes.RecoverInterruptedWrites();
        Assert.Empty(Directory.GetFiles(v.VaultPath, "*" + WriteService.TempSuffix, SearchOption.AllDirectories));
        var recovered = Directory.GetFiles(Path.Combine(v.Layout.State, "recovered")).Single();
        Assert.Contains("lost?", File.ReadAllText(recovered));
    }

    [Fact]
    public async Task Trash_and_restore_keeps_the_same_node()
    {
        using var v = new TestVault();
        var node = await v.Writes.CreateAsync("default", "Keep me", "content\n");
        var bytes = File.ReadAllBytes(v.FullPath(node.Path));

        var entry = await v.Writes.TrashAsync(node.Header.Id, node.Version);
        Assert.False(File.Exists(v.FullPath(node.Path)));
        Assert.Empty(v.Writes.List("default"));
        Assert.Single(v.Writes.ListTrash());

        var restored = await v.Writes.RestoreAsync(entry.TrashId);
        Assert.False(restored.Renamed);
        Assert.Equal(node.Header.Id, restored.Node.Header.Id);
        Assert.Equal(bytes, File.ReadAllBytes(v.FullPath(node.Path)));
        Assert.Empty(v.Writes.ListTrash());
    }

    [Fact]
    public async Task Restore_into_a_taken_name_uses_a_new_name_and_overwrites_nothing()
    {
        using var v = new TestVault();
        var first = await v.Writes.CreateAsync("default", "Clash", "old\n");
        var entry = await v.Writes.TrashAsync(first.Header.Id, first.Version);
        var replacement = await v.Writes.CreateAsync("default", "Clash", "new\n");
        Assert.Equal(first.Path, replacement.Path);

        var restored = await v.Writes.RestoreAsync(entry.TrashId);
        Assert.True(restored.Renamed);
        Assert.Equal("workspaces/default/nodes/clash_restored_2.md", restored.Node.Path);
        Assert.Contains("new", File.ReadAllText(v.FullPath(replacement.Path)));
        Assert.Contains("old", File.ReadAllText(v.FullPath(restored.Node.Path)));
    }

    [Fact]
    public async Task Trash_never_expires_on_its_own()
    {
        using var v = new TestVault();
        var node = await v.Writes.CreateAsync("default", "Old", "");
        await v.Writes.TrashAsync(node.Header.Id, node.Version);
        v.Writes.RecoverInterruptedWrites();
        await v.Writes.RebuildCacheAsync();
        Assert.Single(v.Writes.ListTrash());
    }

    [Fact]
    public async Task Deleting_the_cache_and_rebuilding_restores_everything()
    {
        using var v = new TestVault();
        var a = await v.Writes.CreateAsync("default", "A", "");
        var b = await v.Writes.CreateAsync("default", "B", "");
        await v.Writes.TrashAsync(b.Header.Id, b.Version);

        v.Cache.DeleteFile();
        var report = await v.Writes.RebuildCacheAsync();

        Assert.Equal(2, report.Indexed);
        Assert.Equal(a.Header.Id, Assert.Single(v.Writes.List("default")).Id);
        Assert.Equal("trashed", v.Cache.Get(b.Header.Id)!.State);
    }

    [Fact]
    public async Task Restart_picks_up_files_added_outside_the_app_and_reports_problems()
    {
        using var v = new TestVault();
        var nodes = Path.Combine(v.Layout.Workspace("default"), "nodes");
        File.WriteAllText(Path.Combine(nodes, "from_outside.md"), "---\nid: 0192f0c4-0000-7000-8000-0000000000aa\ntitle: Outside\n---\nhi\n");
        File.WriteAllText(Path.Combine(nodes, "no_id.md"), "just text\n");
        File.WriteAllText(Path.Combine(nodes, "broken.md"), "---\ntitle: [oops\n---\n");

        var restarted = new WriteService(v.Layout, v.Cache);
        restarted.Start();

        Assert.Contains(restarted.List("default"), n => n.Title == "Outside");
        var problems = restarted.Diagnostics().Problems;
        Assert.Contains(problems, p => p.Path.EndsWith("no_id.md") && p.Problem.Contains("No 'id'"));
        Assert.Contains(problems, p => p.Path.EndsWith("broken.md") && p.Problem.Contains("valid YAML"));
        Assert.Equal("just text\n", File.ReadAllText(Path.Combine(nodes, "no_id.md"))); // never rewritten
    }

    [Fact]
    public async Task Daily_note_is_created_once_per_day()
    {
        using var v = new TestVault();
        var day = new DateOnly(2026, 10, 15);
        var a = await v.Writes.GetOrCreateDailyAsync("default", day);
        var b = await v.Writes.GetOrCreateDailyAsync("default", day);
        Assert.Equal(a.Header.Id, b.Header.Id);
        Assert.Equal("workspaces/default/daily/2026-10-15.md", a.Path);
    }

    [Fact]
    public async Task History_is_keyed_by_id_so_it_survives_a_move()
    {
        using var v = new TestVault();
        var n = await v.Writes.CreateAsync("default", "Mover", "1\n");
        var n2 = await v.Writes.UpdateAsync(n.Header.Id, n.Version, null, "2\n");
        // Move the file outside the app (Stage 1 has no move command yet).
        var dest = Path.Combine(v.Layout.Workspace("default"), "archive", "mover.md");
        File.Move(v.FullPath(n2.Path), dest);
        var moved = await v.Writes.UpdateAsync(n.Header.Id, n2.Version, null, "3\n");
        Assert.Equal("workspaces/default/archive/mover.md", moved.Path);
        Assert.Equal(2, v.Writes.History(n.Header.Id).Count);
    }

    [Fact]
    public async Task Concurrent_updates_with_the_same_version_let_exactly_one_win()
    {
        using var v = new TestVault();
        var n = await v.Writes.CreateAsync("default", "Race", "0\n");
        var results = await Task.WhenAll(Enumerable.Range(1, 8).Select(async i =>
        {
            try { await v.Writes.UpdateAsync(n.Header.Id, n.Version, null, $"{i}\n"); return true; }
            catch (VersionConflictException) { return false; }
        }));
        Assert.Equal(1, results.Count(ok => ok));
    }

    [Fact]
    public void Paths_cannot_escape_the_vault()
    {
        using var v = new TestVault();
        Assert.Throws<ValidationException>(() => v.Layout.ResolveInside("../outside.md"));
        Assert.Throws<ValidationException>(() => VaultLayout.ValidateWorkspaceName("../etc"));
    }
}
