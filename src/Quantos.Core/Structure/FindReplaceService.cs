using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Quantos.Core.Security;

namespace Quantos.Core.Structure;

public sealed record ReplaceQuery(string Find, string Replace, bool Regex = false, bool MatchCase = false, bool WholeWord = false,
    string? Workspace = null, IReadOnlyList<string>? Ids = null);
public sealed record ReplaceSample(int Line, string Before, string After);
public sealed record ReplaceNote(string Id, string Title, string Path, int Count, bool Locked, IReadOnlyList<ReplaceSample> Samples);
public sealed record ReplacePreview(IReadOnlyList<ReplaceNote> Notes, int Total, bool Truncated);
public sealed record ReplaceOutcome(string? UndoId, int Notes, int Replacements, IReadOnlyList<string> Skipped);

/// <summary>
/// Batch find and replace across a workspace's notes (Release 1.1). Only note text changes, never
/// frontmatter, links or task markers, so links and tasks keep working (renaming a note is how link text changes). Every changed note keeps
/// its old text in history, and the whole batch can be undone while the notes are unchanged since.
/// </summary>
public sealed partial class FindReplaceService(WriteService writes)
{
    private const int MaxNotes = 500, MaxSamples = 6;
    private readonly ConcurrentDictionary<string, List<(string Id, string Before, string AfterHash)>> _undo = new();

    /// <summary>Wikilinks and embeds (their text follows the linked note's title) and HTML comments such as task markers.</summary>
    [GeneratedRegex(@"\[\[[^\]\n]*\]\]|<!--[\s\S]*?-->")]
    private static partial Regex Protected();

    [GeneratedRegex(@"\[\[[0-9A-HJKMNP-TV-Z]{26}\||[ \t]*<!--task:[0-9A-HJKMNP-TV-Z]{26}-->")]
    private static partial Regex Noise();

    public static Regex Build(ReplaceQuery q)
    {
        if (string.IsNullOrEmpty(q.Find)) throw new QuantosException("Type something to find.");
        var pattern = q.Regex ? q.Find : Regex.Escape(q.Find);
        if (q.WholeWord) pattern = $@"(?<![\p{{L}}\p{{N}}_])(?:{pattern})(?![\p{{L}}\p{{N}}_])";
        var options = RegexOptions.Multiline | (q.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        try { return new Regex(pattern, options, TimeSpan.FromSeconds(1)); }
        catch (ArgumentException e) { throw new QuantosException("That pattern isn't a valid regular expression: " + e.Message); }
    }

    /// <summary>Replaces in a note body, skipping protected ranges. Returns the new body and how many changed.</summary>
    public static (string Body, int Count) ReplaceIn(string body, Regex re, ReplaceQuery q)
    {
        var guarded = Protected().Matches(body).Select(m => (m.Index, End: m.Index + m.Length)).ToList();
        var count = 0;
        var result = re.Replace(body, m =>
        {
            if (m.Length == 0 || guarded.Any(g => m.Index < g.End && m.Index + m.Length > g.Index)) return m.Value;
            count++;
            return q.Regex ? m.Result(q.Replace) : q.Replace;
        });
        return (result, count);
    }

    public ReplacePreview Preview(ReplaceQuery q, Actor actor)
    {
        writes.Permissions.Demand(actor, Permission.Read, "Find text across notes");
        var re = Build(q);
        var notes = new List<ReplaceNote>();
        var total = 0;
        foreach (var node in Candidates(q))
        {
            var (after, count) = ReplaceIn(node.Body, re, q);
            if (count == 0) continue;
            total += count;
            if (notes.Count >= MaxNotes) continue;
            notes.Add(new ReplaceNote(node.Id, node.Title, node.Path, count, node.Locked, Samples(node.Body, after)));
        }
        return new ReplacePreview(notes, total, notes.Count >= MaxNotes);
    }

    public ReplaceOutcome Apply(ReplaceQuery q, Actor actor)
    {
        var re = Build(q);
        var undo = new List<(string, string, string)>();
        var skipped = new List<string>();
        var replaced = 0;
        foreach (var node in Candidates(q))
        {
            var (after, count) = ReplaceIn(node.Body, re, q);
            if (count == 0) continue;
            var result = writes.UpdateFields(node.Id, node.Hash, null, null, actor, after);
            if (result.Status == SaveStatus.Saved && result.Node is not null)
            {
                undo.Add((node.Id, node.Content, result.Node.Hash));
                replaced += count;
            }
            else if (result.Status != SaveStatus.Unchanged) skipped.Add(node.Title + (node.Locked ? " (locked)" : result.Status == SaveStatus.Conflict ? " (changed meanwhile)" : ""));
        }
        string? undoId = null;
        if (undo.Count > 0)
        {
            undoId = Ulid.New();
            _undo[undoId] = undo;
            foreach (var old in _undo.Keys.Order().SkipLast(10).ToList()) _undo.TryRemove(old, out _);
        }
        return new ReplaceOutcome(undoId, undo.Count, replaced, skipped);
    }

    /// <summary>Puts the batch's notes back, except any edited since (those keep their newer text).</summary>
    public (int Restored, IReadOnlyList<string> Kept) Undo(string undoId, Actor actor)
    {
        if (!_undo.TryRemove(undoId, out var items)) throw new QuantosException("That replace can no longer be undone. Each note's earlier text is still in its history.", 404);
        var restored = 0;
        var kept = new List<string>();
        foreach (var (id, before, afterHash) in items)
        {
            var current = writes.Get(id);
            if (current is null) continue;
            if (current.Hash != afterHash) { kept.Add(current.Title); continue; }
            writes.ReplaceContent(id, before, actor);
            restored++;
        }
        return (restored, kept);
    }

    private IEnumerable<Node> Candidates(ReplaceQuery q)
    {
        var ids = q.Ids is { Count: > 0 } ? q.Ids : writes.Cache.List(q.Workspace, includeArchived: false, limit: 1_000_000).Select(n => n.Id).ToList();
        foreach (var id in ids)
        {
            var node = writes.Get(id);
            if (node is null || node.ReadOnly) continue;
            if (q.Workspace is not null && node.Workspace != q.Workspace) continue;
            if (node.Type is NodeTypes.Template or NodeTypes.Supertag) continue;
            yield return node;
        }
    }

    private static List<ReplaceSample> Samples(string before, string after)
    {
        var a = before.Split('\n');
        var b = after.Split('\n');
        var samples = new List<ReplaceSample>();
        // Replacements can't add or remove lines unless the replacement has a line break; compare line by line when they line up.
        if (a.Length == b.Length)
        {
            for (var i = 0; i < a.Length && samples.Count < MaxSamples; i++)
                if (a[i] != b[i]) samples.Add(new ReplaceSample(i + 1, Clip(a[i]), Clip(b[i])));
        }
        else samples.Add(new ReplaceSample(0, Clip(before), Clip(after)));
        return samples;
    }

    /// <summary>Sample lines read as the note does: links by their text, without task markers.</summary>
    private static string Clip(string s)
    {
        var t = Noise().Replace(s.TrimEnd('\r'), m => m.Value.StartsWith("[[") ? "[[" : "");
        return t.Length > 240 ? t[..240] + "…" : t;
    }
}
