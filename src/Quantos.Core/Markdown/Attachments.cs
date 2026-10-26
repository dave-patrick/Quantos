using System.Text.RegularExpressions;

namespace Quantos.Core.Markdown;

public static partial class Attachments
{
    /// <summary>Vault-relative attachment paths referenced from a body (Markdown images/links and embeds).</summary>
    public static IEnumerable<string> Find(string body)
    {
        foreach (Match m in MarkdownRef().Matches(body))
        {
            var target = Uri.UnescapeDataString(m.Groups["path"].Value.Trim());
            if (!target.StartsWith("attachments/", StringComparison.OrdinalIgnoreCase) || target.Contains('\\') || target.Contains(':') || target.Any(char.IsControl)) continue;
            var parts = target.Split('/');
            if (parts.Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' '))) continue;
            yield return target;
        }
    }

    [GeneratedRegex(@"!?\[[^\]\n]*\]\((?<path>[^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex MarkdownRef();
}
