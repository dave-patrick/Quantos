using System.Globalization;
using System.Text.RegularExpressions;
using Quantos.Core.Security;

namespace Quantos.Core.Tasks;

public sealed record HabitValues(string From, string To, IReadOnlyDictionary<string, Dictionary<string, object?>> Days);

/// <summary>
/// Habit and metric tracking (Release 1.1). Each tracker is a property on the daily note (<c>exercise: true</c>,
/// <c>sleep: 7.5</c>), so the values are plain Markdown, show in the note's properties and work in queries.
/// Which properties are trackers is set in Vault/settings.json ("trackers"); this service reads and writes values.
/// </summary>
public sealed partial class HabitService(WriteService writes)
{
    [GeneratedRegex("^[a-z][a-z0-9_]{0,39}$")]
    private static partial Regex KeyPattern();

    public static bool ValidKey(string key) => KeyPattern().IsMatch(key) && !NodeKeys.Reserved.Contains(key) && key != "date";

    /// <summary>Values of the given keys on each daily note in the range; days without a note or values are left out.</summary>
    public HabitValues Read(string workspace, DateOnly from, DateOnly to, IReadOnlyCollection<string> keys, Actor actor)
    {
        writes.Permissions.Demand(actor, Permission.Read, "Read habit values");
        if (to < from) (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber > 400) throw new QuantosException("Ask for at most 400 days at a time.");
        var days = new Dictionary<string, Dictionary<string, object?>>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var path = writes.Vault.DailyPath(workspace, d);
            if (!File.Exists(path)) continue;
            var node = Node.FromText(writes.Vault.Relative(path), Storage.AtomicFile.ReadAllText(path));
            var values = keys.Where(node.Properties.ContainsKey).ToDictionary(k => k, k => node.Properties[k]);
            if (values.Count > 0) days[d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)] = values;
        }
        return new HabitValues(from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), days);
    }

    /// <summary>Sets (or with null, clears) one tracker on a day, creating the daily note when there's something to keep.</summary>
    public Node? Set(string workspace, DateOnly date, string key, object? value, Actor actor)
    {
        if (!ValidKey(key)) throw new QuantosException($"'{key}' can't be used as a tracker name.");
        if (date > writes.Clock.Today.AddDays(1)) throw new QuantosException("Habits can't be filled in ahead of time.");
        value = value switch
        {
            string s when string.IsNullOrWhiteSpace(s) => null,
            false => null,           // an unticked habit is simply absent
            _ => value
        };
        var path = writes.Vault.DailyPath(workspace, date);
        if (value is null && !File.Exists(path)) return null;
        var daily = writes.GetOrCreateDaily(workspace, date, actor);
        var result = value is null
            ? writes.UpdateFields(daily.Id, null, null, [key], actor)
            : writes.UpdateFields(daily.Id, null, new Dictionary<string, object?> { [key] = value }, null, actor);
        if (result.Status is SaveStatus.ReadOnly or SaveStatus.Invalid) throw new QuantosException(result.Message ?? "That day's note can't be changed.", 423);
        return result.Node;
    }
}
