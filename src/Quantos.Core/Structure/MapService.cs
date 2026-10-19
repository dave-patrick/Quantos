using System.Globalization;
using System.Text.RegularExpressions;
using Quantos.Core.Security;
using Quantos.Core.Tasks;

namespace Quantos.Core.Structure;

public sealed record Place(string Id, string Title, string Type, string Workspace, double Lat, double Lng, string? Name, IReadOnlyList<string> Tags);

/// <summary>
/// Map and location views (Release 1.2). A note has a place when one of its properties holds coordinates:
/// <c>location: "51.5072, -0.1276"</c> (also <c>coordinates</c>, <c>geo</c>), a <c>[lat, lng]</c> list, a
/// <c>{lat, lng}</c> map, or separate <c>lat</c> and <c>lng</c> / <c>lon</c> properties. An optional <c>place</c>
/// property names it. Nothing is looked up online.
/// </summary>
public sealed partial class MapService(WriteService writes)
{
    public static readonly string[] Keys = ["location", "coordinates", "geo", "latlng"];

    [GeneratedRegex(@"^\s*\(?\s*(-?\d{1,3}(?:\.\d+)?)\s*[,;\s]\s*(-?\d{1,3}(?:\.\d+)?)\s*\)?\s*$")]
    private static partial Regex Pair();

    public static (double Lat, double Lng)? Parse(IReadOnlyDictionary<string, object?> props)
    {
        foreach (var key in Keys)
        {
            if (!props.TryGetValue(key, out var v) || v is null) continue;
            if (FromValue(v) is { } p) return p;
        }
        var lat = Num(props, "lat", "latitude");
        var lng = Num(props, "lng", "lon", "long", "longitude");
        return lat is { } a && lng is { } b && Valid(a, b) ? (a, b) : null;
    }

    /// <summary>Values read back from the cache are JSON; values parsed from a file are plain objects.</summary>
    private static object? Clr(object? v) => v switch
    {
        System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } e => e.EnumerateArray().Select(x => Clr(x)).ToList(),
        System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Object } e => e.EnumerateObject().ToDictionary(p => p.Name, p => Clr(p.Value)),
        System.Text.Json.JsonElement e => Props.Str(e),
        _ => v
    };

    private static (double, double)? FromValue(object raw)
    {
        var v = Clr(raw);
        if (v is null) return null;
        switch (v)
        {
            case string s when Pair().Match(s) is { Success: true } m:
                return Make(m.Groups[1].Value, m.Groups[2].Value);
            case IEnumerable<object?> list when v is not string:
                var items = list.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).ToList();
                return items.Count == 2 ? Make(items[0], items[1]) : null;
            case IDictionary<string, object?> map:
                var lat = Num(map.AsReadOnly(), "lat", "latitude");
                var lng = Num(map.AsReadOnly(), "lng", "lon", "long", "longitude");
                return lat is { } a && lng is { } b && Valid(a, b) ? (a, b) : null;
            default:
                return null;
        }
    }

    private static (double, double)? Make(string? a, string? b) =>
        double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng) && Valid(lat, lng)
            ? (lat, lng) : null;

    private static double? Num(IReadOnlyDictionary<string, object?> p, params string[] keys)
    {
        foreach (var k in keys)
            if (p.TryGetValue(k, out var v) && double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }

    private static bool Valid(double lat, double lng) => lat is >= -90 and <= 90 && lng is >= -180 and <= 180 && !(lat == 0 && lng == 0);

    public IReadOnlyList<Place> Places(string? workspace, Actor actor)
    {
        writes.Permissions.Demand(actor, Permission.Read, "Read note locations");
        var list = new List<Place>();
        foreach (var n in writes.Cache.List(workspace, includeArchived: false, limit: 1_000_000))
            if (Parse(n.Properties) is { } p)
                list.Add(new Place(n.Id, n.Title, n.Type, n.Workspace, Math.Round(p.Lat, 6), Math.Round(p.Lng, 6), Props.Str(n.Properties, "place"), n.Tags));
        return list;
    }
}
