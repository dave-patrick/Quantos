using System.Globalization;
using System.Text;

namespace Quantos.Core.Tasks;

/// <summary>
/// Calendar file generation (BUILD-PLAN 8B.18): tasks with dates or time blocks as an iCalendar file.
/// Everything is written as events, because Outlook and Google Calendar ignore to-dos (VTODO) in
/// imported files. A date-only task is an all-day event marked free, so it doesn't block time.
/// </summary>
public static class Calendar
{
    public static string Build(IEnumerable<TaskDto> tasks, string name)
    {
        var sb = new StringBuilder();
        void Line(string text)
        {
            // RFC 5545: fold lines at 75 octets, never inside a character (emoji are two UTF-16 units).
            var count = 0;
            foreach (var rune in text.EnumerateRunes())
            {
                var size = rune.Utf8SequenceLength;
                if (count + size > 75) { sb.Append("\r\n "); count = 1; }
                sb.Append(rune.ToString());
                count += size;
            }
            sb.Append("\r\n");
        }
        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//Atomic 22 Industries//Quantos//EN");
        Line("CALSCALE:GREGORIAN");
        Line("METHOD:PUBLISH");
        Line("X-WR-CALNAME:" + Escape(name));
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        foreach (var t in tasks)
        {
            if (t.TimeBlock is { } block && DateTimeOffset.TryParse(block.Start, CultureInfo.InvariantCulture, DateTimeStyles.None, out var s)
                && DateTimeOffset.TryParse(block.End, CultureInfo.InvariantCulture, DateTimeStyles.None, out var e))
            {
                Line("BEGIN:VEVENT");
                Line($"UID:{t.Id}-block@quantos");
                Line($"DTSTAMP:{stamp}");
                Line($"DTSTART:{Utc(s)}");
                Line($"DTEND:{Utc(e)}");
                Line("SUMMARY:" + Escape(t.Title));
                if (t.Status == TaskStatus.Cancelled) Line("STATUS:CANCELLED");
                Line("END:VEVENT");
            }
            // Finished work stays on the calendar through its time block; only open tasks get a due entry.
            var dueText = t.Due ?? t.Planned;
            if (dueText is null || TaskStatus.IsClosed(t.Status)) continue;
            string start, end;
            if (DateOnly.TryParseExact(dueText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                start = $"DTSTART;VALUE=DATE:{day:yyyyMMdd}";
                end = $"DTEND;VALUE=DATE:{day.AddDays(1):yyyyMMdd}";
            }
            else if (DateTimeOffset.TryParse(dueText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var due))
            {
                start = $"DTSTART:{Utc(due)}";
                end = $"DTEND:{Utc(due.AddMinutes(30))}";
            }
            else continue;
            Line("BEGIN:VEVENT");
            Line($"UID:{t.Id}@quantos");
            Line($"DTSTAMP:{stamp}");
            Line(start);
            Line(end);
            Line("SUMMARY:" + Escape(t.Title));
            Line("DESCRIPTION:" + Escape(t.Due is not null ? $"Due {t.Due}" : $"Planned for {t.Planned}"));
            Line("TRANSP:TRANSPARENT");
            if (t.Priority is { } p) Line($"PRIORITY:{p switch { 1 => 1, 2 => 3, 3 => 5, _ => 9 }}");
            foreach (var r in t.Reminders.Where(r => r.Fired is null))
                if (DateTimeOffset.TryParse(r.SnoozedUntil ?? r.At, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
                {
                    Line("BEGIN:VALARM");
                    Line("ACTION:DISPLAY");
                    Line("DESCRIPTION:" + Escape(t.Title));
                    Line($"TRIGGER;VALUE=DATE-TIME:{Utc(at)}");
                    Line("END:VALARM");
                }
            Line("END:VEVENT");
        }
        Line("END:VCALENDAR");
        return sb.ToString();
    }

    private static string Utc(DateTimeOffset d) => d.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");
}