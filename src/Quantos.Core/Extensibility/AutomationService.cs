using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Quantos.Core.Security;
using Quantos.Core.Tasks;

namespace Quantos.Core.Extensibility;

public sealed record AutomationTrigger
{
    public string? Type { get; init; }
    public string? Tag { get; init; }
    public required string Property { get; init; }
    public string? To { get; init; }
    public string? From { get; init; }
}

public sealed record AutomationAction
{
    public Dictionary<string, string>? Set { get; init; }
    public bool? Archive { get; init; }
    public string? Notify { get; init; }
    public Dictionary<string, string>? CreateTask { get; init; }
    public string? AddTag { get; init; }
}

public sealed record AutomationRule
{
    public string Id { get; init; } = Ulid.New();
    public required string Name { get; init; }
    public bool Enabled { get; init; } = true;
    public required AutomationTrigger When { get; init; }
    public List<AutomationAction> Then { get; init; } = [];
}

public sealed record AutomationRun(string Rule, string NodeId, string Title, DateTimeOffset At, string Outcome);

/// <summary>
/// Automations triggered by property changes (BUILD-PLAN 8E.15), defined in Vault/automations.json.
/// They act through the write service as an Automation actor, so permissions apply; by default
/// automations may read and edit content but not archive, trash or run anything.
/// </summary>
public sealed partial class AutomationService : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly WriteService _writes;
    private readonly TaskService _tasks;
    private readonly NotificationStore _notifications;
    private readonly ConcurrentDictionary<string, Dictionary<string, string?>> _snapshot = new();
    private readonly ConcurrentQueue<AutomationRun> _log = new();
    [ThreadStatic] private static int _depth;

    public AutomationService(WriteService writes, TaskService tasks, NotificationStore notifications)
    {
        _writes = writes;
        _tasks = tasks;
        _notifications = notifications;
        foreach (var n in writes.Cache.List(includeArchived: true, limit: 1_000_000)) _snapshot[n.Id] = Flatten(n.Properties, n.Tags);
        writes.Changes.Changed += OnChanged;
    }

    public IReadOnlyList<AutomationRun> Recent => _log.Reverse().Take(100).ToList();

    public List<AutomationRule> Rules()
    {
        var path = Path.Combine(_writes.Vault.Root, "automations.json");
        if (!File.Exists(path)) return [];
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var list = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.TryGetProperty("automations", out var a) ? a : default;
            return list.ValueKind == JsonValueKind.Array ? list.Deserialize<List<AutomationRule>>(Json) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException) { return []; }
    }

    private static Dictionary<string, string?> Flatten(IReadOnlyDictionary<string, object?> props, IReadOnlyList<string> tags)
    {
        var d = props.ToDictionary(kv => kv.Key, kv => Props.List(props, kv.Key) is { Count: > 1 } l ? string.Join(",", l) : Props.Str(kv.Value));
        d["tags"] = string.Join(",", tags);
        return d;
    }

    private void OnChanged(ChangeEvent e)
    {
        if (e.Id is null || e.Kind is not (ChangeKind.Updated or ChangeKind.Created or ChangeKind.Moved)) return;
        var summary = _writes.Cache.Get(e.Id);
        if (summary is null) return;
        var now = Flatten(summary.Properties, summary.Tags);
        var before = _snapshot.TryGetValue(e.Id, out var b) ? b : new();
        _snapshot[e.Id] = now;
        if (_depth > 2 || e.Origin?.StartsWith("Automation:") == true && _depth > 0) return;

        foreach (var rule in Rules().Where(r => r.Enabled))
        {
            var w = rule.When;
            if (w.Type is not null && !w.Type.Equals(summary.Type, StringComparison.OrdinalIgnoreCase)) continue;
            if (w.Tag is not null && !summary.Tags.Contains(w.Tag.TrimStart('#'), StringComparer.OrdinalIgnoreCase)) continue;
            var oldValue = before.GetValueOrDefault(w.Property);
            var newValue = now.GetValueOrDefault(w.Property);
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) continue;
            if (w.To is not null && !string.Equals(newValue, w.To, StringComparison.OrdinalIgnoreCase)) continue;
            if (w.From is not null && !string.Equals(oldValue, w.From, StringComparison.OrdinalIgnoreCase)) continue;
            Run(rule, summary.Id, summary.Title);
        }
    }

    private void Run(AutomationRule rule, string id, string title)
    {
        var actor = new Actor(ActorKind.Automation, rule.Name);
        _depth++;
        try
        {
            foreach (var action in rule.Then)
            {
                if (action.Set is { } set)
                    _writes.UpdateFields(id, null, set.ToDictionary(kv => kv.Key, kv => (object?)Expand(kv.Value, title)), null, actor);
                if (action.AddTag is { } tag)
                {
                    var node = _writes.Require(id);
                    if (!node.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                        _writes.UpdateFields(id, null, new Dictionary<string, object?> { [NodeKeys.Tags] = node.Tags.Append(tag).ToList() }, null, actor);
                }
                if (action.CreateTask is { } t)
                    _tasks.Create(new CreateTaskRequest
                    {
                        Title = Expand(t.GetValueOrDefault("title") ?? "Follow up on {{title}}", title),
                        Workspace = _writes.Require(id).Workspace is { Length: > 0 } ws ? ws : Storage.Vault.DefaultWorkspace,
                        Due = t.GetValueOrDefault("due") is { } due ? Expand(due, title) : null, Source = id
                    }, actor);
                if (action.Notify is { } message)
                {
                    var record = _notifications.Append(rule.Name, Expand(message, title), "automation", id, null, _writes.Clock.Now);
                    _writes.Changes.Publish(new ChangeEvent(ChangeKind.Notification, id, null, Origin: actor.ToString(), Data: new { notification = record }));
                }
                if (action.Archive == true) _writes.SetArchived(id, true, actor);
            }
            _log.Enqueue(new AutomationRun(rule.Name, id, title, _writes.Clock.Now, "ran"));
        }
        catch (Exception ex) when (ex is PermissionDeniedException or ApprovalRequiredException or QuantosException)
        {
            _log.Enqueue(new AutomationRun(rule.Name, id, title, _writes.Clock.Now, "stopped: " + ex.Message));
        }
        finally
        {
            _depth--;
            while (_log.Count > 200) _log.TryDequeue(out _);
        }
    }

    private string Expand(string text, string title) => Variable().Replace(text, m =>
    {
        var today = _writes.Clock.Today;
        return m.Groups["name"].Value.ToLowerInvariant() switch
        {
            "title" => title,
            "today" => (m.Groups["n"].Success ? today.AddDays(int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture) * (m.Groups["sign"].Value == "-" ? -1 : 1)) : today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "now" => _writes.Clock.Stamp(),
            _ => m.Value
        };
    });

    [GeneratedRegex(@"\{\{\s*(?<name>title|today|now)(?:\s*(?<sign>[+-])\s*(?<n>\d+))?\s*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex Variable();

    public void Dispose() => _writes.Changes.Changed -= OnChanged;
}
