using Quantos.Core;
using Quantos.Core.Security;
using Quantos.Core.Structure;
using Quantos.Core.Markdown;

namespace Quantos.Tests;

public class SupertagScopeTests
{
    private static SupertagInput Tag(string name, string scope = "", string? parent = null) =>
        new(name, parent, "#abcdef", null, scope, [new() { ["name"] = "Rating", ["type"] = "number", ["key"] = "rating" }], null, false, "table", "Description");

    [Fact]
    public void Scope_moves_preserve_identity_fields_and_tagged_notes_across_restart()
    {
        using var v = new TestVault();
        v.Session.Vault.EnsureWorkspace("videos");
        var s = v.Session.Supertags;
        var tag = s.Create(Tag("review"), Actor.User);
        var note = v.Writes.Create(new() { Title = "One", Tags = ["review"], Properties = new Dictionary<string, object?> { ["rating"] = 4 } }, Actor.User);
        var before = v.Writes.Require(note.Id).Content;
        var moved = s.ChangeScope(tag.Id, "videos", Actor.User);
        Assert.Equal(tag.Id, moved.Id);
        Assert.Equal("videos", moved.Scope);
        Assert.Equal(tag.Fields, moved.Fields);
        Assert.StartsWith("workspaces/videos/supertags/", moved.Path);
        Assert.DoesNotContain(s.List("default"), t => t.Id == tag.Id);
        Assert.Equal(before, v.Writes.Require(note.Id).Content);
        v.Restart();
        var shared = v.Session.Supertags.ChangeScope(tag.Id, "", Actor.User);
        Assert.StartsWith("supertags/", shared.Path);
        Assert.Equal(tag.Fields, shared.Fields);
        Assert.Equal(before, v.Writes.Require(note.Id).Content);
    }

    [Fact]
    public void Same_name_overrides_remain_independently_addressable_and_collisions_do_not_write()
    {
        using var v = new TestVault();
        v.Session.Vault.EnsureWorkspace("videos");
        var s = v.Session.Supertags;
        var local = s.Create(Tag("review", "videos"), Actor.User);
        var shared = s.Create(Tag("review"), Actor.User);
        Assert.Equal(shared.Id, s.Get(shared.Id)!.Id);
        Assert.Equal(local.Id, s.Get("review", "videos")!.Id);
        var before = v.Writes.Require(shared.Id).Content;
        Assert.Throws<QuantosException>(() => s.ChangeScope(shared.Id, "videos", Actor.User));
        Assert.Equal(before, v.Writes.Require(shared.Id).Content);
        Assert.Equal("", s.Get(shared.Id)!.Scope);
    }

    [Fact]
    public void Moving_override_cannot_expose_an_inheritance_cycle_in_source_workspace()
    {
        using var v = new TestVault();
        var s = v.Session.Supertags;
        v.Session.Vault.EnsureWorkspace("videos");
        v.Session.Vault.EnsureWorkspace("other");
        s.Create(Tag("a", "", "b"), Actor.User);
        var localA = s.Create(Tag("a", "videos"), Actor.User);
        s.Create(Tag("b", "videos", "a"), Actor.User);
        Assert.Throws<QuantosException>(() => s.ChangeScope(localA.Id, "other", Actor.User));
        Assert.Equal("videos", s.Get(localA.Id)!.Scope);
    }

    [Fact]
    public void Scope_rejects_unknown_workspace_locked_definitions_and_non_tags()
    {
        using var v = new TestVault();
        var s = v.Session.Supertags;
        var tag = s.Create(Tag("review"), Actor.User);
        Assert.Throws<QuantosException>(() => s.ChangeScope(tag.Id, "../escape", Actor.User));
        var note = v.Writes.Create(new() { Title = "Note" }, Actor.User);
        Assert.Throws<QuantosException>(() => s.ChangeScope(note.Id, "", Actor.User));
        v.Writes.UpdateFields(tag.Id, null, new Dictionary<string, object?> { ["locked"] = true }, null, Actor.User);
        Assert.Throws<QuantosException>(() => s.ChangeScope(tag.Id, "default", Actor.User));
    }

    [Fact]
    public void Synthetic_task_and_implicit_tags_materialize_in_chosen_workspace()
    {
        using var v = new TestVault();
        var s = v.Session.Supertags;
        v.Session.Vault.EnsureWorkspace("videos");
        var task = s.ChangeScope(SupertagService.TaskDefinitionId, "videos", Actor.User);
        Assert.Equal("videos", task.Scope);
        Assert.Contains(task.Fields, f => f.Key == "status");
        Assert.Equal("default", s.ChangeScope(SupertagService.TaskDefinitionId, "default", Actor.User).Scope);
        v.Writes.Create(new() { Title = "Tagged", Tags = ["meeting"] }, Actor.User);
        var meeting = s.ChangeScope("implicit:meeting", "videos", Actor.User);
        Assert.Equal("videos", meeting.Scope);
        Assert.Empty(meeting.Fields);
        Assert.Equal("default", s.ChangeScope("implicit:meeting", "default", Actor.User).Scope);
    }

    [Fact]
    public void Existing_imported_tag_names_with_spaces_are_preserved_when_scoped()
    {
        using var v = new TestVault();
        var note = v.Writes.Create(new() { Title = "Imported", Tags = ["machine learning"] }, Actor.User);
        var content = note.Content;
        var tag = v.Session.Supertags.ChangeScope("implicit:machine learning", "default", Actor.User);
        Assert.Equal("machine learning", tag.Name);
        Assert.Equal("default", tag.Scope);
        Assert.Equal(content, v.Writes.Require(note.Id).Content);
        Assert.Throws<QuantosException>(() => v.Session.Supertags.ChangeScope("implicit:unused space", "default", Actor.User));
    }
}
