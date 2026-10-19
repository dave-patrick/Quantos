using Quantos.Core;
using Quantos.Core.Security;

namespace Quantos.Tests;

/// <summary>Release 1.2 global knowledge graph.</summary>
public class GraphTests
{
    [Fact]
    public void Links_across_workspaces_tags_as_hubs_and_the_limit()
    {
        using var v = new TestVault();
        var a = v.Writes.Create(new CreateNodeRequest { Title = "Alpha", Tags = ["idea"] }, Actor.User);
        var b = v.Writes.Create(new CreateNodeRequest { Title = "Beta", Workspace = "work", Body = $"See [[{a.Id}|Alpha]].", Tags = ["idea"] }, Actor.User);
        v.Writes.Create(new CreateNodeRequest { Title = "Gamma", Body = $"Also [[{a.Id}|Alpha]]." }, Actor.User);
        v.Writes.Create(new CreateNodeRequest { Title = "Loner" }, Actor.User);

        var everywhere = v.Session.Graph.Global(null, 100, tags: false, null, Actor.User);
        Assert.Contains(everywhere.Edges, e => e.From == b.Id && e.To == a.Id);
        Assert.Contains(everywhere.Nodes, n => n.Title == "Beta" && n.Workspace == "work");
        Assert.Equal(2, everywhere.Nodes.Single(n => n.Id == a.Id).Degree);

        var withTags = v.Session.Graph.Global(null, 100, tags: true, null, Actor.User);
        Assert.Contains(withTags.Nodes, n => n.Id == "#idea" && n.Type == "tag" && n.Degree == 2);

        var oneWorkspace = v.Session.Graph.Global("default", 100, tags: false, null, Actor.User);
        Assert.DoesNotContain(oneWorkspace.Nodes, n => n.Title == "Beta");

        var top = v.Session.Graph.Global(null, 20, tags: false, ["note"], Actor.User);
        Assert.True(top.Nodes.Count <= 20);
        Assert.All(top.Nodes, n => Assert.Equal("note", n.Type));
    }
}
