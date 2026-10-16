using System.Globalization;
using System.Text;

namespace Quantos.Core.Tasks;

/// <summary>Calendar file generation (BUILD-PLAN 8B.18): tasks with dates or time blocks as an iCalendar file.</summary>
public static class Calendar
{
    public static string Build(IEnumerable<TaskDto> tasks, string name)
    {
        var sb = new StringBuilder();
        void Line(string text)
        {
            // RFC 5545: fold lines at 75 octets.
            var bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length <= 75) { sb.Append(text).Append("\r\n"); return; }
            var chunk = new StringBuilder();
            var count = 0;
            foreach (var ch in text)
            {
                var size = Encoding.UTF8.GetByteCount(ch.ToString());
                if (count + size > 74) { sb.Append(chunk).Append("\r\n "); chunk.Clear(); count = 1; }
                chunk.Append(ch);
                count += size;
            }
            sb.Append(chunk).Append("\r\n");
        }
        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//Atomic 22 Industries//Quantos//EN");
        Line("CALSCALE:GREGORIAN");
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
                if (TaskStatus.IsClosed(t.Status)) Line("STATUS:CANCELLED");
                Line("END:VEVENT");
            }
            var dueText = t.Due ?? t.Planned;
            if (dueText is null) continue;
            Line("BEGIN:VTODO");
            Line($"UID:{t.Id}@quantos");
            Line($"DTSTAMP:{stamp}");
            Line("SUMMARY:" + Escape(t.Title));
            if (dueText.Length == 10) Line($"DUE;VALUE=DATE:{dueText.Replace("-", "")}");
            else if (DateTimeOffset.TryParse(dueText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var due)) Line($"DUE:{Utc(due)}");
            if (t.Priority is { } p) Line($"PRIORITY:{p switch { 1 => 1, 2 => 3, 3 => 5, _ => 9 }}");
            Line("STATUS:" + (t.Status == TaskStatus.Done ? "COMPLETED" : t.Status == TaskStatus.Cancelled ? "CANCELLED" : t.Status == TaskStatus.Doing ? "IN-PROCESS" : "NEEDS-ACTION"));
            foreach (var r in t.Reminders.Where(r => r.Fired is null))
                if (DateTimeOffset.TryParse(r.SnoozedUntil ?? r.At, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
                {
                    Line("BEGIN:VALARM");
                    Line("ACTION:DISPLAY");
                    Line("DESCRIPTION:" + Escape(t.Title));
                    Line($"TRIGGER;VALUE=DATE-TIME:{Utc(at)}");
                    Line("END:VALARM");
                }
            Line("END:VTODO");
        }
        Line("END:VCALENDAR");
        return sb.ToString();
    }

    private static string Utc(DateTimeOffset d) => d.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");
}
