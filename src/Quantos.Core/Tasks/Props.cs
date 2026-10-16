using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace Quantos.Core.Tasks;

/// <summary>Reads property values whether they came from YAML (CLR objects) or the cache (JSON elements).</summary>
public static class Props
{
    public static string? Str(IReadOnlyDictionary<string, object?> props, string key) =>
        props.TryGetValue(key, out var v) ? Str(v) : null;

    public static string? Str(object? v) => v switch
    {
        null => null,
        string s => s.Length == 0 ? null : s,
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString() is { Length: > 0 } s ? s : null,
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        JsonElement e => e.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString()
    };

    public static int? Int(IReadOnlyDictionary<string, object?> props, string key) =>
        int.TryParse(Str(props, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    public static List<string> List(IReadOnlyDictionary<string, object?> props, string key)
    {
        if (!props.TryGetValue(key, out var v) || v is null) return [];
        return v switch
        {
            string s => s.Length == 0 ? [] : [s],
            JsonElement { ValueKind: JsonValueKind.Array } e => e.EnumerateArray().Select(x => Str(x)).OfType<string>().ToList(),
            JsonElement e => Str(e) is { } one ? [one] : [],
            IEnumerable list => list.Cast<object?>().Select(Str).OfType<string>().ToList(),
            _ => [v.ToString()!]
        };
    }

    public static List<Dictionary<string, string?>> Maps(IReadOnlyDictionary<string, object?> props, string key)
    {
        if (!props.TryGetValue(key, out var v) || v is null) return [];
        var result = new List<Dictionary<string, string?>>();
        switch (v)
        {
            case JsonElement { ValueKind: JsonValueKind.Array } e:
                foreach (var item in e.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object)
                        result.Add(item.EnumerateObject().ToDictionary(p => p.Name, p => Str(p.Value)));
                    else if (Str(item) is { } s) result.Add(new() { ["at"] = s });
                break;
            case IEnumerable list when v is not string:
                foreach (var item in list)
                    if (item is IDictionary d) result.Add(d.Keys.Cast<object>().ToDictionary(k => k.ToString()!, k => Str(d[k])));
                    else if (Str(item) is { } s) result.Add(new() { ["at"] = s });
                break;
        }
        return result;
    }

    public static Dictionary<string, string?>? Map(IReadOnlyDictionary<string, object?> props, string key)
    {
        if (!props.TryGetValue(key, out var v) || v is null) return null;
        return v switch
        {
            JsonElement { ValueKind: JsonValueKind.Object } e => e.EnumerateObject().ToDictionary(p => p.Name, p => Str(p.Value)),
            IDictionary d => d.Keys.Cast<object>().ToDictionary(k => k.ToString()!, k => Str(d[k])),
            _ => null
        };
    }

    /// <summary>The id inside a [[id|Title]] link, or the value itself if it's a bare id.</summary>
    public static string? LinkId(string? value)
    {
        if (value is null) return null;
        if (Ulid.IsValid(value)) return value;
        var m = System.Text.RegularExpressions.Regex.Match(value, @"^\[\[([0-9A-HJKMNP-TV-Z]{26})(?:\|[^\]]*)?\]\]$");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static DateOnly? Date(string? value) =>
        value is not null && value.Length >= 10 && DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
