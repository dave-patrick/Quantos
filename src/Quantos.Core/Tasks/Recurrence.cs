using System.Globalization;

namespace Quantos.Core.Tasks;

/// <summary>
/// RRULE subset: FREQ=DAILY|WEEKLY|MONTHLY|YEARLY; INTERVAL=n; BYDAY=MO,TU,...; BYMONTHDAY=n.
/// The next occurrence is calculated from the scheduled date (confirmed), never from the completion date.
/// </summary>
public sealed record Recurrence(string Freq, int Interval, IReadOnlyList<DayOfWeek> ByDay, int? ByMonthDay)
{
    public static Recurrence? Parse(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule)) return null;
        var text = rule.Trim();
        // Friendly forms people type.
        switch (text.ToLowerInvariant())
        {
            case "daily": case "every day": return new("DAILY", 1, [], null);
            case "weekdays": case "every weekday": return new("WEEKLY", 1, [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday], null);
            case "weekly": case "every week": return new("WEEKLY", 1, [], null);
            case "monthly": case "every month": return new("MONTHLY", 1, [], null);
            case "yearly": case "annually": case "every year": return new("YEARLY", 1, [], null);
        }
        var parts = text.Replace("RRULE:", "", StringComparison.OrdinalIgnoreCase).Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].Trim().ToUpperInvariant(), p => p[1].Trim().ToUpperInvariant());
        if (!parts.TryGetValue("FREQ", out var freq) || freq is not ("DAILY" or "WEEKLY" or "MONTHLY" or "YEARLY")) return null;
        var interval = parts.TryGetValue("INTERVAL", out var i) && int.TryParse(i, out var n) && n > 0 ? n : 1;
        var days = parts.TryGetValue("BYDAY", out var byDay)
            ? byDay.Split(',').Select(d => d switch { "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday, "TH" => DayOfWeek.Thursday, "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, "SU" => DayOfWeek.Sunday, _ => (DayOfWeek?)null })
                .OfType<DayOfWeek>().ToList()
            : [];
        int? monthDay = parts.TryGetValue("BYMONTHDAY", out var md) && int.TryParse(md, out var m) && m is >= 1 and <= 31 ? m : null;
        return new Recurrence(freq, interval, days, monthDay);
    }

    /// <param name="anchorDay">The day of the month the series started on; monthly and yearly occurrences aim for it.</param>
    public DateOnly Next(DateOnly scheduled, int? anchorDay = null)
    {
        switch (Freq)
        {
            case "DAILY":
                return scheduled.AddDays(Interval);
            case "WEEKLY":
                if (ByDay.Count == 0) return scheduled.AddDays(7 * Interval);
                // Next listed weekday after the scheduled date; jump interval weeks when the week wraps.
                for (var d = 1; d <= 7 * Interval + 7; d++)
                {
                    var candidate = scheduled.AddDays(d);
                    var weeksAhead = (StartOfWeek(candidate).DayNumber - StartOfWeek(scheduled).DayNumber) / 7;
                    if (ByDay.Contains(candidate.DayOfWeek) && (weeksAhead == 0 || weeksAhead % Interval == 0)) return candidate;
                }
                return scheduled.AddDays(7 * Interval);
            case "MONTHLY":
            {
                var target = scheduled.AddMonths(Interval);
                var day = ByMonthDay ?? anchorDay ?? scheduled.Day;
                return new DateOnly(target.Year, target.Month, Math.Min(day, DateTime.DaysInMonth(target.Year, target.Month)));
            }
            default:
            {
                var year = scheduled.Year + Interval;
                return new DateOnly(year, scheduled.Month, Math.Min(ByMonthDay ?? anchorDay ?? scheduled.Day, DateTime.DaysInMonth(year, scheduled.Month)));
            }
        }
    }

    private static DateOnly StartOfWeek(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    public string Describe() => Freq switch
    {
        "DAILY" => Interval == 1 ? "Every day" : $"Every {Interval} days",
        "WEEKLY" when ByDay.Count > 0 => (Interval == 1 ? "Every " : $"Every {Interval} weeks on ") + string.Join(", ", ByDay.Select(d => CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(d))),
        "WEEKLY" => Interval == 1 ? "Every week" : $"Every {Interval} weeks",
        "MONTHLY" => Interval == 1 ? "Every month" : $"Every {Interval} months",
        _ => Interval == 1 ? "Every year" : $"Every {Interval} years"
    };
}
