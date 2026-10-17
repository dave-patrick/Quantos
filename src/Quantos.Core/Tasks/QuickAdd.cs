using System.Globalization;
using System.Text.RegularExpressions;

namespace Quantos.Core.Tasks;

public sealed record QuickTask(string Title, int? Priority, string? Due, string? Planned, int? Effort,
    IReadOnlyList<string> Tags, string? Recurrence, string? Project, bool MyDay)
{
    /// <summary>True when the text held any token beyond the title.</summary>
    public bool HasTokens => Priority is not null || Due is not null || Planned is not null || Effort is not null
        || Tags.Count > 0 || Recurrence is not null || Project is not null || MyDay;
}

/// <summary>
/// Quick-add tokens (BUILD-PLAN 8C.3), the same rules as the frontend's quickadd.ts so a task line
/// means the same thing wherever it's typed: !1-!4 priority, due:fri, plan:tomorrow (or @tomorrow),
/// ~45m / ~2h effort, #tag, +project, "every week" recurrence, and !myday.
/// </summary>
public static partial class QuickAdd
{
    private static readonly string[] Weekdays = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    /// <summary>Natural dates: today, tomorrow, mon..sun, +3d, +2w, 2026-10-20, 20/10.</summary>
    public static string? ParseDate(string word, DateOnly today)
    {
        var w = word.ToLowerInvariant();
        if (w is "today" or "tod") return Iso(today);
        if (w is "tomorrow" or "tom") return Iso(today.AddDays(1));
        if (w == "yesterday") return Iso(today.AddDays(-1));
        if (DateOnly.TryParseExact(w, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact)) return Iso(exact);
        if (Relative().Match(w) is { Success: true } rel)
            return Iso(today.AddDays(int.Parse(rel.Groups[1].Value, CultureInfo.InvariantCulture) * (rel.Groups[2].Value == "w" ? 7 : 1)));
        if (DayMonth().Match(w) is { Success: true } dm)
        {
            var day = int.Parse(dm.Groups[1].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(dm.Groups[2].Value, CultureInfo.InvariantCulture);
            if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(today.Year, month)) return null;
            var candidate = new DateOnly(today.Year, month, day);
            if (candidate < today && day <= DateTime.DaysInMonth(today.Year + 1, month)) candidate = new DateOnly(today.Year + 1, month, day);
            return Iso(candidate);
        }
        var index = Array.FindIndex(Weekdays, d => w.StartsWith(d, StringComparison.Ordinal) && w.Length <= 9);
        if (index >= 0)
        {
            var diff = (index - (int)today.DayOfWeek + 7) % 7;
            return Iso(today.AddDays(diff == 0 ? 7 : diff));
        }
        return null;
    }

    public static QuickTask Parse(string text, DateOnly today)
    {
        string? recurrence = null;
        var rest = text;
        if (Every().Match(rest) is { Success: true } every)
        {
            var what = every.Groups[1].Value.ToLowerInvariant();
            recurrence = Interval().Match(what) is { Success: true } n
                ? $"FREQ={n.Groups[2].Value switch { "days" => "DAILY", "weeks" => "WEEKLY", _ => "MONTHLY" }};INTERVAL={n.Groups[1].Value}"
                : what switch { "day" => "daily", "weekday" => "weekdays", _ => what + "ly" };
            rest = rest.Remove(every.Index, every.Length).Insert(every.Index, " ");
        }
        int? priority = null, effort = null;
        string? due = null, planned = null, project = null;
        var myDay = false;
        var tags = new List<string>();
        var words = new List<string>();
        foreach (var token in rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            Match m;
            if ((m = PriorityToken().Match(token)).Success) priority = token[1] - '0';
            else if (token.Equals("!myday", StringComparison.OrdinalIgnoreCase)) myDay = true;
            else if ((m = DueToken().Match(token)).Success && ParseDate(m.Groups[1].Value, today) is { } d) due = d;
            else if ((m = PlanToken().Match(token)).Success && ParseDate(m.Groups[1].Value, today) is { } p) planned = p;
            else if ((m = EffortToken().Match(token)).Success)
                effort = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * (m.Groups[2].Value.Equals("h", StringComparison.OrdinalIgnoreCase) ? 60 : 1);
            else if ((m = TagToken().Match(token)).Success) tags.Add(m.Groups[1].Value);
            else if ((m = ProjectToken().Match(token)).Success) project = m.Groups[1].Value.Replace('-', ' ').Replace('_', ' ');
            else words.Add(token);
        }
        return new QuickTask(string.Join(' ', words).Trim(), priority, due, planned, effort, tags, recurrence, project, myDay);
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\+(\d+)([dw])$")] private static partial Regex Relative();
    [GeneratedRegex(@"^(\d{1,2})/(\d{1,2})$")] private static partial Regex DayMonth();
    [GeneratedRegex(@"\bevery\s+(day|weekday|week|month|year|\d+\s+(?:days|weeks|months))\b", RegexOptions.IgnoreCase)] private static partial Regex Every();
    [GeneratedRegex(@"^(\d+)\s+(days|weeks|months)$")] private static partial Regex Interval();
    [GeneratedRegex(@"^![1-4]$")] private static partial Regex PriorityToken();
    [GeneratedRegex(@"^due:(.+)$", RegexOptions.IgnoreCase)] private static partial Regex DueToken();
    [GeneratedRegex(@"^(?:plan:|@)(.+)$", RegexOptions.IgnoreCase)] private static partial Regex PlanToken();
    [GeneratedRegex(@"^~(\d+)(m|h)?$", RegexOptions.IgnoreCase)] private static partial Regex EffortToken();
    [GeneratedRegex(@"^#([\w\-/]+)$")] private static partial Regex TagToken();
    [GeneratedRegex(@"^\+([\w\-]+)$")] private static partial Regex ProjectToken();
}
