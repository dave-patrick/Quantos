using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Markdown;

namespace Quantos.Backend.Api;

public sealed record ExtractBody(string Text, string Title, bool? Embed);
public sealed record MergeBody(string Other);
public sealed record SyncedCreateBody(string Content, string? Title);

public static class RelationEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/nodes/{id}/extract", (VaultHost host, HttpContext ctx, string id, ExtractBody b) =>
        {
            var (source, extracted) = host.Require().Writes.Extract(id, b.Text, b.Title, b.Embed ?? false, ctx.Actor());
            return new { source = NodeDto.From(source), extracted = NodeDto.From(extracted) };
        });

        api.MapPost("/nodes/{id}/merge", (VaultHost host, HttpContext ctx, string id, MergeBody b) =>
            NodeDto.From(host.Require().Writes.Merge(id, b.Other, ctx.Actor())));

        // Block references and heading embeds: ![[id#^block]] / ![[id#Heading]].
        api.MapGet("/nodes/{id}/fragment", (VaultHost host, string id, string f) =>
        {
            var node = host.Require().Writes.Get(id);
            if (node is null) return Results.NotFound(new { error = "Not found." });
            var text = Links.Fragment(node.Body, f);
            return text is null ? Results.NotFound(new { error = $"“{f}” isn't in {node.Title} any more." }) : Results.Ok(new { id, title = node.Title, fragment = f, text });
        });

        api.MapGet("/synced-blocks", (VaultHost host) => host.Require().SyncedBlocks.List());
        api.MapPost("/synced-blocks", (VaultHost host, HttpContext ctx, SyncedCreateBody b) =>
        {
            var node = host.Require().SyncedBlocks.Create(b.Content, b.Title, ctx.Actor());
            return new { id = node.Id, markdown = Core.Structure.SyncedBlockService.Wrap(node.Id, b.Content) };
        });

        // Graph neighbourhood (BUILD-PLAN 8C.15): links in both directions, up to two hops out.
        api.MapGet("/graph", (VaultHost host, string? id, int? hops, string? workspace) =>
        {
            var cache = host.Require().Cache;
            var nodes = new Dictionary<string, (string Title, string Type, string Workspace, int Depth)>();
            var edges = new HashSet<(string From, string To, string Kind)>();
            if (id is null)
            {
                // Workspace overview: the most connected notes.
                var all = cache.List(workspace, includeArchived: false, limit: 100_000);
                var degree = all.ToDictionary(n => n.Id, n => cache.Backlinks(n.Id).Count + cache.OutLinks(n.Id).Count(o => !o.Target.StartsWith('?')));
                foreach (var n in all.OrderByDescending(n => degree[n.Id]).Take(150)) nodes[n.Id] = (n.Title, n.Type, n.Workspace, 0);
                foreach (var n in nodes.Keys.ToList())
                    foreach (var o in cache.OutLinks(n).Where(o => nodes.ContainsKey(o.Target))) edges.Add((n, o.Target, o.Kind));
            }
            else
            {
                var root = cache.Get(id);
                if (root is null) return Results.NotFound();
                nodes[id] = (root.Title, root.Type, root.Workspace, 0);
                var frontier = new List<string> { id };
                for (var depth = 1; depth <= Math.Clamp(hops ?? 2, 1, 2); depth++)
                {
                    var next = new List<string>();
                    foreach (var n in frontier)
                    {
                        foreach (var o in cache.OutLinks(n).Where(o => !o.Target.StartsWith('?')))
                        {
                            edges.Add((n, o.Target, o.Kind));
                            if (!nodes.ContainsKey(o.Target) && cache.Get(o.Target) is { } t) { nodes[o.Target] = (t.Title, t.Type, t.Workspace, depth); next.Add(o.Target); }
                        }
                        foreach (var b in cache.Backlinks(n))
                        {
                            edges.Add((b.SourceId, n, b.Kind));
                            if (!nodes.ContainsKey(b.SourceId) && cache.Get(b.SourceId) is { } s) { nodes[b.SourceId] = (s.Title, s.Type, s.Workspace, depth); next.Add(b.SourceId); }
                        }
                    }
                    frontier = next;
                }
            }
            return Results.Ok(new
            {
                nodes = nodes.Select(kv => new { id = kv.Key, title = kv.Value.Title, type = kv.Value.Type, workspace = kv.Value.Workspace, depth = kv.Value.Depth }),
                edges = edges.Where(e => nodes.ContainsKey(e.From) && nodes.ContainsKey(e.To) && e.From != e.To).Select(e => new { from = e.From, to = e.To, kind = e.Kind })
            });
        });
    }
}
