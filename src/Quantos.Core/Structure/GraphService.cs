using Quantos.Core.Security;

namespace Quantos.Core.Structure;

public sealed record GraphNode(string Id, string Title, string Type, string Workspace, int Degree, int Depth = 0);
public sealed record GraphEdge(string From, string To, string Kind);
public sealed record GraphData(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges, int Total);

/// <summary>
/// The global knowledge graph (Release 1.2): every note in one workspace or all of them, linked through
/// their text, properties, embeds and synced blocks, with tags as hubs when asked for. The best-connected
/// notes are kept when there are more than the limit.
/// </summary>
public sealed class GraphService(WriteService writes)
{
    public GraphData Global(string? workspace, int limit, bool tags, IReadOnlyCollection<string>? types, Actor actor)
    {
        writes.Permissions.Demand(actor, Permission.Read, "Read the knowledge graph");
        limit = Math.Clamp(limit, 20, 1000);
        var cache = writes.Cache;
        var all = cache.List(workspace, includeArchived: false, limit: 1_000_000)
            .Where(n => types is not { Count: > 0 } || types.Contains(n.Type))
            .Where(n => n.Type is not (NodeTypes.Template or NodeTypes.SyncedBlock) || types?.Contains(n.Type) == true)
            .ToDictionary(n => n.Id);

        var edges = new HashSet<GraphEdge>();
        foreach (var id in all.Keys)
            foreach (var o in cache.OutLinks(id))
                if (o.Target != id && all.ContainsKey(o.Target)) edges.Add(new GraphEdge(id, o.Target, o.Kind));

        if (tags)
            foreach (var n in all.Values)
                foreach (var t in n.Tags.Distinct(StringComparer.OrdinalIgnoreCase))
                    edges.Add(new GraphEdge(n.Id, "#" + t.ToLowerInvariant(), "tag"));

        var degree = new Dictionary<string, int>();
        foreach (var e in edges) { degree[e.From] = degree.GetValueOrDefault(e.From) + 1; degree[e.To] = degree.GetValueOrDefault(e.To) + 1; }

        // Keep the best-connected; unconnected notes are only shown when everything fits.
        var candidates = degree.Keys.Concat(all.Keys.Where(k => !degree.ContainsKey(k))).Distinct().ToList();
        var connectedCount = degree.Count;
        var keep = candidates
            .Where(k => degree.ContainsKey(k) || candidates.Count <= limit)
            .OrderByDescending(k => degree.GetValueOrDefault(k)).ThenBy(k => k, StringComparer.Ordinal)
            .Take(limit).ToHashSet();

        var nodes = keep.Select(k => k.StartsWith('#')
                ? new GraphNode(k, k, "tag", "", degree.GetValueOrDefault(k))
                : new GraphNode(k, all[k].Title, all[k].Type, all[k].Workspace, degree.GetValueOrDefault(k)))
            .ToList();
        return new GraphData(nodes, edges.Where(e => keep.Contains(e.From) && keep.Contains(e.To)).ToList(), Math.Max(connectedCount, all.Count));
    }
}
