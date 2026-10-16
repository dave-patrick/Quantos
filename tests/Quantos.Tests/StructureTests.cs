using Quantos.Core;
using Quantos.Core.Security;
using Quantos.Core.Structure;

namespace Quantos.Tests;

/// <summary>Stage 4: supertags, inheritance, templates, forms, queries.</summary>
public class StructureTests
{
    private static Dictionary<string, object?> Field(string name, string type, bool required = false, params string[] options)
    {
        var f = new Dictionary<string, object?> { ["name"] = name, ["type"] = type };
        if (required) f["required"] = true;
        if (options.Length > 0) f["options"] = options.ToList();
        return f;
    }

    private static SupertagInput Tag(string name, string? parent = null, params Dictionary<string, object?>[] fields) =>
        new(name, parent, null, null, null, [.. fields], null, null, null, null);

    [Fact]
    public void Fields_are_inherited_from_parent_supertags()
    {
        using var v = new TestVault();
        var s = v.Session.Supertags;
        s.Create(Tag("event", null, Field("When", "date", required: true), Field("Location", "text")), Actor.User);
        s.Create(Tag("meeting", "event", Field("Attendees", "nodes")), Actor.User);
        var meeting = s.Get("meeting")!;
        Assert.Equal(["attendees", "when", "location"], meeting.AllFields.Select(f => f.Key).OrderBy(k => k == "attendees" ? 0 : 1).ThenBy(k => k == "when" ? 0 : 1));
        Assert.Equal("event", meeting.AllFields.Single(f => f.Key == "when").Inherited);
        Assert.True(meeting.AllFields.Single(f => f.Key == "when").Required);
    }

    [Fact]
    public void Inheritance_loops_are_blocked()
    {
        using var v = new TestVault();
        var s = v.Session.Supertags;
        var a = s.Create(Tag("alpha"), Actor.User);
        s.Create(Tag("beta", "alpha"), Actor.User);
        Assert.Throws<QuantosException>(() => s.Update(a.Id, Tag("alpha", "beta"), Actor.User));
        // A loop made outside the app is reported and ignored, not followed.
        var file = v.Abs(a.Path);
        File.WriteAllText(file, File.ReadAllText(file).Replace("type: supertag", "type: supertag\nparent: beta"));
        v.Restart();
        Assert.NotNull(v.Session.Supertags.Get("alpha")!.Error);
    }

    [Fact]
    public void Conflicting_field_names_are_namespaced()
    {
        using var v = new TestVault();
        var s = v.Session.Supertags;
        s.Create(Tag("book", null, Field("Rating", "number")), Actor.User);
        s.Create(Tag("film", null, Field("Rating", "select", false, "Good", "Bad")), Actor.User);
        Assert.Equal("rating", s.Get("book")!.AllFields.Single().Key);
        Assert.Equal("film.rating", s.Get("film")!.AllFields.Single().Key);
    }

    [Fact]
    public void Renaming_a_tag_updates_real_references_only()
    {
        using var v = new TestVault();
        var tag = v.Session.Supertags.Create(Tag("proj"), Actor.User);
        var note = v.Writes.Create(new CreateNodeRequest { Title = "Plan", Tags = ["proj"], Body = "Tagged #proj here.\n```\n#proj stays in code\n```\n" }, Actor.User);
        var versionsBefore = v.Writes.History.List(note.Id).Count;
        v.Session.Supertags.Rename(tag.Id, "project", Actor.User);
        var updated = v.Writes.Require(note.Id);
        Assert.Equal(["project"], updated.Tags);
        Assert.Contains("Tagged #project here.", updated.Body);
        Assert.Contains("#proj stays in code", updated.Body);
        Assert.NotNull(v.Session.Supertags.Get("project"));
        // Earlier history snapshots keep the old name.
        Assert.Contains(v.Writes.History.List(note.Id).Select(x => v.Writes.History.Read(note.Id, x.Version)!), c => c.Contains("Tagged #proj here."));
        Assert.True(v.Writes.History.List(note.Id).Count >= versionsBefore);
    }

    [Fact]
    public void Linked_templates_apply_automatically_when_a_tag_is_added()
    {
        using var v = new TestVault();
        var template = v.Session.Templates.Create("Meeting notes", "## Agenda\n\n## Actions for {{title}} on {{date}}\n", Actor.User);
        v.Writes.UpdateFields(template.Id, null, new Dictionary<string, object?> { ["status"] = "planned" }, null, Actor.User);
        v.Session.Supertags.Create(new SupertagInput("meeting", null, null, null, null, null, template.Id, true, null, null), Actor.User);
        var note = v.Writes.Create(new CreateNodeRequest { Title = "Kickoff", Tags = ["meeting"] }, Actor.User);
        var applied = v.Writes.Require(note.Id);
        Assert.Contains("## Agenda", applied.Body);
        Assert.Contains("Actions for Kickoff on " + v.Session.Clock.Today.ToString("yyyy-MM-dd"), applied.Body);
        Assert.Equal("planned", applied.Properties["status"]);
        // Adding the tag later applies it too, without overwriting existing values.
        var other = v.Writes.Create(new CreateNodeRequest { Title = "Retro", Properties = new Dictionary<string, object?> { ["status"] = "done" } }, Actor.User);
        v.Writes.UpdateFields(other.Id, null, new Dictionary<string, object?> { ["tags"] = new List<object?> { "meeting" } }, null, Actor.User);
        var retro = v.Writes.Require(other.Id);
        Assert.Contains("## Agenda", retro.Body);
        Assert.Equal("done", retro.Properties["status"]);
    }

    [Fact]
    public void Freeform_edits_warn_but_are_never_blocked()
    {
        using var v = new TestVault();
        v.Session.Supertags.Create(Tag("book", null, Field("Pages", "number", required: true)), Actor.User);
        var note = v.Writes.Create(new CreateNodeRequest { Title = "Dune", Tags = ["book"] }, Actor.User);
        var r = v.Writes.UpdateFields(note.Id, null, new Dictionary<string, object?> { ["pages"] = "lots" }, null, Actor.User);
        Assert.Equal(SaveStatus.Saved, r.Status);
        Assert.Equal("lots", v.Writes.Require(note.Id).Properties["pages"]);
    }

    [Fact]
    public void Schema_migration_happens_only_when_asked_and_keeps_unconvertible_values()
    {
        using var v = new TestVault();
        var tag = v.Session.Supertags.Create(Tag("book", null, Field("Pages", "text")), Actor.User);
        var a = v.Writes.Create(new CreateNodeRequest { Title = "A", Tags = ["book"], Properties = new Dictionary<string, object?> { ["pages"] = "320" } }, Actor.User);
        var b = v.Writes.Create(new CreateNodeRequest { Title = "B", Tags = ["book"], Properties = new Dictionary<string, object?> { ["pages"] = "many" } }, Actor.User);
        v.Session.Supertags.Update(tag.Id, Tag("book", null, Field("Pages", "number")), Actor.User);
        Assert.Equal("320", v.Writes.Require(a.Id).Properties["pages"]); // untouched by the schema change
        var (changed, skipped) = v.Session.Supertags.Migrate(tag.Id, "pages", "number", null, Actor.User);
        Assert.Equal((1, 1), (changed, skipped));
        Assert.Contains("pages: 320\n", v.Writes.Require(a.Id).Content);
        Assert.Equal("many", v.Writes.Require(b.Id).Properties["pages"]);
    }

    [Fact]
    public void Queries_filter_sort_and_follow_tag_inheritance()
    {
        using var v = new TestVault();
        v.Session.Supertags.Create(Tag("event"), Actor.User);
        v.Session.Supertags.Create(Tag("meeting", "event"), Actor.User);
        Node Make(string title, string tag, int prio, string due) => v.Writes.Create(new CreateNodeRequest
        {
            Title = title, Tags = [tag], Properties = new Dictionary<string, object?> { ["prio"] = prio, ["due"] = due, ["owner"] = $"[[{Ulid.New()}|Sam]]" }
        }, Actor.User);
        Make("Standup", "meeting", 2, "2026-10-20");
        Make("Launch", "event", 1, "2026-11-01");
        Make("Other", "misc", 1, "2026-10-01");
        var q = v.Session.Queries;
        var events = q.Run(new QuerySpec { Tags = ["event"], Sort = [new QuerySort("prio")] }, "default");
        Assert.Equal(["Launch", "Standup"], events.Rows.Select(r => r.Title));
        var parsed = q.RunText("tags: [event]\nwhere:\n  - prio >= 2\n  - owner = Sam\nsort: -due\ncolumns: [title, due]", "default");
        Assert.Null(parsed.Error);
        Assert.Equal(["Standup"], parsed.Rows.Select(r => r.Title));
        Assert.Equal(["title", "due"], parsed.Columns);
        Assert.NotNull(q.RunText("where: nonsense here", "default").Error);
    }
}
