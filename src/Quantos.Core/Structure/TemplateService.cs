using System.Globalization;
using System.Text.RegularExpressions;
using Quantos.Core.Markdown;
using Quantos.Core.Security;
using Quantos.Core.Tasks;

namespace Quantos.Core.Structure;

public sealed record TemplateDto(string Id, string Title, string Path, IReadOnlyList<string> Tags, string? Description, IReadOnlyDictionary<string, object?> Properties);

/// <summary>
/// Templates (BUILD-PLAN 8C.2) live in Vault/templates. Applying one merges its properties into a
/// node (never overwriting values already there) and adds its body with variables filled in.
/// A supertag can link a template that's applied automatically when the tag is added.
/// </summary>
public sealed partial class TemplateService
{
    private readonly WriteService _writes;
    private readonly SupertagService _tags;
    [ThreadStatic] private static int _depth;

    public TemplateService(WriteService writes, SupertagService tags)
    {
        _writes = writes;
        _tags = tags;
        writes.TagsAdded += OnTagsAdded;
    }

    public IReadOnlyList<TemplateDto> List() =>
        _writes.Cache.List(type: NodeTypes.Template, limit: 10_000).OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TemplateDto(t.Id, t.Title, t.Path, t.Tags, Props.Str(t.Properties, "description"),
                t.Properties.Where(p => p.Key != "description").ToDictionary(p => p.Key, p => p.Value))).ToList();

    public Node Create(string title, string body, Actor actor) =>
        _writes.Create(new CreateNodeRequest { Title = title, Type = NodeTypes.Template, Workspace = "", Body = body }, actor);

    /// <summary>Fills {{title}}, {{date}}, {{time}}, {{datetime}}, {{weekday}}, {{workspace}} and {{date:+N}}.</summary>
    public string Expand(string text, Node target)
    {
        var now = _writes.Clock.Now;
        var day = target.Type == NodeTypes.Daily && Props.Date(Props.Str(target.Properties, "date")) is { } d ? d : DateOnly.FromDateTime(now.DateTime);
        return Variable().Replace(text, m =>
        {
            var name = m.Groups["name"].Value.ToLowerInvariant();
            var arg = m.Groups["arg"].Success ? m.Groups["arg"].Value : null;
            return name switch
            {
                "title" => target.Title,
                "workspace" => target.Workspace,
                "date" when arg is not null && int.TryParse(arg, out var offset) => day.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "date" => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "time" => now.ToString("HH:mm", CultureInfo.InvariantCulture),
                "datetime" => now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                "weekday" => day.ToDateTime(TimeOnly.MinValue).ToString("dddd", CultureInfo.CurrentCulture),
                _ => m.Value
            };
        });
    }

    public Node Apply(string nodeId, string templateId, Actor actor)
    {
        var template = _writes.Get(templateId) ?? throw new QuantosException("Template not found.", 404);
        if (template.Type != NodeTypes.Template) throw new QuantosException("That note isn't a template.");
        var target = _writes.Require(nodeId);
        var set = new Dictionary<string, object?>();
        foreach (var (key, value) in template.Properties)
            if (key != "description" && !target.Properties.ContainsKey(key)) set[key] = value is string s ? Expand(s, target) : value;
        var tags = target.Tags.Union(template.Tags, StringComparer.OrdinalIgnoreCase).ToList();
        if (tags.Count != target.Tags.Count) set[NodeKeys.Tags] = tags;
        var body = Expand(template.Body, target);
        var newBody = target.Body.Trim().Length == 0 ? body : target.Body.TrimEnd() + "\n\n" + body;
        _depth++;
        try
        {
            var result = _writes.UpdateFields(nodeId, null, set, null, actor, newBody);
            if (result.Status is SaveStatus.Conflict or SaveStatus.Invalid or SaveStatus.NotFound) throw new QuantosException(result.Message ?? "Couldn't apply the template.");
            return _writes.Require(nodeId);
        }
        finally { _depth--; }
    }

    /// <summary>Automatic application: a supertag's linked template, applied when the tag is added.</summary>
    private void OnTagsAdded(Node node, IReadOnlyList<string> added, Actor actor)
    {
        if (_depth > 0 || node.Type is NodeTypes.Template or NodeTypes.Supertag) return;
        foreach (var tag in added)
        {
            var def = _tags.Get(tag, node.Workspace.Length > 0 ? node.Workspace : null);
            if (def is { Template: { } templateId, AutoApplyTemplate: true }) Apply(node.Id, templateId, actor);
        }
    }

    [GeneratedRegex(@"\{\{\s*(?<name>[a-zA-Z]+)(?::(?<arg>[+-]?\d+))?\s*\}\}")]
    private static partial Regex Variable();
}
