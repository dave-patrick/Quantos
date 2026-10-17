using Quantos.Core;
using Quantos.Core.Markdown;
using Quantos.Core.Security;
using Quantos.Core.Structure;

namespace Quantos.Tests;

/// <summary>Stage 5: synced blocks, embeds, extract and merge. Advanced content survives open/save and outside edits.</summary>
public class RelationTests
{
    [Fact]
    public void Editing_any_copy_of_a_synced_block_updates_the_others()
    {
        using var v = new TestVault();
        var block = v.Session.SyncedBlocks.Create("Shared checklist v1", null, Actor.User);
        var wrapped = SyncedBlockService.Wrap(block.Id, "Shared checklist v1");
        var a = v.Writes.Create(new CreateNodeRequest { Title = "A", Body = "Intro\n" + wrapped + "\nOutro\n" }, Actor.User);
        var b = v.Writes.Create(new CreateNodeRequest { Title = "B", Body = wrapped + "\n" }, Actor.User);
        var current = v.Writes.Require(a.Id);
        v.Writes.Save(a.Id, current.Content.Replace("Shared checklist v1", "Shared checklist v2"), current.Hash, Actor.User);
        Assert.Contains("Shared checklist v2", v.Writes.Require(block.Id).Body);
        Assert.Contains("Shared checklist v2", v.Writes.Require(b.Id).Body);
        Assert.Contains("Intro", v.Writes.Require(a.Id).Body);
        // The previous text is still in history.
        Assert.Contains(v.Writes.History.List(b.Id).Select(x => v.Writes.History.Read(b.Id, x.Version)!), c => c.Contains("Shared checklist v1"));
    }

    [Fact] // Stage 5 exit: advanced content survives edits made outside the app
    public void Synced_block_edited_outside_the_app_propagates_on_rescan()
    {
        using var v = new TestVault();
        var block = v.Session.SyncedBlocks.Create("Policy text", null, Actor.User);
        var a = v.Writes.Create(new CreateNodeRequest { Title = "A", Body = SyncedBlockService.Wrap(block.Id, "Policy text") + "\n" }, Actor.User);
        var b = v.Writes.Create(new CreateNodeRequest { Title = "B", Body = SyncedBlockService.Wrap(block.Id, "Policy text") + "\n" }, Actor.User);
        v.Session.Dispose();
        File.WriteAllText(v.Abs(a.Path), File.ReadAllText(v.Abs(a.Path)).Replace("Policy text", "Policy text, revised in Notepad"));
        v.Restart();
        // Outside edits are indexed but not silently propagated; the next save in the app syncs them.
        var current = v.Writes.Require(a.Id);
        Assert.Contains("revised in Notepad", current.Body);
        v.Writes.Save(a.Id, current.Content + "\nmore\n", current.Hash, Actor.User);
        Assert.Contains("revised in Notepad", v.Writes.Require(b.Id).Body);
    }

    [Fact]
    public void Block_and_heading_fragments_can_be_embedded()
    {
        var body = "# Title\n\nFirst para line one\nline two ^b1\n\n## Section\nInside section\n\n## Next\nOther\n";
        Assert.Equal("First para line one\nline two", Links.Fragment(body, "^b1"));
        Assert.Equal("## Section\nInside section", Links.Fragment(body, "Section"));
        Assert.Null(Links.Fragment(body, "^missing"));
    }

    [Fact]
    public void Extract_moves_text_to_a_new_note_and_links_it()
    {
        using var v = new TestVault();
        var src = v.Writes.Create(new CreateNodeRequest { Title = "Long note", Body = "Keep this.\nMove this paragraph out.\nKeep that.\n" }, Actor.User);
        var (source, extracted) = v.Writes.Extract(src.Id, "Move this paragraph out.", "Moved idea", embed: false, Actor.User);
        Assert.Equal("Moved idea", extracted.Title);
        Assert.Contains("Move this paragraph out.", extracted.Body);
        Assert.Contains($"[[{extracted.Id}|Moved idea]]", source.Body);
        Assert.DoesNotContain("Move this paragraph out.", source.Body);
        Assert.Single(v.Session.Cache.Backlinks(extracted.Id));
    }

    [Fact]
    public void Merge_joins_notes_repoints_links_and_trashes_the_other()
    {
        using var v = new TestVault();
        var keep = v.Writes.Create(new CreateNodeRequest { Title = "Apollo", Body = "Main notes.\n", Tags = ["project"] }, Actor.User);
        var dupe = v.Writes.Create(new CreateNodeRequest { Title = "Apollo project", Body = "Duplicate notes.\n", Tags = ["client"], Properties = new Dictionary<string, object?> { ["owner"] = "Sam" } }, Actor.User);
        var linker = v.Writes.Create(new CreateNodeRequest { Title = "Index", Body = "See [[Apollo project]].\n" }, Actor.User);
        var merged = v.Writes.Merge(keep.Id, dupe.Id, Actor.User);
        Assert.Contains("Main notes.", merged.Body);
        Assert.Contains("Duplicate notes.", merged.Body);
        Assert.Equal(["project", "client"], merged.Tags);
        Assert.Equal("Sam", merged.Properties["owner"]);
        Assert.Contains("Apollo project", merged.Aliases);
        Assert.Contains($"[[{keep.Id}|Apollo project]]", v.Writes.Require(linker.Id).Body);
        Assert.Single(v.Writes.TrashBin.List(), e => e.Id == dupe.Id);
    }

    [Fact] // BUILD-PLAN 4: renaming updates every reference, including links held in properties
    public void Links_in_properties_are_backlinks_and_follow_renames()
    {
        using var v = new TestVault();
        var sam = v.Writes.Create(new CreateNodeRequest { Title = "Sam" }, Actor.User);
        var meeting = v.Writes.Create(new CreateNodeRequest
        {
            Title = "Kickoff",
            Properties = new Dictionary<string, object?> { ["owner"] = $"[[{sam.Id}|Sam]]", ["attendees"] = new List<object?> { $"[[{sam.Id}|Sam]]", "Visitor" } }
        }, Actor.User);

        var back = v.Session.Cache.Backlinks(sam.Id);
        Assert.Equal(2, back.Count);
        Assert.All(back, b => Assert.Equal(meeting.Id, b.SourceId));

        v.Writes.Rename(sam.Id, "Samantha", Actor.User);
        var after = v.Writes.Require(meeting.Id);
        Assert.Equal($"[[{sam.Id}|Samantha]]", after.Properties["owner"]);
        Assert.Equal(new object?[] { $"[[{sam.Id}|Samantha]]", "Visitor" }, ((IEnumerable<object?>)after.Properties["attendees"]!).ToArray());
    }

    [Fact]
    public void Vault_diagnostics_report_broken_links_in_properties()
    {
        using var v = new TestVault();
        var meeting = v.Writes.Create(new CreateNodeRequest
        {
            Title = "Kickoff",
            Body = "Notes.\n",
            Properties = new Dictionary<string, object?> { ["owner"] = "[[01ARZ3NDEKTSV4RRFFQ69G5FAV|Ghost]]" }
        }, Actor.User);
        var (diagnostics, _) = Integrity.Check(v.Writes);
        var broken = Assert.Single(diagnostics, d => d.Code == "broken-link");
        Assert.Equal(meeting.Id, broken.Id);
        Assert.Contains("Ghost", broken.Message);
    }
}