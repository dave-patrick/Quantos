using Quantos.Core;
using Quantos.Core.Cache;
using Quantos.Core.Security;
using Quantos.Core.Storage;

namespace Quantos.Tests;

/// <summary>
/// Folders linked in from elsewhere on the computer: their Markdown is read and written where it
/// lies, so the other app and Quantos both work on the same files (STORAGE-SPEC section 8).
/// </summary>
public class LinkedFolderTests
{
    private static string Outside(TestVault v, string name = "outside")
    {
        var path = Path.Combine(v.Root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteFile(string folder, string name, string text)
    {
        var path = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static NodeSummary Only(TestVault v, string scope) =>
        v.Session.Cache.List(scope, null, includeArchived: true, limit: 100).Single();

    [Fact]
    public void A_linked_folder_is_indexed_where_it_lies()
    {
        using var v = new TestVault();
        var outside = Outside(v);
        WriteFile(outside, "note.md", "---\ntitle: Outside note\n---\n\nHello from the other app.\n");

        v.Session.Vault.RegisterLinkedFolder("notes", outside);
        v.Session.ApplyLinkedFolders();

        var node = Only(v, "notes");
        Assert.Equal("linked/notes/note.md", node.Path);
        // The file is still where the other app put it.
        Assert.True(File.Exists(Path.Combine(outside, "note.md")));
        // Quantos added an id and left the other app's own frontmatter alone.
        var text = File.ReadAllText(Path.Combine(outside, "note.md"));
        Assert.Contains("id:", text);
        Assert.Contains("title: Outside note", text);
        Assert.Contains("Hello from the other app.", text);
    }

    [Fact]
    public void A_linked_note_keeps_its_identity_when_the_other_app_rewrites_it()
    {
        using var v = new TestVault();
        var outside = Outside(v);
        WriteFile(outside, "note.md", "Just a body, no frontmatter.\n");
        v.Session.Vault.RegisterLinkedFolder("notes", outside);
        v.Session.ApplyLinkedFolders();
        var id = Only(v, "notes").Id;

        // The other app rewrites the file and drops the frontmatter.
        File.WriteAllText(Path.Combine(outside, "note.md"), "Their new text.\n");
        v.Session.Reconciler.Run();

        Assert.Equal(id, Only(v, "notes").Id);
    }

    [Fact]
    public void A_linked_note_is_saved_where_it_lives()
    {
        using var v = new TestVault();
        var outside = Outside(v);
        WriteFile(outside, "note.md", "---\ntitle: Outside\n---\n\nHello.\n");
        v.Session.Vault.RegisterLinkedFolder("notes", outside);
        v.Session.ApplyLinkedFolders();
        var node = v.Writes.Require(Only(v, "notes").Id);

        var saved = v.Writes.Save(node.Id, node.Content.Replace("Hello.", "Hi there."), node.Hash, Actor.User);

        Assert.Equal(SaveStatus.Saved, saved.Status);
        Assert.Contains("Hi there.", File.ReadAllText(Path.Combine(outside, "note.md")));
        // Nothing was copied into the vault.
        Assert.False(File.Exists(Path.Combine(v.VaultPath, "workspaces", "default", "nodes", "note.md")));
    }

    [Fact]
    public void A_linked_note_takes_the_newest_write_and_keeps_the_other_one_in_history()
    {
        using var v = new TestVault();
        var outside = Outside(v);
        WriteFile(outside, "note.md", "---\ntitle: Shared\n---\n\nOriginal.\n");
        v.Session.Vault.RegisterLinkedFolder("notes", outside);
        v.Session.ApplyLinkedFolders();
        var node = v.Writes.Require(Only(v, "notes").Id);
        var stale = node.Hash;

        // The other app saves first.
        File.WriteAllText(Path.Combine(outside, "note.md"), "---\nid: " + node.Id + "\ntitle: Shared\n---\n\nTheir version.\n");

        var saved = v.Writes.Save(node.Id, node.Content.Replace("Original.", "My version."), stale, Actor.User);

        Assert.Equal(SaveStatus.Saved, saved.Status);
        Assert.Contains("My version.", File.ReadAllText(Path.Combine(outside, "note.md")));
        // The version that was on disk is kept, so nothing is lost.
        Assert.Contains(v.Writes.History.List(node.Id), x => x.Kind == HistoryStore.Conflict);
    }

    [Fact]
    public void A_linked_note_is_not_moved_or_archived()
    {
        using var v = new TestVault();
        var outside = Outside(v);
        WriteFile(outside, "note.md", "Body.\n");
        v.Session.Vault.RegisterLinkedFolder("notes", outside);
        v.Session.ApplyLinkedFolders();
        var node = Only(v, "notes");

        Assert.Throws<QuantosException>(() => v.Writes.MoveToWorkspace(node.Id, "default", Actor.User));
        Assert.Throws<QuantosException>(() => v.Writes.SetArchived(node.Id, true, Actor.User));
        Assert.True(File.Exists(Path.Combine(outside, "note.md")));
    }

    [Fact]
    public void Unlinking_a_folder_leaves_its_files_alone()
    {
        using var v = new TestVault();
        var outside = Outside(v);
        WriteFile(outside, "note.md", "Body.\n");
        v.Session.Vault.RegisterLinkedFolder("notes", outside);
        v.Session.ApplyLinkedFolders();
        Assert.Single(v.Session.Cache.List("notes", null, includeArchived: true, limit: 100));

        v.Session.Vault.UnregisterLinkedFolder("notes");
        v.Session.ApplyLinkedFolders();

        Assert.Empty(v.Session.Cache.List("notes", null, includeArchived: true, limit: 100));
        Assert.True(File.Exists(Path.Combine(outside, "note.md")));
    }

    [Fact]
    public void A_linked_folder_cannot_take_the_name_of_a_workspace()
    {
        using var v = new TestVault();
        var outside = Outside(v);
        Assert.Throws<QuantosException>(() => v.Session.Vault.RegisterLinkedFolder("default", outside));
    }

    [Fact]
    public void A_linked_folder_cannot_be_inside_the_vault()
    {
        using var v = new TestVault();
        var inside = Path.Combine(v.VaultPath, "workspaces", "default");
        Assert.Throws<QuantosException>(() => v.Session.Vault.RegisterLinkedFolder("notes", inside));
    }

    [Fact]
    public void A_linked_folders_layout_stays_in_the_vault()
    {
        using var v = new TestVault();
        var outside = Outside(v);
        v.Session.Vault.RegisterLinkedFolder("notes", outside);

        var state = v.Session.Vault.WorkspaceStatePath("notes");
        Assert.StartsWith(v.VaultPath, state);
        v.Session.Config.WriteWorkspaceState("notes", System.Text.Json.Nodes.JsonNode.Parse("{}"), Actor.User);

        Assert.True(File.Exists(state));
        // Nothing of Quantos' making lands in the other app's folder.
        Assert.Empty(Directory.GetFiles(outside));
    }

    [Fact]
    public void The_watcher_picks_up_edits_in_a_linked_folder()
    {
        using var v = new TestVault();
        var outside = Outside(v);
        WriteFile(outside, "note.md", "---\ntitle: Shared\n---\n\nOriginal.\n");
        v.Session.Vault.RegisterLinkedFolder("notes", outside);
        v.Session.StartWatching();
        var errors = new List<Exception>();
        v.Session.Watcher!.Failed += errors.Add;
        v.Session.ApplyLinkedFolders();
        var id = Only(v, "notes").Id;

        // The other app rewrites the file and drops the frontmatter.
        var events = new List<ChangeEvent>();
        v.Session.Changes.Changed += events.Add;
        File.WriteAllText(Path.Combine(outside, "note.md"), "Their version.\n");
        for (var i = 0; i < 200 && !events.Any(e => e.Id == id); i++) Thread.Sleep(100);

        var node = Only(v, "notes");
        Assert.Equal(id, node.Id);
        Assert.Equal("Their version.\n", v.Writes.Require(id).Body);
        Assert.Empty(errors);
        // Quantos puts the id back, so the note keeps its identity.
        Assert.Contains("id: " + id, File.ReadAllText(Path.Combine(outside, "note.md")));
    }

    [Fact]
    public void The_watcher_keeps_watching_a_linked_folder_that_is_deleted_and_made_again()
    {
        var sweep = VaultWatcher.SweepInterval;
        VaultWatcher.SweepInterval = TimeSpan.FromMilliseconds(500);
        try
        {
            using var v = new TestVault();
            var outside = Outside(v);
            WriteFile(outside, "note.md", "---\ntitle: Shared\n---\n\nOriginal.\n");
            v.Session.Vault.RegisterLinkedFolder("notes", outside);
            v.Session.StartWatching();
            v.Session.ApplyLinkedFolders();

            // The other app replaces the whole folder.
            Directory.Delete(outside, recursive: true);
            Directory.CreateDirectory(outside);
            WriteFile(outside, "note.md", "A different file entirely.\n");

            for (var i = 0; i < 100 && Only(v, "notes").Title != "note"; i++) Thread.Sleep(100);
            Assert.Equal("A different file entirely.\n", v.Writes.Require(Only(v, "notes").Id).Body);
        }
        finally { VaultWatcher.SweepInterval = sweep; }
    }

    [Fact]
    public void A_file_next_to_a_linked_note_belongs_to_the_linked_folder()
    {
        using var v = new TestVault();
        var outside = Outside(v);
        v.Session.Vault.RegisterLinkedFolder("notes", outside);
        var picture = Path.Combine(outside, "images", "pic.png");
        Directory.CreateDirectory(Path.GetDirectoryName(picture)!);
        File.WriteAllBytes(picture, [1, 2, 3]);

        // The picture is addressed the way the note that shows it is: relative to the note's folder.
        Assert.Equal(picture, v.Session.Vault.Absolute("linked/notes/images/pic.png"));
        Assert.Equal("linked/notes/images/pic.png", v.Session.Vault.Relative(picture));
        Assert.True(v.Session.Vault.IsLinkedFile(picture));
        Assert.False(v.Session.Vault.IsLinkedFile(v.Abs("workspaces/default/nodes/x.md")));
    }

    [Fact]
    public void Linked_paths_map_to_the_folder_they_live_in()
    {
        using var v = new TestVault();
        var outside = Outside(v);
        v.Session.Vault.RegisterLinkedFolder("notes", outside);

        Assert.Equal("linked/notes/sub/note.md", v.Session.Vault.Relative(Path.Combine(outside, "sub", "note.md")));
        Assert.Equal("linked/notes", v.Session.Vault.Relative(outside));
        Assert.Equal(Path.Combine(outside, "sub", "note.md"), v.Session.Vault.Absolute("linked/notes/sub/note.md"));
        Assert.Equal("notes", Vault.WorkspaceOf("linked/notes/sub/note.md"));
        // A path can't climb out of the linked folder.
        Assert.Throws<UnauthorizedAccessException>(() => v.Session.Vault.Absolute("linked/notes/../elsewhere.md"));
    }
}
