using System.Globalization;

namespace Quantos.Core.Tasks;

/// <summary>
/// RRULE subset: FREQ=DAILY|WEEKLY|MONTHLY|YEARLY; INTERVAL=n; BYDAY=[n]MO,TU,...; BYMONTHDAY=n|-1; BYMONTH=1..12; UNTIL=YYYY-MM-DD; COUNT=n.
/// The next occurrence is calculated from the scheduled date (confirmed), never from the completion date.
/// </summary>
public sealed record Recurrence(
    string Freq,
    int Interval,
    IReadOnlyList<DayOfWeek> ByDay,
    int? ByMonthDay,
    int? ByDayOrdinal = null,
    int? ByMonth = null,
    DateOnly? Until = null,
    int? Count = null)
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
            case "yearly": case "annually": case "anually": case "every year": return new("YEARLY", 1, [], null);
        }
        var parts = text.Replace("RRULE:", "", StringComparison.OrdinalIgnoreCase).Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].Trim().ToUpperInvariant(), p => p[1].Trim().ToUpperInvariant());
        if (!parts.TryGetValue("FREQ", out var freq) || freq is not ("DAILY" or "WEEKLY" or "MONTHLY" or "YEARLY")) return null;
        var interval = parts.TryGetValue("INTERVAL", out var i) && int.TryParse(i, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 1;

        var days = new List<DayOfWeek>();
        int? byDayOrdinal = null;
        if (parts.TryGetValue("BYDAY", out var byDay))
        {
            foreach (var rawToken in byDay.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var token = rawToken.Trim();
                if (token.Length < 2) continue;
                var dayCode = token[^2..];
                var day = dayCode switch
                {
                    "MO" => DayOfWeek.Monday,
                    "TU" => DayOfWeek.Tuesday,
                    "WE" => DayOfWeek.Wednesday,
                    "TH" => DayOfWeek.Thursday,
                    "FR" => DayOfWeek.Friday,
                    "SA" => DayOfWeek.Saturday,
                    "SU" => DayOfWeek.Sunday,
                    _ => (DayOfWeek?)null
                };
                if (day is null) continue;
                if (token.Length > 2 && int.TryParse(token[..^2], CultureInfo.InvariantCulture, out var ord) && ord is >= -5 and <= 5 and not 0)
                    byDayOrdinal ??= ord;
                days.Add(day.Value);
            }
        }
        if (byDayOrdinal is null && days.Count == 1 && parts.TryGetValue("BYSETPOS", out var posStr)
            && int.TryParse(posStr, CultureInfo.InvariantCulture, out var pos) && pos is >= -5 and <= 5 and not 0)
        {
            byDayOrdinal = pos;
        }

        int? monthDay = parts.TryGetValue("BYMONTHDAY", out var md) && int.TryParse(md, CultureInfo.InvariantCulture, out var m) && (m is >= 1 and <= 31 || m == -1) ? m : null;
        int? month = parts.TryGetValue("BYMONTH", out var bm) && int.TryParse(bm, CultureInfo.InvariantCulture, out var mo) && mo is >= 1 and <= 12 ? mo : null;
        DateOnly? until = parts.TryGetValue("UNTIL", out var u) ? ParseUntil(u) : null;
        int? count = parts.TryGetValue("COUNT", out var c) && int.TryParse(c, CultureInfo.InvariantCulture, out var cnt) && cnt > 0 ? cnt : null;

        return new Recurrence(freq, interval, days, monthDay, byDayOrdinal, month, until, count);
    }

    private static DateOnly? ParseUntil(string raw)
    {
        var text = raw.Trim();
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso)) return iso;
        if (text.Length >= 8 && DateOnly.TryParseExact(text[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var compact)) return compact;
        return null;
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
                var first = new DateOnly(scheduled.Year, scheduled.Month, 1);
                if (ByDayOrdinal is { } ord && ByDay.Count > 0)
                {
                    for (var step = 1; step <= 60; step++)
                    {
                        var targetMonth = first.AddMonths(Interval * step);
                        if (NthWeekday(targetMonth.Year, targetMonth.Month, ByDay[0], ord) is { } match) return match;
                    }
                }
                var target = first.AddMonths(Interval);
                if (ByMonthDay == -1)
                    return new DateOnly(target.Year, target.Month, DateTime.DaysInMonth(target.Year, target.Month));
                var day = ByMonthDay ?? anchorDay ?? scheduled.Day;
                return new DateOnly(target.Year, target.Month, Math.Min(day, DateTime.DaysInMonth(target.Year, target.Month)));
            }
            default:
            {
                var month = ByMonth ?? scheduled.Month;
                if (ByDayOrdinal is { } ord && ByDay.Count > 0)
                {
                    for (var step = 1; step <= 20; step++)
                    {
                        var y = scheduled.Year + Interval * step;
                        if (NthWeekday(y, month, ByDay[0], ord) is { } match) return match;
                    }
                }
                var year = scheduled.Year + Interval;
                if (ByMonthDay == -1)
                    return new DateOnly(year, month, DateTime.DaysInMonth(year, month));
                var day = ByMonthDay ?? anchorDay ?? scheduled.Day;
                return new DateOnly(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
            }
        }
    }

    private static DateOnly? NthWeekday(int year, int month, DayOfWeek day, int ordinal)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        if (ordinal > 0)
        {
            var first = new DateOnly(year, month, 1);
            var offset = ((int)day - (int)first.DayOfWeek + 7) % 7;
            var resultDay = 1 + offset + 7 * (ordinal - 1);
            return resultDay <= daysInMonth ? new DateOnly(year, month, resultDay) : null;
        }
        var last = new DateOnly(year, month, daysInMonth);
        var back = ((int)last.DayOfWeek - (int)day + 7) % 7;
        var d = daysInMonth - back - 7 * (-ordinal - 1);
        return d >= 1 ? new DateOnly(year, month, d) : null;
    }

    private static DateOnly StartOfWeek(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    public string Describe()
    {
        var core = Freq switch
        {
            "DAILY" => Interval == 1 ? "Daily" : $"Every {Interval} days",
            "WEEKLY" when ByDay.Count > 0 => (Interval == 1 ? "Weekly on " : $"Every {Interval} weeks on ") + string.Join(", ", ByDay.Select(d => CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(d))),
            "WEEKLY" => Interval == 1 ? "Weekly" : $"Every {Interval} weeks",
            "MONTHLY" when ByDayOrdinal is { } ord && ByDay.Count > 0 => $"{(Interval == 1 ? "Monthly" : $"Every {Interval} months")} on the {OrdinalLabel(ord)} {CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(ByDay[0])}",
            "MONTHLY" when ByMonthDay == -1 => $"{(Interval == 1 ? "Monthly" : $"Every {Interval} months")} on the last day",
            "MONTHLY" when ByMonthDay is { } md => $"{(Interval == 1 ? "Monthly" : $"Every {Interval} months")} on day {md}",
            "MONTHLY" => Interval == 1 ? "Monthly" : $"Every {Interval} months",
            _ when ByDayOrdinal is { } ord && ByDay.Count > 0 && ByMonth is { } bm => $"{(Interval == 1 ? "Annually" : $"Every {Interval} years")} on the {OrdinalLabel(ord)} {CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(ByDay[0])} of {CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(bm)}",
            _ when ByMonth is { } bm && ByMonthDay == -1 => $"{(Interval == 1 ? "Annually" : $"Every {Interval} years")} on the last day of {CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(bm)}",
            _ when ByMonth is { } bm && ByMonthDay is { } md => $"{(Interval == 1 ? "Annually" : $"Every {Interval} years")} on {md} {CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(bm)}",
            _ => Interval == 1 ? "Annually" : $"Every {Interval} years"
        };
        if (Until is { } until) core += $", until {until:yyyy-MM-dd}";
        else if (Count is { } count) core += count == 1 ? ", 1 time" : $", {count} times";
        return core;
    }

    private static string OrdinalLabel(int ord) => ord switch
    {
        -1 => "last",
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        4 => "4th",
        5 => "5th",
        _ => ord.ToString(CultureInfo.InvariantCulture)
    };
}
