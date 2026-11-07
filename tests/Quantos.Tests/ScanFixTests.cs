using Quantos.Core;
using Quantos.Core.Security;
using Quantos.Core.Structure;
using Quantos.Core.Tasks;

namespace Quantos.Tests;

/// <summary>Regression tests for the 2026-11-06 code scan findings.</summary>
public class ScanFixTests
{
    [Fact]
    public void Trashing_a_task_takes_every_level_of_subtasks_and_restoring_brings_them_all_back()
    {
        using var v = new TestVault();
        var tasks = v.Session.Tasks;
        var a = tasks.Create(new CreateTaskRequest { Title = "A", Workspace = "default" }, Actor.User);
        var b = tasks.Create(new CreateTaskRequest { Title = "B", Workspace = "default", Parent = a.Id }, Actor.User);
        var c = tasks.Create(new CreateTaskRequest { Title = "C", Workspace = "default", Parent = b.Id }, Actor.User);
        var d = tasks.Create(new CreateTaskRequest { Title = "D", Workspace = "default", Parent = c.Id }, Actor.User);

        v.Writes.Trash(a.Id, Actor.User);

        foreach (var id in new[] { a.Id, b.Id, c.Id, d.Id })
        {
            Assert.NotNull(v.Writes.TrashBin.FindById(id));
            Assert.Null(v.Writes.Get(id));
        }

        v.Writes.Restore(v.Writes.TrashBin.FindById(a.Id)!.EntryId, Actor.User);
        foreach (var id in new[] { a.Id, b.Id, c.Id, d.Id }) Assert.NotNull(v.Writes.Get(id));
    }

    [Fact]
    public void Trashing_a_subtask_leaves_its_parent_alone()
    {
        using var v = new TestVault();
        var tasks = v.Session.Tasks;
        var a = tasks.Create(new CreateTaskRequest { Title = "A", Workspace = "default" }, Actor.User);
        var b = tasks.Create(new CreateTaskRequest { Title = "B", Workspace = "default", Parent = a.Id }, Actor.User);
        var c = tasks.Create(new CreateTaskRequest { Title = "C", Workspace = "default", Parent = b.Id }, Actor.User);

        v.Writes.Trash(b.Id, Actor.User);

        Assert.NotNull(v.Writes.Get(a.Id));
        Assert.Null(v.Writes.Get(c.Id));
    }

    [Fact]
    public void Listing_and_querying_are_not_capped_at_5000()
    {
        using var v = new TestVault();
        const int Count = 5_050;
        using (v.Writes.Cache.Batch())
        {
            for (var i = 0; i < Count; i++)
            {
                var id = Ulid.New();
                var text = $"---\nid: {id}\ntitle: Bulk {i}\ntype: note\nmodified: 2026-01-01T00:00:00Z\n---\nshared wordzebra body {i}\n";
                v.Writes.Cache.Upsert(Node.FromText($"workspaces/default/notes/bulk-{i}.md", text), 0, text.Length);
            }
        }

        Assert.True(v.Writes.Cache.List("default", "note").Count >= Count);

        var result = v.Session.Queries.Run(new QuerySpec { Text = "wordzebra", Limit = 10 }, "default");
        Assert.Equal(Count, result.Total);
    }

    [Fact]
    public void Getting_one_task_returns_it_with_its_subtasks()
    {
        using var v = new TestVault();
        var tasks = v.Session.Tasks;
        var a = tasks.Create(new CreateTaskRequest { Title = "A", Workspace = "default" }, Actor.User);
        var b = tasks.Create(new CreateTaskRequest { Title = "B", Workspace = "default", Parent = a.Id }, Actor.User);

        var dto = tasks.Get(a.Id)!;
        var fromList = tasks.List().Single(t => t.Id == a.Id);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(fromList), System.Text.Json.JsonSerializer.Serialize(dto));
        Assert.Null(tasks.Get("01ZZZZZZZZZZZZZZZZZZZZZZZZ"));
        Assert.Equal(a.Id, tasks.Get(b.Id)!.Parent);
    }
}
