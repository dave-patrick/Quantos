using Quantos.Core;
using Quantos.Core.Security;
using Quantos.Core.Structure;

namespace Quantos.Tests;

/// <summary>Release 1.1 batch find and replace.</summary>
public class ReplaceTests
{
    [Fact]
    public void Replaces_text_but_never_links_or_task_markers()
    {
        using var v = new TestVault();
        var target = v.Writes.Create(new CreateNodeRequest { Title = "Apollo" }, Actor.User);
        var note = v.Writes.Create(new CreateNodeRequest { Title = "Plan", Body = "Apollo ships. See [[Apollo]].\n- [ ] Tell Apollo team\n" }, Actor.User);
        var q = new ReplaceQuery("Apollo", "Artemis", Workspace: note.Workspace);
        var preview = v.Session.Replace.Preview(q, Actor.User);
        var hit = Assert.Single(preview.Notes, n => n.Id == note.Id);
        Assert.Equal(2, hit.Count); // the words, not the link (renaming the note changes that)

        var outcome = v.Session.Replace.Apply(q, Actor.User);
        Assert.Equal(1, outcome.Notes);
        var body = v.Writes.Require(note.Id).Body;
        Assert.Contains("Artemis ships.", body);
        Assert.Contains($"[[{target.Id}|Apollo]]", body);
        Assert.DoesNotContain("<!--", hit.Samples.Last().After);
        Assert.Contains("<!--task:", body);
        Assert.Contains("Tell Artemis team", body);
    }

    [Fact]
    public void Regex_whole_word_and_case_options()
    {
        using var v = new TestVault();
        var note = v.Writes.Create(new CreateNodeRequest { Title = "Dates", Body = "due 2026-10-18, cat Cat category\n" }, Actor.User);
        v.Session.Replace.Apply(new ReplaceQuery(@"(\d{4})-(\d{2})-(\d{2})", "$3/$2/$1", Regex: true, Ids: [note.Id]), Actor.User);
        v.Session.Replace.Apply(new ReplaceQuery("cat", "dog", WholeWord: true, MatchCase: true, Ids: [note.Id]), Actor.User);
        Assert.Contains("due 18/10/2026, dog Cat category", v.Writes.Require(note.Id).Body);
        Assert.Throws<QuantosException>(() => FindReplaceService.Build(new ReplaceQuery("(", "x", Regex: true)));
    }

    [Fact]
    public void Undo_restores_unless_edited_since_and_locked_notes_are_skipped()
    {
        using var v = new TestVault();
        var a = v.Writes.Create(new CreateNodeRequest { Title = "A", Body = "old word\n" }, Actor.User);
        var b = v.Writes.Create(new CreateNodeRequest { Title = "B", Body = "old word\n" }, Actor.User);
        var c = v.Writes.Create(new CreateNodeRequest { Title = "C", Body = "old word\n" }, Actor.User);
        v.Writes.UpdateFields(c.Id, null, new Dictionary<string, object?> { [NodeKeys.Locked] = true }, null, Actor.User);

        var outcome = v.Session.Replace.Apply(new ReplaceQuery("old", "new", Workspace: a.Workspace), Actor.User);
        Assert.Equal(2, outcome.Notes);
        Assert.Contains(outcome.Skipped, s => s.Contains("locked"));
        Assert.Contains("old word", v.Writes.Require(c.Id).Body);

        var bNow = v.Writes.Require(b.Id);
        v.Writes.Save(b.Id, bNow.Content.Replace("new word", "newer word"), bNow.Hash, Actor.User);
        var (restored, kept) = v.Session.Replace.Undo(outcome.UndoId!, Actor.User);
        Assert.Equal(1, restored);
        Assert.Equal(["B"], kept);
        Assert.Contains("old word", v.Writes.Require(a.Id).Body);
        Assert.Contains("newer word", v.Writes.Require(b.Id).Body);
    }
}
