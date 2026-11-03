using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Quantos.Core.Tasks;
using TaskStatus = Quantos.Core.Tasks.TaskStatus;

namespace Quantos.Core.GoogleTasks;

/// <summary>Pure field rules: how a Quantos task value and a Google task value are normalised so they can be compared.</summary>
public static partial class GoogleTasksMapper
{
    public const int MaxTitle = 1024;
    public const int MaxNotes = 8000;
    public const string Untitled = "(untitled)";
    public const string HintId = "gtask_id", HintList = "gtask_list";

    /// <summary>Line breaks become spaces, comment and link brackets are removed, the result is trimmed; empty becomes "(untitled)".</summary>
    public static string NormTitle(string? title)
    {
        var t = (title ?? "").Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        t = t.Replace("<!--", "").Replace("-->", "").Replace("[[", "[").Replace("]]", "]").Trim();
        return t.Length == 0 ? Untitled : t;
    }

    /// <summary>At most <see cref="MaxTitle"/> characters, never cutting an emoji (a surrogate pair) in half, which JSON cannot carry.</summary>
    public static string Truncate(string title)
    {
        if (title.Length <= MaxTitle) return title;
        var length = char.IsHighSurrogate(title[MaxTitle - 1]) ? MaxTitle - 1 : MaxTitle;
        return title[..length];
    }

    /// <summary>Null becomes ""; every line break is "\n"; trailing whitespace is dropped.</summary>
    public static string NormNotes(string? notes) => (notes ?? "").Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    [GeneratedRegex(@"\[\[([^\]\|#^\[]+)(?:\|([^\]]+))?\]\]")]
    private static partial Regex SupportedLink();

    /// <summary>
    /// The notes text to send for a task body: [[id|text]], [[Title|alias]] and [[Title]] become their display text.
    /// Embeds, headings and block references are not representable, so notes are not synced for such a body (Eligible = false),
    /// nor for one over the Google limit.
    /// </summary>
    public static (string Text, bool Eligible) NotesFromBody(string? body)
    {
        var normal = NormNotes(body);
        if (normal.Contains("![[")) return (normal, false);
        var text = SupportedLink().Replace(normal, m => m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value);
        if (text.Contains("[[")) return (normal, false);
        return (text, text.Length <= MaxNotes);
    }

    /// <summary>Google notes are acceptable to store locally only within the cap.</summary>
    public static bool NotesFitFromGoogle(string? notes) => NormNotes(notes).Length <= MaxNotes;

    /// <summary>yyyy-MM-dd of a Quantos due value (date or date-time); the zone is never converted.</summary>
    public static string? LocalDue(string? due) => Props.Date(due)?.ToString("yyyy-MM-dd");

    public static bool IsClosed(string? status) => TaskStatus.IsClosed(status);
}
