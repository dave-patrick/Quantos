using System.Globalization;
using System.Text.Json;
using Quantos.Core.Security;
using Quantos.Core.Storage;
using Quantos.Core.Tasks;

namespace Quantos.Core;

public sealed record NotificationRecord(string Id, string At, string Title, string Body, string Kind, string? NodeId, bool Read, string? Occurrence);

/// <summary>Notification history in .state/notifications.jsonl (append-only); read state in notifications-read.json.</summary>
public sealed class NotificationStore(Vault vault)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Lock _gate = new();
    private string ReadFile => Path.Combine(vault.State, "notifications-read.json");

    public NotificationRecord Append(string title, string body, string kind, string? nodeId, string? occurrence, DateTimeOffset at)
    {
        var record = new NotificationRecord(Ulid.New(), at.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture), title, body, kind, nodeId, false, occurrence);
        lock (_gate)
        {
            Directory.CreateDirectory(vault.State);
            using var stream = new FileStream(vault.Notifications, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(JsonSerializer.Serialize(record with { Read = false }, Json));
            writer.Flush();
            stream.Flush(true);
        }
        return record;
    }

    public IReadOnlyList<NotificationRecord> List(int limit = 200)
    {
        lock (_gate)
        {
            if (!File.Exists(vault.Notifications)) return [];
            var read = ReadSet();
            var list = new List<NotificationRecord>();
            foreach (var line in File.ReadLines(vault.Notifications))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var r = JsonSerializer.Deserialize<NotificationRecord>(line, Json);
                    if (r is not null) list.Add(r with { Read = read.Contains(r.Id) });
                }
                catch (JsonException) { }
            }
            return list.AsEnumerable().Reverse().Take(limit).ToList();
        }
    }

    public void MarkRead(IEnumerable<string> ids)
    {
        lock (_gate)
        {
            var set = ReadSet();
            set.UnionWith(ids);
            AtomicFile.WriteAllText(ReadFile, JsonSerializer.Serialize(set, Json));
        }
    }

    private HashSet<string> ReadSet()
    {
        try { return File.Exists(ReadFile) ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(ReadFile), Json) ?? [] : []; }
        catch (JsonException) { return []; }
    }
}

/// <summary>
/// Fires task reminders (BUILD-PLAN section 9). Each occurrence's reminders are tracked in its own
/// task file; firing records the time, so an alert is never shown twice, even across restarts or
/// with several clients open. Missed reminders fire after sleep or restart.
/// </summary>
public sealed class ReminderService(WriteService writes, TaskService tasks, NotificationStore store)
{
    public int Tick()
    {
        var now = writes.Clock.Now;
        var fired = 0;
        foreach (var task in tasks.List(includeClosed: false))
        {
            if (task.Reminders.Count == 0) continue;
            var changed = false;
            var reminders = task.Reminders.ToList();
            for (var i = 0; i < reminders.Count; i++)
            {
                var r = reminders[i];
                if (r.Fired is not null) continue;
                var dueText = r.SnoozedUntil ?? r.At;
                if (!DateTimeOffset.TryParse(dueText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var due) || due > now) continue;
                reminders[i] = r with { Fired = writes.Clock.Stamp() };
                changed = true;
                var body = task.Due is not null ? $"Due {task.Due}" : task.Planned is not null ? $"Planned for {task.Planned}" : "Reminder";
                var record = store.Append(task.Title, body, "reminder", task.Id, task.Occurrence, now);
                writes.Changes.Publish(new ChangeEvent(ChangeKind.Notification, task.Id, task.Path, Origin: "reminders", Data: new { notification = record }));
                fired++;
            }
            if (changed)
                writes.UpdateFields(task.Id, null, new Dictionary<string, object?> { [TaskKeys.Reminders] = TaskService.ReminderList(reminders) }, null, Actor.System);
        }
        return fired;
    }
}
