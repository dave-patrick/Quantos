using System.Globalization;
using System.Text.RegularExpressions;
using Quantos.Core.Security;

namespace Quantos.Core.Tasks;

public sealed record FocusSession(string Start, string End, int Minutes, string? TaskId, string Label);
public sealed record FocusDay(string Date, int Minutes, IReadOnlyList<FocusSession> Sessions);
public sealed record FocusTotal(string? TaskId, string Label, int Minutes, int Sessions);
public sealed record FocusSummary(IReadOnlyList<FocusDay> Days, IReadOnlyList<FocusTotal> ByTask, int Minutes);

/// <summary>
/// Focus sessions (Release 1.1). A finished session is written where people already look: a line under
/// "## Focus" in that day's daily note, and minutes added to the task's <c>time_spent</c>. The summary is read
/// back from those daily notes, so editing or deleting a line there changes the totals too.
/// </summary>
public sealed partial class FocusService(WriteService writes)
{
    public const string Heading = "## Focus", TimeSpent = "time_spent";

    [GeneratedRegex(@"^- (?<s>\d{2}:\d{2})[–-](?<e>\d{2}:\d{2}) · (?<m>\d+) min(?: · (?<rest>.*))?$", RegexOptions.Multiline)]
    private static partial Regex Line();

    [GeneratedRegex(@"^\[\[(?<id>[0-9A-HJKMNP-TV-Z]{26})(?:\|(?<title>[^\]]*))?\]\]$")]
    private static partial Regex TaskLink();

    public FocusSession Log(string workspace, DateTimeOffset start, DateTimeOffset end, string? taskId, string? label, Actor actor)
    {
        if (end <= start) throw new QuantosException("A focus session has to end after it starts.");
        var minutes = (int)Math.Round((end - start).TotalMinutes);
        if (minutes < 1) throw new QuantosException("Sessions under a minute aren't logged.");
        if (minutes > 12 * 60) throw new QuantosException("A focus session can't be longer than 12 hours.");
        var zone = writes.Clock.Zone;
        var s = TimeZoneInfo.ConvertTime(start, zone);
        var e = TimeZoneInfo.ConvertTime(end, zone);

        Node? task = null;
        if (taskId is not null)
        {
            task = writes.Require(taskId);
            workspace = task.Workspace;
        }
        var what = task is not null ? $"[[{task.Id}|{task.Title}]]" : string.IsNullOrWhiteSpace(label) ? "" : Clean(label);
        var line = $"- {s:HH:mm}–{e:HH:mm} · {minutes} min" + (what.Length > 0 ? " · " + what : "");

        var daily = writes.GetOrCreateDaily(workspace, DateOnly.FromDateTime(s.DateTime), actor);
        writes.AppendToBody(daily.Id, line, actor, Heading);

        if (task is not null)
        {
            var spent = task.Properties.TryGetValue(TimeSpent, out var v) && int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var n) ? n : 0;
            writes.UpdateFields(task.Id, null, new Dictionary<string, object?> { [TimeSpent] = spent + minutes }, null, actor);
        }
        return new FocusSession(s.ToString("HH:mm", CultureInfo.InvariantCulture), e.ToString("HH:mm", CultureInfo.InvariantCulture), minutes, task?.Id, task?.Title ?? what);
    }

    public FocusSummary Summary(string workspace, int days, Actor actor)
    {
        writes.Permissions.Demand(actor, Permission.Read, "Read focus sessions");
        days = Math.Clamp(days, 1, 366);
        var today = writes.Clock.Today;
        var list = new List<FocusDay>();
        for (var i = days - 1; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            var sessions = Read(workspace, date);
            list.Add(new FocusDay(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), sessions.Sum(x => x.Minutes), sessions));
        }
        var byTask = list.SelectMany(d => d.Sessions)
            .GroupBy(x => x.TaskId ?? "label:" + x.Label)
            .Select(g => new FocusTotal(g.First().TaskId, g.First().TaskId is { } id ? writes.Get(id)?.Title ?? g.First().Label : g.First().Label, g.Sum(x => x.Minutes), g.Count()))
            .OrderByDescending(t => t.Minutes).ToList();
        return new FocusSummary(list, byTask, list.Sum(d => d.Minutes));
    }

    private List<FocusSession> Read(string workspace, DateOnly date)
    {
        var path = writes.Vault.DailyPath(workspace, date);
        if (!File.Exists(path)) return [];
        var body = Node.FromText(writes.Vault.Relative(path), Storage.AtomicFile.ReadAllText(path)).Body.Replace("\r\n", "\n");
        var at = body.IndexOf(Heading + "\n", StringComparison.Ordinal);
        if (at < 0) return [];
        var section = body[(at + Heading.Length + 1)..];
        var next = Regex.Match(section, @"^#{1,2} ", RegexOptions.Multiline);
        if (next.Success) section = section[..next.Index];
        var sessions = new List<FocusSession>();
        foreach (Match m in Line().Matches(section))
        {
            var rest = m.Groups["rest"].Success ? m.Groups["rest"].Value.Trim() : "";
            var link = TaskLink().Match(rest);
            sessions.Add(new FocusSession(m.Groups["s"].Value, m.Groups["e"].Value, int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture),
                link.Success ? link.Groups["id"].Value : null, link.Success ? link.Groups["title"].Value : rest));
        }
        return sessions;
    }

    private static string Clean(string label)
    {
        var t = Regex.Replace(label.Trim(), @"\s+", " ");
        return t.Length > 120 ? t[..120] : t;
    }
}
