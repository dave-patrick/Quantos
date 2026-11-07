using Quantos.Core;
using Quantos.Core.Cache;
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

/// <summary>Rebuilds 4 and 5 (2026-11-07): reader connections and SQL-side query filters.</summary>
public class CacheRebuildTests
{
    private static Node Put(TestVault v, string rel, string front, string body = "text")
    {
        var text = $"---\n{front}\n---\n{body}\n";
        var node = Node.FromText(rel, text);
        v.Writes.Cache.Upsert(node, 0, text.Length);
        return node;
    }

    [Fact]
    public void Other_threads_read_committed_data_without_waiting_while_the_batch_owner_sees_its_own_writes()
    {
        using var v = new TestVault();
        var cache = v.Writes.Cache;
        var id = Ulid.New();
        using (cache.Batch())
        {
            Put(v, "workspaces/default/notes/in-batch.md", $"id: {id}\ntitle: In batch\ntype: note\nworkspace: default");
            Assert.NotNull(cache.Get(id)); // the batch owner sees its own uncommitted write

            NodeSummary? seenElsewhere = null;
            var other = Task.Run(() => seenElsewhere = cache.Get(id));
            Assert.True(other.Wait(TimeSpan.FromSeconds(5)), "a read on another thread waited for the open batch");
            Assert.Null(seenElsewhere); // ...and other threads see only committed data
        }
        Assert.NotNull(Task.Run(() => cache.Get(id)).Result);
    }

    [Fact]
    public void Query_filters_give_the_same_answers_with_sql_narrowing()
    {
        using var v = new TestVault();
        string N(string slug, string front) { var id = Ulid.New(); Put(v, $"workspaces/default/{slug}.md", $"id: {id}\n{front}"); return id; }
        var upper = N("notes/Projects/a", "title: A\ntype: note\nworkspace: default\ntags: [Über, Alpha]");
        var lower = N("notes/projects/b", "title: B\ntype: note\nworkspace: default\ntags: [über]");
        var other = N("notes/other/c", "title: C\ntype: Note\nworkspace: default\ntags: [ALPHA]");
        var task = N("tasks/t", "title: T\ntype: task\nworkspace: default");
        var tpl = Ulid.New(); Put(v, "templates/tpl.md", $"id: {tpl}\ntitle: Tpl\ntype: template\nworkspace: ''");

        HashSet<string> Ids(QuerySpec q) => v.Session.Queries.Run(q, "default").Rows.Select(r => r.Id).ToHashSet();

        Assert.Equal(new HashSet<string> { upper, lower }, Ids(new QuerySpec { Tags = ["über"] }));
        Assert.Equal(new HashSet<string> { upper, other }, Ids(new QuerySpec { Tags = ["alpha"] }));
        Assert.Equal(new HashSet<string> { task }, Ids(new QuerySpec { Tags = ["task"] }));
        Assert.Equal(new HashSet<string> { upper, lower }, Ids(new QuerySpec { Folder = "workspaces/default/notes/projects" }));
        Assert.Equal(new HashSet<string> { upper, lower, other }, Ids(new QuerySpec { Types = ["note"] }));
        Assert.Equal(new HashSet<string> { tpl }, Ids(new QuerySpec { Types = ["template"] }));
        Assert.DoesNotContain(tpl, Ids(new QuerySpec()));
    }

    [Fact]
    public void Subtask_loops_are_cut()
    {
        using var v = new TestVault();
        string a = Ulid.New(), b = Ulid.New(), c = Ulid.New();
        Put(v, "workspaces/default/tasks/a.md", $"id: {a}\ntitle: A\ntype: task\nworkspace: default\nparent: '[[{b}]]'");
        Put(v, "workspaces/default/tasks/b.md", $"id: {b}\ntitle: B\ntype: task\nworkspace: default\nparent: '[[{a}]]'");
        Put(v, "workspaces/default/tasks/c.md", $"id: {c}\ntitle: C\ntype: task\nworkspace: default\nparent: {b}");
        Assert.Equal([b, c], v.Writes.Cache.Descendants(a).ToList()); // b first (depth 1), then c; a itself is left out
    }
}

/// <summary>Rebuild 2 (2026-11-07): each note keeps its newest N versions (Vault/settings.json historyKeep, default 10).</summary>
public class HistoryRetentionTests
{
    private static void Saves(TestVault v, string id, int count)
    {
        for (var i = 0; i < count; i++)
            v.Writes.History.Record(id, System.Text.Encoding.UTF8.GetBytes($"version {i} {Guid.NewGuid()}"), Quantos.Core.Storage.HistoryStore.Conflict);
    }

    [Fact]
    public void Each_note_keeps_ten_versions_by_default_and_the_newest_survive()
    {
        using var v = new TestVault();
        var id = Ulid.New();
        Saves(v, id, 15);
        var versions = v.Writes.History.List(id);
        Assert.Equal(10, versions.Count);
        Assert.StartsWith("version 14 ", v.Writes.History.Latest(id));
    }

    [Fact]
    public void Zero_keeps_everything_and_lowering_the_limit_trims_existing_history()
    {
        using var v = new TestVault();
        File.WriteAllText(Path.Combine(v.VaultPath, "settings.json"), "{\"historyKeep\": 0}");
        var id = Ulid.New();
        Saves(v, id, 14);
        Assert.Equal(14, v.Writes.History.List(id).Count);

        File.WriteAllText(Path.Combine(v.VaultPath, "settings.json"), "{\"historyKeep\": 3}");
        File.SetLastWriteTimeUtc(Path.Combine(v.VaultPath, "settings.json"), DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(11, v.Writes.History.PruneAll());
        Assert.Equal(3, v.Writes.History.List(id).Count);
        Assert.StartsWith("version 13 ", v.Writes.History.Latest(id));
    }

    [Fact]
    public void A_broken_setting_falls_back_to_ten()
    {
        using var v = new TestVault();
        File.WriteAllText(Path.Combine(v.VaultPath, "settings.json"), "{ not json");
        Assert.Equal(10, v.Writes.History.KeepVersions);
        File.WriteAllText(Path.Combine(v.VaultPath, "settings.json"), "{\"historyKeep\": -4}");
        File.SetLastWriteTimeUtc(Path.Combine(v.VaultPath, "settings.json"), DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(0, v.Writes.History.KeepVersions); // clamped to 0 = keep all, never deletes on a bad value
    }
}

/// <summary>Rebuild 3 (2026-11-07): change events carry the node's list entry.</summary>
public class LiveUpdateEventTests
{
    [Fact]
    public void Created_and_updated_events_carry_the_new_list_entry()
    {
        using var v = new TestVault();
        var seen = new List<ChangeEvent>();
        v.Writes.Changes.Changed += seen.Add;
        var node = v.Writes.Create(new CreateNodeRequest { Title = "Live", Body = "one", Tags = ["alpha"] }, Actor.User);
        v.Writes.Rename(node.Id, "Live renamed", Actor.User);

        var created = seen.First(e => e.Kind == ChangeKind.Created && e.Id == node.Id);
        Assert.Equal("Live", created.Summary?.Title);
        Assert.Contains("alpha", created.Summary!.Tags);
        var renamed = seen.Last(e => e.Id == node.Id && e.Kind is ChangeKind.Updated or ChangeKind.Moved);
        Assert.Equal("Live renamed", renamed.Summary?.Title);

        v.Writes.Trash(node.Id, Actor.User);
        Assert.Null(seen.Last().Summary); // nothing to describe once it's gone
    }
}
