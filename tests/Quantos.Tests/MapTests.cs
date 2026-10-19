using Quantos.Core;
using Quantos.Core.Security;
using Quantos.Core.Structure;

namespace Quantos.Tests;

/// <summary>Release 1.2 map: locations read from properties, offline.</summary>
public class MapTests
{
    [Theory]
    [InlineData("location: \"51.5072, -0.1276\"", 51.5072, -0.1276)]
    [InlineData("coordinates: [40.7128, -74.006]", 40.7128, -74.006)]
    [InlineData("geo:\n  lat: -33.8688\n  lon: 151.2093", -33.8688, 151.2093)]
    [InlineData("lat: 35.6762\nlng: 139.6503", 35.6762, 139.6503)]
    public void Reads_the_usual_ways_of_writing_a_place(string yaml, double lat, double lng)
    {
        var node = Node.FromText("n.md", $"---\nid: X\ntitle: T\n{yaml}\n---\n");
        var p = MapService.Parse(node.Properties);
        Assert.NotNull(p);
        Assert.Equal(lat, p.Value.Lat, 4);
        Assert.Equal(lng, p.Value.Lng, 4);
    }

    [Theory]
    [InlineData("location: London")]
    [InlineData("location: \"95, 10\"")]
    [InlineData("location: \"0, 0\"")]
    public void Ignores_what_isnt_a_place(string yaml)
    {
        var node = Node.FromText("n.md", $"---\nid: X\ntitle: T\n{yaml}\n---\n");
        Assert.Null(MapService.Parse(node.Properties));
    }

    [Fact]
    public void Lists_notes_with_places()
    {
        using var v = new TestVault();
        v.Writes.Create(new CreateNodeRequest { Title = "Office", Properties = new Dictionary<string, object?> { ["location"] = "51.5072, -0.1276", ["place"] = "London" } }, Actor.User);
        v.Writes.Create(new CreateNodeRequest { Title = "Nowhere" }, Actor.User);
        var places = v.Session.Map.Places(null, Actor.User);
        var office = Assert.Single(places);
        Assert.Equal(("Office", "London"), (office.Title, office.Name));
    }
}
