using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Quantos.Core.Cache;
using Quantos.Core.Tasks;
using YamlDotNet.Serialization;

namespace Quantos.Core.Structure;

public sealed record QueryFilter(string Field, string Op, object? Value);
public sealed record QuerySort(string Field, string Dir = "asc");

public sealed record QuerySpec
{
    public List<string>? Types { get; init; }
    public List<string>? Tags { get; init; }
    /// <summary>"active" (default), "all", or a list of workspace names.</summary>
    public object? Workspaces { get; init; }
    public string? Text { get; init; }
    public string? Folder { get; init; }
    public List<QueryFilter>? Filters { get; init; }
    public bool IncludeArchived { get; init; }
    public List<QuerySort>? Sort { get; init; }
    public List<string>? Columns { get; init; }
    public int? Limit { get; init; }
}

public sealed record QueryRow(string Id, string Title, string Type, string Workspace, string Path, IReadOnlyList<string> Tags,
    string? Created, string? Modified, bool Archived, IReadOnlyDictionary<string, object?> Properties, string? Cover = null);

public sealed record QueryResult(IReadOnlyList<QueryRow> Rows, int Total, IReadOnlyList<string> Columns, string? Error);

/// <summary>
/// Saved queries, virtual folders, embedded query blocks and editable views (BUILD-PLAN 8C) all run
/// here. Tag filters include tags that inherit from the requested one.
/// </summary>
public sealed partial class QueryService(WriteService writes, SupertagService tags)
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();

    public QueryResult Run(QuerySpec spec, string activeWorkspace)
    {
        // Every note in the vault has to be considered. A filter that only saw the first page
        // would quietly leave the rest out, and the number of results shown to the person would
        // be wrong. The cache is read a page at a time, so the whole vault is seen without holding
        // all of it at once, and only the notes that match are kept.
        var workspaces = WorkspaceList(spec.Workspaces, activeWorkspace);
        var wanted = spec.Tags is { Count: > 0 } ? spec.Tags.Select(t => tags.WithDescendants(t)).ToList() : null;
        var hits = spec.Text is { Length: > 0 } text ? writes.Cache.Search(text, null, 5000).Select(h => h.Id).ToHashSet() : null;

        bool Match(NodeSummary n)
        {
            if (workspaces is not null && !(workspaces.Contains(n.Workspace) || (n.Workspace.Length == 0 && n.Type is "template" or "supertag"))) return false;
            if (spec.Types is { Count: > 0 })
            {
                if (!spec.Types.Contains(n.Type, StringComparer.OrdinalIgnoreCase)) return false;
            }
            // Schema records (supertags, templates, synced block sources) appear only when asked for by type or browsed by folder.
            else if (spec.Folder is not { Length: > 0 } && n.Type is (NodeTypes.Supertag or NodeTypes.Template or NodeTypes.SyncedBlock)) return false;
            if (wanted is not null && !wanted.All(set => n.Tags.Any(set.Contains))) return false;
            if (spec.Folder is { Length: > 0 } folder && !n.Path.StartsWith(folder.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)) return false;
            if (hits is not null && !hits.Contains(n.Id)) return false;
            foreach (var f in spec.Filters ?? []) if (!Matches(n, f)) return false;
            return true;
        }

        var list = new List<NodeSummary>();
        var matched = new HashSet<string>();
        const int PageSize = 10_000;
        for (var offset = 0; ; offset += PageSize)
        {
            var page = writes.Cache.List(null, null, spec.IncludeArchived, PageSize, offset);
            if (page.Count == 0) break;
            foreach (var n in page)
                if (matched.Add(n.Id) && Match(n)) list.Add(n);
            if (page.Count < PageSize) break;
        }

        IOrderedEnumerable<NodeSummary>? ordered = null;
        foreach (var s in spec.Sort ?? [new QuerySort("modified", "desc")])
        {
            Func<NodeSummary, SortKey> key = n => new SortKey(Value(n, s.Field));
            var desc = s.Dir.Equals("desc", StringComparison.OrdinalIgnoreCase);
            ordered = ordered is null
                ? (desc ? list.OrderByDescending(key) : list.OrderBy(key))
                : (desc ? ordered.ThenByDescending(key) : ordered.ThenBy(key));
        }
        var sorted = (ordered ?? list.OrderBy(n => n.Title)).ToList();
        var limit = Math.Clamp(spec.Limit ?? 500, 1, 5000);
        var top = sorted.Take(limit).ToList();
        // Only look up images for the notes we're actually returning, not the whole vault.
        var needImages = top.Where(n => !(n.Properties.TryGetValue("cover", out var c) && c is string s && s.StartsWith("attachments/", StringComparison.OrdinalIgnoreCase))).Select(n => n.Id).ToList();
        var images = needImages.Count > 0 ? writes.Cache.FirstImages(needImages) : new Dictionary<string, string>();
        var rows = top.Select(n => new QueryRow(n.Id, n.Title, n.Type, n.Workspace, n.Path, n.Tags, n.Created, n.Modified, n.Archived, n.Properties,
            n.Properties.TryGetValue("cover", out var c) && c is string s && s.StartsWith("attachments/", StringComparison.OrdinalIgnoreCase) ? s : images.GetValueOrDefault(n.Id))).ToList();
        var columns = spec.Columns is { Count: > 0 } ? spec.Columns : DefaultColumns(rows);
        return new QueryResult(rows, sorted.Count, columns, null);
    }

    /// <summary>Embedded query blocks: a ```query fence holding YAML (or JSON).</summary>
    public QueryResult RunText(string text, string activeWorkspace)
    {
        try { return Run(ParseText(text), activeWorkspace); }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or JsonException or FormatException or QuantosException)
        {
            return new QueryResult([], 0, [], "This query couldn't be read: " + ex.Message);
        }
    }

    public static QuerySpec ParseText(string text)
    {
        var raw = Yaml.Deserialize<Dictionary<string, object?>>(text) ?? new();
        var map = raw.ToDictionary(kv => kv.Key.ToLowerInvariant(), kv => kv.Value);
        List<string>? Strings(string key) => map.TryGetValue(key, out var v) && v is not null ? Props.List(new Dictionary<string, object?> { ["x"] = v }, "x") : null;
        var filters = new List<QueryFilter>();
        foreach (var where in Strings("where") ?? []) filters.Add(ParseCondition(where));
        var sort = (Strings("sort") ?? []).Select(s =>
        {
            var parts = s.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new QuerySort(parts[0].TrimStart('-'), parts[0].StartsWith('-') || (parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase)) ? "desc" : "asc");
        }).ToList();
        object? workspaces = map.TryGetValue("workspace", out var w) ? Props.Str(w) : map.TryGetValue("workspaces", out var ws) ? (object?)Strings("workspaces") : null;
        return new QuerySpec
        {
            Types = Strings("type") ?? Strings("types"),
            Tags = (Strings("tag") ?? Strings("tags"))?.Select(SupertagService.NormalizeTag).ToList(),
            Workspaces = workspaces,
            Text = map.TryGetValue("text", out var t) ? Props.Str(t) : null,
            Folder = map.TryGetValue("folder", out var f) ? Props.Str(f) : null,
            Filters = filters,
            IncludeArchived = map.TryGetValue("archived", out var a) && Props.Str(a) == "true",
            Sort = sort.Count > 0 ? sort : null,
            Columns = Strings("columns"),
            Limit = map.TryGetValue("limit", out var l) && int.TryParse(Props.Str(l), out var n) ? n : null
        };
    }

    /// <summary>"status != done", "due < today", "priority <= 2", "project contains Apollo", "owner is empty".</summary>
    public static QueryFilter ParseCondition(string text)
    {
        var m = Condition().Match(text.Trim());
        if (!m.Success) throw new FormatException($"Can't read the condition “{text}”. Try “field = value”.");
        var op = m.Groups["op"].Value.ToLowerInvariant() switch
        {
            "=" or "==" or "is" => "eq", "!=" or "is not" => "neq", "<" => "lt", "<=" => "lte", ">" => "gt", ">=" => "gte",
            "contains" or "has" => "contains", "in" => "in", var o => o
        };
        var value = m.Groups["value"].Value.Trim().Trim('"', '\'');
        if (value.Equals("empty", StringComparison.OrdinalIgnoreCase)) return new QueryFilter(m.Groups["field"].Value, op == "neq" ? "notempty" : "empty", null);
        return new QueryFilter(m.Groups["field"].Value, op, value);
    }

    private static HashSet<string>? WorkspaceList(object? value, string active) => value switch
    {
        null => [active],
        string s when s.Equals("all", StringComparison.OrdinalIgnoreCase) => null,
        string s when s.Equals("active", StringComparison.OrdinalIgnoreCase) || s.Length == 0 => [active],
        string s => [s],
        JsonElement { ValueKind: JsonValueKind.String } e => WorkspaceList(e.GetString(), active),
        JsonElement { ValueKind: JsonValueKind.Array } e => e.EnumerateArray().Select(x => x.GetString() ?? "").ToHashSet(),
        IEnumerable<string> list => list.ToHashSet(),
        _ => [active]
    };

    private static bool Matches(NodeSummary n, QueryFilter f)
    {
        var value = Value(n, f.Field);
        var target = Resolve(Props.Str(f.Value));
        var values = f.Field.Equals("tags", StringComparison.OrdinalIgnoreCase) ? n.Tags.ToList() : Props.List(n.Properties, f.Field);
        switch (f.Op)
        {
            case "empty": return string.IsNullOrEmpty(value);
            case "notempty": return !string.IsNullOrEmpty(value);
            case "contains":
                return values.Any(v => Clean(v).Contains(target ?? "", StringComparison.OrdinalIgnoreCase)) || (value ?? "").Contains(target ?? "", StringComparison.OrdinalIgnoreCase);
            case "tag": return n.Tags.Contains(target ?? "", StringComparer.OrdinalIgnoreCase);
            case "in":
                var options = (target ?? "").Trim('[', ']').Split(',').Select(s => s.Trim()).ToList();
                return options.Any(o => string.Equals(Clean(value), o, StringComparison.OrdinalIgnoreCase) || values.Any(v => Clean(v).Equals(o, StringComparison.OrdinalIgnoreCase)));
            case "eq": return string.Equals(Clean(value), target, StringComparison.OrdinalIgnoreCase) || values.Any(v => Clean(v).Equals(target, StringComparison.OrdinalIgnoreCase));
            case "neq": return !string.Equals(Clean(value), target, StringComparison.OrdinalIgnoreCase) && !values.Any(v => Clean(v).Equals(target, StringComparison.OrdinalIgnoreCase));
            default:
                if (value is null || target is null) return false;
                var cmp = new SortKey(value).CompareTo(new SortKey(target));
                return f.Op switch { "lt" => cmp < 0, "lte" => cmp <= 0, "gt" => cmp > 0, "gte" => cmp >= 0, _ => false };
        }
    }

    /// <summary>today / tomorrow / yesterday / today+7 become dates.</summary>
    private static string? Resolve(string? value)
    {
        if (value is null) return null;
        var m = Relative().Match(value);
        if (!m.Success) return value;
        var day = DateOnly.FromDateTime(DateTime.Now).AddDays(m.Groups["word"].Value.ToLowerInvariant() switch { "tomorrow" => 1, "yesterday" => -1, _ => 0 });
        if (m.Groups["n"].Success) day = day.AddDays(int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture) * (m.Groups["sign"].Value == "-" ? -1 : 1));
        return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string Clean(string? v) => v is null ? "" : LinkText().Replace(v, "$1");

    public static string? Value(NodeSummary n, string field) => field.ToLowerInvariant() switch
    {
        "title" => n.Title,
        "type" => n.Type,
        "workspace" => n.Workspace,
        "created" => n.Created,
        "modified" => n.Modified,
        "path" => n.Path,
        "tags" => string.Join(", ", n.Tags),
        _ => n.Properties.TryGetValue(field, out var v) ? (Props.List(n.Properties, field) is { Count: > 1 } list ? string.Join(", ", list.Select(Clean)) : Clean(Props.Str(v))) : null
    };

    private static List<string> DefaultColumns(IReadOnlyList<QueryRow> rows)
    {
        var keys = rows.SelectMany(r => r.Properties.Keys).GroupBy(k => k).OrderByDescending(g => g.Count()).Select(g => g.Key)
            .Where(k => k is not ("tz" or "series" or "occurrence" or "source" or "reminders" or "time_block" or "my_day_order" or "rolled_over")).Take(5);
        return ["title", .. keys, "modified"];
    }

    /// <summary>Numbers compare as numbers, everything else as text (ISO dates sort correctly as text).</summary>
    private readonly record struct SortKey(string? Text) : IComparable<SortKey>
    {
        public int CompareTo(SortKey other)
        {
            if (Text is null) return other.Text is null ? 0 : 1;   // empty values sort last
            if (other.Text is null) return -1;
            if (double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) && double.TryParse(other.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                return a.CompareTo(b);
            return string.Compare(Text, other.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [GeneratedRegex(@"^(?<field>[\w\.\-]+)\s*(?<op>==|!=|<=|>=|=|<|>|is not|is|contains|has|in)\s*(?<value>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex Condition();

    [GeneratedRegex(@"^(?<word>today|tomorrow|yesterday)(?:\s*(?<sign>[+-])\s*(?<n>\d+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Relative();

    [GeneratedRegex(@"\[\[[0-9A-HJKMNP-TV-Z]{26}(?:#[^\]|]+)?\|([^\]]+)\]\]")]
    private static partial Regex LinkText();
}
