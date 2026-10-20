using Quantos.Core;
using Quantos.Core.Security;

namespace Quantos.Tests;

/// <summary>The derived cache: what it keeps and what it hands back.</summary>
public class CacheTests
{
    [Fact]
    public void Excerpts_gives_the_start_of_every_note()
    {
        using var v = new TestVault();
        var a = v.Writes.Create(new CreateNodeRequest { Title = "Policy", Body = "Settled text." }, Actor.User);
        var b = v.Writes.Create(new CreateNodeRequest { Title = "Budget", Body = "Numbers here." }, Actor.User);
        var excerpts = v.Session.Cache.Excerpts();
        Assert.Contains("Settled text.", excerpts[a.Id]);
        Assert.Contains("Numbers here.", excerpts[b.Id]);
    }
}
