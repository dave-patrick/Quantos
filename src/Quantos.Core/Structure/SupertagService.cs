using System.Globalization;
using System.Text.RegularExpressions;
using Quantos.Core.Cache;
using Quantos.Core.Markdown;
using Quantos.Core.Security;
using Quantos.Core.Storage;
using Quantos.Core.Tasks;

namespace Quantos.Core.Structure;

public static class FieldTypes
{
    public static readonly string[] All = ["text", "number", "date", "datetime", "checkbox", "select", "multiselect", "node", "nodes", "url", "email", "person", "status"];
}

public sealed record FieldDef(string Name, string Key, string Type, IReadOnlyList<string>? Options, bool Required, object? Default, string? Description, string? Inherited);

public sealed record SupertagDto(
    string Id, string Name, string? Parent, string? Color, string? Icon, string Scope, IReadOnlyList<FieldDef> Fields, IReadOnlyList<FieldDef> AllFields,
    string? Template, bool AutoApplyTemplate, string? Layout, string Description, int Count, string Path, string? Error, string? Created);

public sealed record SupertagInput(string? Name, string? Parent, string? Color, string? Icon, string? Scope, List<Dictionary<string, object?>>? Fields,
    string? Template, bool? AutoApplyTemplate, string? Layout, string? Description);

/// <summary>
/// Supertags (BUILD-PLAN section 5): global definitions by default, optional workspace scope,
/// inheritance with loops blocked, namespaced keys when names conflict, linked templates and
/// per-type layouts. Schema changes never rewrite data unless the user explicitly migrates.
/// </summary>
public sealed partial class SupertagService(WriteService writes)
{
    private CacheIndex Cache => writes.Cache;

    public static string FieldKey(string name) => Regex.Replace(name.Trim().ToLowerInvariant(), @"[^\p{L}\p{N}]+", "_").Trim('_');
    public static string NormalizeTag(string name) => name.Trim().TrimStart('#').Trim();

    public IReadOnlyList<SupertagDto> List(string? workspace = null)
    {
        var defs = Cache.List(type: NodeTypes.Supertag, includeArchived: false, limit: 100_000)
            .Where(d => d.Workspace.Length == 0 || workspace is null || d.Workspace == workspace)
            .ToList();
        // A workspace definition overrides a global one with the same name.
        var byName = new Dictionary<string, NodeSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in defs.OrderBy(d => d.Workspace.Length)) byName[d.Title] = d;
        var counts = TagCounts(workspace);

        var own = byName.Values.ToDictionary(d => d.Title, d => OwnFields(d), StringComparer.OrdinalIgnoreCase);
        // Namespacing: when two tags define the same key with different types, the newer definition's key becomes "tag.key".
        var owners = new Dictionary<string, (string Tag, string Type, string Created)>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in byName.Values.OrderBy(d => d.Created ?? "", StringComparer.Ordinal))
        {
            var fields = own[d.Title];
            for (var i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                if (owners.TryGetValue(f.Key, out var o) && !o.Tag.Equals(d.Title, StringComparison.OrdinalIgnoreCase) && o.Type != f.Type && !IsAncestor(byName, d.Title, o.Tag))
                    fields[i] = f with { Key = FieldKey(d.Title) + "." + f.Key };
                else owners.TryAdd(f.Key, (d.Title, f.Type, d.Created ?? ""));
            }
        }

        return byName.Values.Select(d =>
        {
            var p = d.Properties;
            var (chain, error) = Chain(byName, d.Title);
            var all = new List<FieldDef>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ancestor in chain.AsEnumerable().Reverse())
            {
                foreach (var f in own[ancestor])
                {
                    var inherited = ancestor.Equals(d.Title, StringComparison.OrdinalIgnoreCase) ? null : ancestor;
                    // A child redefining an inherited field with another type gets its own namespaced key.
                    var existing = all.FindIndex(x => x.Key.Equals(f.Key, StringComparison.OrdinalIgnoreCase));
                    if (existing >= 0)
                    {
                        if (all[existing].Type == f.Type) { all[existing] = f with { Inherited = inherited }; continue; }
                        all.Add(f with { Key = FieldKey(ancestor) + "." + f.Key, Inherited = inherited });
                        continue;
                    }
                    if (seen.Add(f.Key)) all.Add(f with { Inherited = inherited });
                }
            }
            return new SupertagDto(d.Id, d.Title, chain.Count > 1 || error is not null ? Props.Str(p, "parent") : Props.Str(p, "parent"),
                Props.Str(p, "color"), Props.Str(p, "icon"), d.Workspace, own[d.Title], all,
                Props.LinkId(Props.Str(p, "template")), string.Equals(Props.Str(p, "auto_apply_template"), "true", StringComparison.OrdinalIgnoreCase),
                Props.Str(p, "layout"), Props.Str(p, "description") ?? "", counts.GetValueOrDefault(d.Title.ToLowerInvariant()), d.Path, error, d.Created);
        }).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public SupertagDto? Get(string nameOrId, string? workspace = null) =>
        List(workspace).FirstOrDefault(t => t.Id == nameOrId || t.Name.Equals(NormalizeTag(nameOrId), StringComparison.OrdinalIgnoreCase));

    /// <summary>The tag and every tag that inherits from it (used by queries: #event matches #meeting if meeting extends event).</summary>
    public HashSet<string> WithDescendants(string tag, string? workspace = null)
    {
        var all = List(workspace);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { NormalizeTag(tag) };
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var t in all)
                if (t.Parent is not null && result.Contains(t.Parent) && result.Add(t.Name)) changed = true;
        }
        return result;
    }

    private static bool IsAncestor(Dictionary<string, NodeSummary> byName, string tag, string candidate)
    {
        var (chain, _) = Chain(byName, tag);
        return chain.Contains(candidate, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The tag followed by its ancestors. A loop stops the walk and is reported, never followed.</summary>
    private static (List<string> Chain, string? Error) Chain(Dictionary<string, NodeSummary> byName, string tag)
    {
        var chain = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var cursor = tag; cursor is not null && byName.ContainsKey(cursor);)
        {
            if (!seen.Add(cursor)) return (chain, $"Inheritance loop: {string.Join(" → ", chain)} → {cursor}. The loop is ignored.");
            chain.Add(byName[cursor].Title);
            cursor = Props.Str(byName[cursor].Properties, "parent");
        }
        return (chain, null);
    }

    private static List<FieldDef> OwnFields(NodeSummary d)
    {
        var list = new List<FieldDef>();
        foreach (var f in Props.Maps(d.Properties, "fields"))
        {
            var name = f.GetValueOrDefault("name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var type = f.GetValueOrDefault("type") is { } t && FieldTypes.All.Contains(t) ? t : "text";
            var options = OptionsOf(d.Properties, list.Count);
            list.Add(new FieldDef(name, f.GetValueOrDefault("key") ?? FieldKey(name), type, options,
                string.Equals(f.GetValueOrDefault("required"), "true", StringComparison.OrdinalIgnoreCase),
                f.GetValueOrDefault("default"), f.GetValueOrDefault("description"), null));
        }
        return list;
    }

    private static List<string>? OptionsOf(IReadOnlyDictionary<string, object?> props, int index)
    {
        if (!props.TryGetValue("fields", out var v)) return null;
        object? item = v switch
        {
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } e when e.GetArrayLength() > index => e[index],
            System.Collections.IList l when l.Count > index => l[index],
            _ => null
        };
        var options = item switch
        {
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Object } o when o.TryGetProperty("options", out var opts) => (object?)opts,
            System.Collections.IDictionary d when d.Contains("options") => d["options"],
            _ => null
        };
        return options is null ? null : Props.List(new Dictionary<string, object?> { ["o"] = options }, "o");
    }

    private Dictionary<string, int> TagCounts(string? workspace)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in Cache.List(workspace, includeArchived: false, limit: 1_000_000))
            foreach (var t in n.Tags) counts[t.ToLowerInvariant()] = counts.GetValueOrDefault(t.ToLowerInvariant()) + 1;
        return counts;
    }

    // ---- changes ------------------------------------------------------------------------------

    public SupertagDto Create(SupertagInput input, Actor actor)
    {
        var name = NormalizeTag(input.Name ?? "");
        if (name.Length == 0 || name.Contains(' ')) throw new QuantosException("A supertag name can't be empty or contain spaces.");
        var scope = input.Scope ?? "";
        if (List(scope.Length == 0 ? null : scope).Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && t.Scope == scope))
            throw new QuantosException($"#{name} already exists.", 409);
        var props = Properties(input, name, scope);
        var node = writes.Create(new CreateNodeRequest { Title = name, Type = NodeTypes.Supertag, Workspace = scope, Body = input.Description ?? "", Properties = props }, actor);
        return Get(node.Id, scope.Length == 0 ? null : scope) ?? throw new QuantosException("The supertag wasn't indexed.");
    }

    public SupertagDto Update(string id, SupertagInput input, Actor actor)
    {
        var current = Get(id) ?? throw new QuantosException("Supertag not found.", 404);
        var props = Properties(input with { Name = current.Name }, current.Name, current.Scope);
        var remove = new List<string>();
        foreach (var key in new[] { "parent", "color", "icon", "template", "layout" })
            if (props.TryGetValue(key, out var v) && v is null) { props.Remove(key); remove.Add(key); }
        writes.UpdateFields(id, null, props, remove, actor, input.Description);
        return Get(id) ?? throw new QuantosException("Supertag not found.", 404);
    }

    private Dictionary<string, object?> Properties(SupertagInput input, string name, string scope)
    {
        var props = new Dictionary<string, object?>();
        if (input.Parent is not null)
        {
            var parent = NormalizeTag(input.Parent);
            if (parent.Length == 0) props["parent"] = null;
            else
            {
                // Block inheritance loops before they're written.
                var all = List(scope.Length == 0 ? null : scope).ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
                for (var cursor = parent; cursor is not null; cursor = all.GetValueOrDefault(cursor)?.Parent)
                    if (cursor.Equals(name, StringComparison.OrdinalIgnoreCase)) throw new QuantosException($"#{name} can't inherit from #{parent}: that would make a loop.");
                props["parent"] = parent;
            }
        }
        if (input.Color is not null) props["color"] = input.Color.Length == 0 ? null : input.Color;
        if (input.Icon is not null) props["icon"] = input.Icon.Length == 0 ? null : input.Icon;
        if (input.Layout is not null) props["layout"] = input.Layout.Length == 0 ? null : input.Layout;
        if (input.Template is not null) props["template"] = input.Template.Length == 0 ? null : input.Template;
        if (input.AutoApplyTemplate is not null) props["auto_apply_template"] = input.AutoApplyTemplate.Value;
        if (input.Fields is not null)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fields = new List<object?>();
            foreach (var f in input.Fields)
            {
                var fieldName = Props.Str(f.GetValueOrDefault("name"))?.Trim();
                if (string.IsNullOrEmpty(fieldName)) continue;
                var type = Props.Str(f.GetValueOrDefault("type")) ?? "text";
                if (!FieldTypes.All.Contains(type)) throw new QuantosException($"Unknown field type '{type}'.");
                if (!keys.Add(FieldKey(fieldName))) throw new QuantosException($"Two fields are called '{fieldName}'.");
                var map = new Dictionary<string, object?> { ["name"] = fieldName, ["type"] = type };
                if (f.GetValueOrDefault("options") is { } opts) map["options"] = Props.List(new Dictionary<string, object?> { ["o"] = opts }, "o");
                if (Props.Str(f.GetValueOrDefault("required")) is "true" or "True") map["required"] = true;
                if (Props.Str(f.GetValueOrDefault("description")) is { } desc) map["description"] = desc;
                if (Props.Str(f.GetValueOrDefault("default")) is { } def) map["default"] = def;
                fields.Add(map);
            }
            props["fields"] = fields;
        }
        return props;
    }

    /// <summary>
    /// Renames a tag everywhere it's really used: tags lists, #hashtags in bodies (not in code),
    /// and child definitions. History snapshots are left as they were. One journaled operation.
    /// </summary>
    public SupertagDto Rename(string id, string newName, Actor actor)
    {
        var tag = Get(id) ?? throw new QuantosException("Supertag not found.", 404);
        newName = NormalizeTag(newName);
        if (newName.Length == 0 || newName.Contains(' ')) throw new QuantosException("A supertag name can't be empty or contain spaces.");
        if (newName.Equals(tag.Name, StringComparison.Ordinal)) return tag;
        if (Get(newName) is { } clash && clash.Id != id) throw new QuantosException($"#{newName} already exists.", 409);
        writes.Permissions.Demand(actor, Permission.CreateEdit, $"Rename #{tag.Name} to #{newName}");

        var op = new JournalOperation { Description = $"Rename #{tag.Name} to #{newName}" };
        var touched = new List<string>();
        foreach (var n in Cache.List(includeArchived: true, limit: 1_000_000))
        {
            var usesTag = n.Tags.Any(t => t.Equals(tag.Name, StringComparison.OrdinalIgnoreCase));
            var isChild = n.Type == NodeTypes.Supertag && string.Equals(Props.Str(n.Properties, "parent"), tag.Name, StringComparison.OrdinalIgnoreCase);
            var node = writes.Get(n.Id);
            if (node is null || node.ReadOnly) continue;
            var doc = FrontmatterDocument.Parse(node.Content);
            var changed = false;
            if (usesTag)
            {
                doc.Set(NodeKeys.Tags, doc.GetStringList(NodeKeys.Tags).Select(t => t.TrimStart('#').Equals(tag.Name, StringComparison.OrdinalIgnoreCase) ? newName : t).ToList());
                changed = true;
            }
            if (isChild) { doc.Set("parent", newName); changed = true; }
            if (n.Id == id) { doc.Set(NodeKeys.Title, newName); changed = true; }
            var body = ReplaceHashtag(doc.Body, tag.Name, newName);
            if (body != doc.Body) { doc.SetBody(body); changed = true; }
            if (!changed) continue;
            doc.Set(NodeKeys.Modified, writes.Clock.Stamp());
            op.Steps.Add(JournalStep.Write(node.Path, node.Content, doc.ToString()));
            touched.Add(node.Path);
        }
        if (op.Steps.Count > 0) writes.Exclusive(() => { writes.Journal.Execute(op); return 0; });
        foreach (var path in touched)
        {
            var updated = writes.Index(writes.Vault.Absolute(path));
            writes.Changes.Publish(new ChangeEvent(ChangeKind.Updated, updated.Id, path, Hash: updated.Hash, Origin: actor.ToString()));
        }
        return Get(id) ?? throw new QuantosException("Supertag not found.", 404);
    }

    private static string ReplaceHashtag(string body, string from, string to) =>
        Regex.Replace(body, @"(?<![\w&/#])#" + Regex.Escape(from) + @"(?![\w\-/])", m => Links.InCode(body, m.Index) ? m.Value : "#" + to, RegexOptions.IgnoreCase);

    /// <summary>
    /// Explicit data migration after a schema change (the user chose it): converts a field's values
    /// on every node with the tag, or moves them to a new key. Values that don't convert are kept as they were.
    /// </summary>
    public (int Changed, int Skipped) Migrate(string id, string fieldKey, string? toType, string? toKey, Actor actor)
    {
        var tag = Get(id) ?? throw new QuantosException("Supertag not found.", 404);
        var tags = WithDescendants(tag.Name);
        int changed = 0, skipped = 0;
        foreach (var n in Cache.List(includeArchived: true, limit: 1_000_000).Where(n => n.Tags.Any(tags.Contains)))
        {
            if (!n.Properties.TryGetValue(fieldKey, out var raw) || raw is null) continue;
            var value = Props.Str(raw);
            object? converted = value;
            if (toType is not null)
            {
                converted = toType switch
                {
                    "number" => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null,
                    "date" => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                    "checkbox" => value?.ToLowerInvariant() switch { "true" or "yes" or "1" or "x" => true, "false" or "no" or "0" or "" => false, _ => null },
                    "multiselect" or "nodes" => Props.List(n.Properties, fieldKey),
                    _ => value
                };
                if (converted is null) { skipped++; continue; }
            }
            var set = new Dictionary<string, object?> { [toKey ?? fieldKey] = converted };
            var remove = toKey is not null && toKey != fieldKey ? new[] { fieldKey } : [];
            var r = writes.UpdateFields(n.Id, null, set, remove, actor);
            if (r.Status == SaveStatus.Saved) changed++; else if (r.Status != SaveStatus.Unchanged) skipped++;
        }
        return (changed, skipped);
    }
}
