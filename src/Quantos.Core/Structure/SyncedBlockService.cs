using Quantos.Core.Markdown;
using Quantos.Core.Security;

namespace Quantos.Core.Structure;

public sealed record SyncedBlockDto(string Id, string Title, string Content, int Uses, string? Modified);

/// <summary>
/// Synced blocks (BUILD-PLAN 8C.12): the canonical text lives in synced_blocks/&lt;id&gt;.md; every
/// note that includes it holds a readable copy between &lt;!--sync:id--&gt; markers, so files stay
/// meaningful outside the app. Editing any copy updates the canonical file and every other copy.
/// Each overwritten version stays in history.
/// </summary>
public sealed class SyncedBlockService
{
    private readonly WriteService _writes;
    [ThreadStatic] private static int _depth;

    public SyncedBlockService(WriteService writes)
    {
        _writes = writes;
        writes.BodyWritten += OnBodyWritten;
    }

    public IReadOnlyList<SyncedBlockDto> List() =>
        _writes.Cache.List(type: NodeTypes.SyncedBlock, limit: 10_000)
            .Select(s => _writes.Get(s.Id) is { } n ? new SyncedBlockDto(n.Id, n.Title, n.Body.Trim(), _writes.Cache.Backlinks(n.Id).Count(b => b.Kind == "sync"), n.Modified) : null)
            .OfType<SyncedBlockDto>().ToList();

    public Node Create(string content, string? title, Actor actor) =>
        _writes.Create(new CreateNodeRequest
        {
            Title = string.IsNullOrWhiteSpace(title) ? FirstWords(content) : title.Trim(), Type = NodeTypes.SyncedBlock, Workspace = "", Body = content.Trim() + "\n"
        }, actor);

    public static string Wrap(string id, string content) => $"<!--sync:{id}-->\n{content.Trim()}\n<!--/sync-->";

    private void OnBodyWritten(Node node, Actor actor)
    {
        if (_depth > 0) return;
        _depth++;
        try
        {
            if (node.Type == NodeTypes.SyncedBlock) { Propagate(node.Id, node.Body.Trim(), except: null, actor); return; }
            foreach (var region in Links.SyncRegions(node.Body))
            {
                var canonical = _writes.Get(region.Id);
                if (canonical is null) continue; // deleted block: the copy stays as ordinary text
                var content = region.Content.Trim();
                if (content == canonical.Body.Trim()) continue;
                _writes.UpdateFields(region.Id, null, null, null, actor, content + "\n");
                Propagate(region.Id, content, except: node.Id, actor);
            }
        }
        finally { _depth--; }
    }

    private void Propagate(string blockId, string content, string? except, Actor actor)
    {
        foreach (var referrer in _writes.Cache.Backlinks(blockId).Where(b => b.Kind == "sync").Select(b => b.SourceId).Distinct())
        {
            if (referrer == except) continue;
            var note = _writes.Get(referrer);
            if (note is null || note.ReadOnly) continue;
            var body = note.Body;
            foreach (var region in Links.SyncRegions(body).Where(r => r.Id == blockId).Reverse())
                if (region.Content.Trim() != content)
                    body = body[..region.ContentStart] + content + body[region.ContentEnd..];
            if (body != note.Body) _writes.UpdateFields(referrer, null, null, null, actor, body);
        }
    }

    private static string FirstWords(string text)
    {
        var line = text.Trim().Split('\n')[0].TrimStart('#', '-', '*', ' ');
        return line.Length > 50 ? line[..50].TrimEnd() + "…" : line.Length == 0 ? "Synced block" : line;
    }
}
