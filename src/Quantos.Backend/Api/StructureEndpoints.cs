using System.Text.Json;
using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Structure;
using Quantos.Core.Tasks;

namespace Quantos.Backend.Api;

public sealed record SupertagBody(string? Name, string? Parent, string? Color, string? Icon, string? Scope, List<Dictionary<string, JsonElement>>? Fields,
    string? Template, bool? AutoApplyTemplate, string? Layout, string? Description);
public sealed record MigrateBody(string Field, string? ToType, string? ToKey);
public sealed record ApplyTemplateBody(string TemplateId);
public sealed record TemplateCreateBody(string Title, string? Body);
public sealed record QueryTextBody(string Text, string? Workspace);
public sealed record BatchRow(string Title, Dictionary<string, JsonElement>? Properties, string? Body);
public sealed record BatchBody(List<BatchRow> Rows, string? Type, List<string>? Tags, string? Workspace);

public static class StructureEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/supertags", (VaultHost host, string? workspace) => host.Require().Supertags.List(workspace));
        api.MapPost("/supertags", (VaultHost host, HttpContext ctx, SupertagBody b) => host.Require().Supertags.Create(Input(b), ctx.Actor()));
        api.MapPut("/supertags/{id}", (VaultHost host, HttpContext ctx, string id, SupertagBody b) => host.Require().Supertags.Update(id, Input(b), ctx.Actor()));
        api.MapPost("/supertags/{id}/rename", (VaultHost host, HttpContext ctx, string id, RenameBody b) => host.Require().Supertags.Rename(id, b.Title, ctx.Actor()));
        api.MapPost("/supertags/{id}/migrate", (VaultHost host, HttpContext ctx, string id, MigrateBody b) =>
        {
            var (changed, skipped) = host.Require().Supertags.Migrate(id, b.Field, b.ToType, b.ToKey, ctx.Actor());
            return new { changed, skipped };
        });

        api.MapGet("/templates", (VaultHost host) => host.Require().Templates.List());
        api.MapPost("/templates", (VaultHost host, HttpContext ctx, TemplateCreateBody b) => NodeDto.From(host.Require().Templates.Create(b.Title, b.Body ?? "", ctx.Actor())));
        api.MapPost("/nodes/{id}/apply-template", (VaultHost host, HttpContext ctx, string id, ApplyTemplateBody b) =>
            NodeDto.From(host.Require().Templates.Apply(id, b.TemplateId, ctx.Actor())));

        api.MapPost("/query", (VaultHost host, QuerySpec spec, string? workspace) =>
            host.Require().Queries.Run(spec, workspace ?? host.Settings.LastWorkspace ?? Core.Storage.Vault.DefaultWorkspace));
        api.MapPost("/query/text", (VaultHost host, QueryTextBody b) =>
            host.Require().Queries.RunText(b.Text, b.Workspace ?? host.Settings.LastWorkspace ?? Core.Storage.Vault.DefaultWorkspace));

        // Metadata dictionary (BUILD-PLAN 8C.16): every property, how often it's used, what kinds of values it holds, which supertags define it.
        api.MapGet("/dictionary", (VaultHost host, string? workspace) =>
        {
            var session = host.Require();
            var nodes = session.Cache.List(workspace, includeArchived: true, limit: 1_000_000);
            var tags = session.Supertags.List(workspace);
            return nodes.SelectMany(n => n.Properties.Select(p => (Key: p.Key, Value: p.Value, n.Type)))
                .GroupBy(x => x.Key)
                .Select(g => new
                {
                    key = g.Key,
                    count = g.Count(),
                    kinds = g.Select(x => Kind(x.Value)).Distinct().ToList(),
                    nodeTypes = g.Select(x => x.Type).Distinct().ToList(),
                    definedBy = tags.Where(t => t.AllFields.Any(f => f.Key == g.Key)).Select(t => t.Name).ToList(),
                    samples = g.Select(x => Props.Str(x.Value)).OfType<string>().Distinct().Take(5).ToList()
                })
                .OrderByDescending(x => x.count).ToList();
        });

        // Structured paste (BUILD-PLAN 8C.3): rows pasted from a spreadsheet become nodes.
        api.MapPost("/nodes/batch", (VaultHost host, HttpContext ctx, BatchBody b) =>
        {
            var session = host.Require();
            if (b.Rows.Count > 500) throw new QuantosException("Paste at most 500 rows at a time.");
            var created = new List<NodeDto>();
            foreach (var row in b.Rows.Where(r => !string.IsNullOrWhiteSpace(r.Title)))
            {
                var node = (b.Type ?? NodeTypes.Note) == NodeTypes.Task
                    ? session.Tasks.Create(new CreateTaskRequest { Title = row.Title, Workspace = b.Workspace ?? Core.Storage.Vault.DefaultWorkspace, Tags = b.Tags, Body = row.Body ?? "" }, ctx.Actor())
                    : session.Writes.Create(new CreateNodeRequest { Title = row.Title, Type = b.Type ?? NodeTypes.Note, Workspace = b.Workspace ?? Core.Storage.Vault.DefaultWorkspace, Tags = b.Tags, Body = row.Body ?? "" }, ctx.Actor());
                if (row.Properties is { Count: > 0 })
                    session.Writes.UpdateFields(node.Id, null, Json.ToClr(row.Properties), null, ctx.Actor());
                created.Add(NodeDto.From(session.Writes.Require(node.Id)));
            }
            return created;
        });
    }

    private static string Kind(object? v) => v switch
    {
        JsonElement { ValueKind: JsonValueKind.Number } => "number",
        JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False } => "checkbox",
        JsonElement { ValueKind: JsonValueKind.Array } => "list",
        JsonElement { ValueKind: JsonValueKind.Object } => "object",
        JsonElement e when e.ValueKind == JsonValueKind.String && e.GetString() is { } s =>
            System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}$") ? "date"
            : System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}T") ? "datetime"
            : s.StartsWith("[[") ? "node" : double.TryParse(s, out _) ? "number" : s is "true" or "false" ? "checkbox" : "text",
        _ => "text"
    };

    private static SupertagInput Input(SupertagBody b) => new(b.Name, b.Parent, b.Color, b.Icon, b.Scope,
        b.Fields?.Select(f => f.ToDictionary(kv => kv.Key, kv => Json.ToClr(kv.Value))).ToList(), b.Template, b.AutoApplyTemplate, b.Layout, b.Description);
}
