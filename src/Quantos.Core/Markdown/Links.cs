using System.Text.RegularExpressions;

namespace Quantos.Core.Markdown;

public enum LinkKind { Link, Embed }

/// <summary>A wiki link found in a body: <c>[[target#fragment|display]]</c> or <c>![[...]]</c>.</summary>
public sealed record WikiLink(LinkKind Kind, string Target, string? Fragment, string? Display, int Start, int Length)
{
    public bool IsResolved => Ulid.IsValid(Target);

    /// <summary>For an unresolved link: optional <c>workspace:</c> prefix and the title text.</summary>
    public (string? Workspace, string Title) TitleTarget
    {
        get
        {
            var colon = Target.IndexOf(':');
            return colon > 0 ? (Target[..colon].Trim(), Target[(colon + 1)..].Trim()) : (null, Target.Trim());
        }
    }
}

public static partial class Links
{
    public static IEnumerable<WikiLink> Find(string body)
    {
        foreach (Match m in WikiPattern().Matches(body))
        {
            if (InCode(body, m.Index)) continue;
            yield return new WikiLink(
                m.Groups["bang"].Success ? LinkKind.Embed : LinkKind.Link,
                m.Groups["target"].Value.Trim(),
                m.Groups["frag"].Success ? m.Groups["frag"].Value : null,
                m.Groups["display"].Success ? m.Groups["display"].Value : null,
                m.Index, m.Length);
        }
    }

    public static string Format(LinkKind kind, string target, string? fragment, string? display) =>
        (kind == LinkKind.Embed ? "!" : "") + "[[" + target + (fragment is null ? "" : "#" + fragment) +
        (display is null ? "" : "|" + display) + "]]";

    /// <summary>Rewrites links in <paramref name="body"/>; the callback returns the replacement or null to keep the link.</summary>
    public static string Rewrite(string body, Func<WikiLink, string?> replace)
    {
        var links = Find(body).ToList();
        if (links.Count == 0) return body;
        var sb = new System.Text.StringBuilder(body.Length);
        var pos = 0;
        foreach (var link in links)
        {
            var replacement = replace(link);
            if (replacement is null) continue;
            sb.Append(body, pos, link.Start - pos).Append(replacement);
            pos = link.Start + link.Length;
        }
        sb.Append(body, pos, body.Length - pos);
        return sb.ToString();
    }

    /// <summary>Ids of task nodes bound to inline task lines (<c>&lt;!--task:ID--&gt;</c>).</summary>
    public static IEnumerable<(string Id, bool Checked, string Text, int LineStart)> InlineTasks(string body)
    {
        foreach (Match m in InlineTaskPattern().Matches(body))
            yield return (m.Groups["id"].Value, m.Groups["check"].Value is "x" or "X", m.Groups["text"].Value.Trim(), m.Index);
    }

    /// <summary>True when <paramref name="index"/> is inside a fenced code block or inline code span.</summary>
    public static bool InCode(string text, int index)
    {
        var fences = 0;
        var lineStart = 0;
        while (lineStart < index)
        {
            var lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0 || lineEnd >= index) break;
            var line = text.AsSpan(lineStart, lineEnd - lineStart).TrimStart();
            if (line.StartsWith("```") || line.StartsWith("~~~")) fences++;
            lineStart = lineEnd + 1;
        }
        if (fences % 2 == 1) return true;
        // Inline code on the same line: odd number of backticks before the index.
        var ticks = 0;
        for (var i = lineStart; i < index; i++) if (text[i] == '`') ticks++;
        return ticks % 2 == 1;
    }

    [GeneratedRegex(@"(?<bang>!)?\[\[(?<target>[^\[\]\|#\n]+?)(?:#(?<frag>[^\[\]\|\n]+))?(?:\|(?<display>[^\[\]\n]+))?\]\]")]
    private static partial Regex WikiPattern();

    [GeneratedRegex(@"^[ \t]*[-*+][ \t]+\[(?<check>[ xX])\][ \t]+(?<text>.*?)[ \t]*<!--task:(?<id>[0-9A-HJKMNP-TV-Z]{26})-->", RegexOptions.Multiline)]
    private static partial Regex InlineTaskPattern();
}
