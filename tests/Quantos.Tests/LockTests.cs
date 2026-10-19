using Quantos.Core;
using Quantos.Core.Security;

namespace Quantos.Tests;

/// <summary>Release 1.1 read-only mode: a locked note refuses edits until the person unlocks it.</summary>
public class LockTests
{
    private static Node Locked(TestVault v, string body = "Settled text.\n")
    {
        var note = v.Writes.Create(new CreateNodeRequest { Title = "Policy", Body = body }, Actor.User);
        var r = v.Writes.UpdateFields(note.Id, null, new Dictionary<string, object?> { [NodeKeys.Locked] = true }, null, Actor.User);
        Assert.Equal(SaveStatus.Saved, r.Status);
        Assert.True(r.Node!.Locked);
        Assert.DoesNotContain(NodeKeys.Locked, r.Node.Properties.Keys);
        return r.Node;
    }

    [Fact]
    public void A_locked_note_refuses_edits()
    {
        using var v = new TestVault();
        var note = Locked(v);
        var save = v.Writes.Save(note.Id, note.Content.Replace("Settled", "Changed"), note.Hash, Actor.User);
        Assert.Equal(SaveStatus.ReadOnly, save.Status);
        Assert.Contains("locked", save.Message);
        Assert.Equal(SaveStatus.ReadOnly, v.Writes.UpdateFields(note.Id, null, new Dictionary<string, object?> { ["status"] = "x" }, null, Actor.User).Status);
        Assert.Throws<QuantosException>(() => v.Writes.AppendToBody(note.Id, "More", Actor.User));
        Assert.Contains("Settled text.", v.Writes.Require(note.Id).Body);
    }

    [Fact]
    public void Only_the_person_can_unlock()
    {
        using var v = new TestVault();
        var note = Locked(v);
        var agent = new Actor(ActorKind.Agent, "helper");
        v.Session.Permissions.Set(ActorKind.Agent, "helper", [Permission.Read, Permission.CreateEdit]);
        var refused = v.Writes.UpdateFields(note.Id, null, null, [NodeKeys.Locked], agent);
        Assert.Equal(SaveStatus.ReadOnly, refused.Status);
        Assert.Contains("Only you", refused.Message);

        var unlocked = v.Writes.UpdateFields(note.Id, null, null, [NodeKeys.Locked], Actor.User);
        Assert.False(unlocked.Node!.Locked);
        var current = v.Writes.Require(note.Id);
        Assert.Equal(SaveStatus.Saved, v.Writes.Save(note.Id, current.Content.Replace("Settled", "Changed"), current.Hash, Actor.User).Status);
    }

    [Fact]
    public void Links_in_a_locked_note_still_follow_a_rename()
    {
        using var v = new TestVault();
        var target = v.Writes.Create(new CreateNodeRequest { Title = "Budget" }, Actor.User);
        var note = Locked(v, "See [[Budget]].\n");
        v.Writes.Rename(target.Id, "Budget 2027", Actor.User);
        Assert.Contains("Budget 2027", v.Writes.Require(note.Id).Body);
    }
}
