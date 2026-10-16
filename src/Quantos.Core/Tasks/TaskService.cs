using System.Globalization;
using System.Text.RegularExpressions;
using Quantos.Core.Cache;
using Quantos.Core.Markdown;
using Quantos.Core.Security;

namespace Quantos.Core.Tasks;

public static class TaskKeys
{
    public const string Status = "status", Priority = "priority", Due = "due", Planned = "planned", Effort = "effort",
        Assignees = "assignees", Project = "project", WaitingOn = "waiting_on", Parent = "parent", DependsOn = "depends_on",
        Recurrence = "recurrence", Series = "series", Occurrence = "occurrence", Reminders = "reminders", Completed = "completed",
        Source = "source", TimeBlock = "time_block", Tz = "tz", Stage = "stage", MyDay = "my_day", MyDayOrder = "my_day_order",
        RolledOver = "rolled_over";
}

public static class TaskStatus
{
    public const string Todo = "todo", Doing = "doing", Waiting = "waiting", Done = "done", Cancelled = "cancelled";
    public static readonly string[] All = [Todo, Doing, Waiting, Done, Cancelled];
    public static bool IsClosed(string? s) => s is Done or Cancelled;
}

public sealed record CreateTaskRequest
{
    public required string Title { get; init; }
    public string Workspace { get; init; } = Storage.Vault.DefaultWorkspace;
    public string Body { get; init; } = "";
    public string Status { get; init; } = TaskStatus.Todo;
    public string? Due { get; init; }
    public string? Planned { get; init; }
    public int? Priority { get; init; }
    public int? Effort { get; init; }
    public string? Parent { get; init; }
    public string? Project { get; init; }
    public IReadOnlyList<string>? Assignees { get; init; }
    public string? Source { get; init; }
    public string? Recurrence { get; init; }
    public IReadOnlyList<string>? DependsOn { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public IReadOnlyList<string>? Reminders { get; init; }
    public string? WaitingOn { get; init; }
    public string? Stage { get; init; }
}

public sealed record ReminderDto(string At, string? SnoozedUntil, string? Fired);

public sealed record TimeBlockDto(string Start, string End);

public sealed record TaskDto(
    string Id, string Title, string Workspace, string Path, string Status, int? Priority, string? Due, string? Planned, int? Effort,
    string? Project, IReadOnlyList<string> Assignees, string? WaitingOn, string? Parent, IReadOnlyList<string> DependsOn,
    string? Recurrence, string? Series, string? Occurrence, string? Completed, string? Source, TimeBlockDto? TimeBlock,
    IReadOnlyList<ReminderDto> Reminders, IReadOnlyList<string> Tags, string? Modified, string? Created, bool Archived,
    bool Blocked, int SubtaskCount, int SubtasksDone, int? EffortRollup, string? Stage, string? MyDay, int? MyDayOrder, int RolledOver);

/// <summary>
/// Tasks are first-class nodes (BUILD-PLAN 8B). This service keeps a task's identity and state the
/// same everywhere it appears: inline checkbox lines in notes bind to task nodes and sync both ways.
/// </summary>
public sealed partial class TaskService : ITaskHooks
{
    private readonly WriteService _writes;
    [ThreadStatic] private static int _syncDepth;

    public TaskService(WriteService writes)
    {
        _writes = writes;
        writes.TaskHooks = this;
    }

    private CacheIndex Cache => _writes.Cache;

    // ---- create / read ------------------------------------------------------------------------

    public Node Create(CreateTaskRequest r, Actor actor)
    {
        var title = r.Title.Trim();
        if (title.Length == 0) throw new QuantosException("A task needs a title.");
        if (!TaskStatus.All.Contains(r.Status)) throw new QuantosException($"Unknown status '{r.Status}'.");
        var props = new Dictionary<string, object?> { [TaskKeys.Status] = r.Status, [TaskKeys.Tz] = _writes.Clock.Zone.Id };
        void Put(string key, object? value) { if (value is not null && (value is not string s || s.Length > 0)) props[key] = value; }
        Put(TaskKeys.Priority, r.Priority is >= 1 and <= 4 ? r.Priority : null);
        Put(TaskKeys.Due, ValidDate(r.Due, "due"));
        Put(TaskKeys.Planned, ValidDate(r.Planned, "planned"));
        Put(TaskKeys.Effort, r.Effort is > 0 ? r.Effort : null);
        Put(TaskKeys.Parent, r.Parent);
        Put(TaskKeys.Project, r.Project);
        Put(TaskKeys.WaitingOn, r.WaitingOn);
        Put(TaskKeys.Stage, r.Stage);
        if (r.Assignees is { Count: > 0 }) props[TaskKeys.Assignees] = r.Assignees.ToList();
        if (r.DependsOn is { Count: > 0 }) props[TaskKeys.DependsOn] = r.DependsOn.ToList();
        Put(TaskKeys.Source, r.Source);
        if (Recurrence.Parse(r.Recurrence) is not null)
        {
            props[TaskKeys.Recurrence] = r.Recurrence;
            props[TaskKeys.Series] = Ulid.New();
            props[TaskKeys.Occurrence] = (Props.Date(r.Planned) ?? Props.Date(r.Due) ?? _writes.Clock.Today).ToString("yyyy-MM-dd");
        }
        if (r.Reminders is { Count: > 0 })
            props[TaskKeys.Reminders] = r.Reminders.Select(at => (object?)new Dictionary<string, object?> { ["at"] = ValidDateTime(at) }).ToList();
        if (TaskStatus.IsClosed(r.Status)) props[TaskKeys.Completed] = _writes.Clock.Stamp();
        return _writes.Create(new CreateNodeRequest { Title = title, Type = NodeTypes.Task, Workspace = r.Workspace, Body = r.Body, Tags = r.Tags, Properties = props }, actor);
    }

    public TaskDto? Get(string id)
    {
        var all = AllSummaries();
        var summary = all.FirstOrDefault(s => s.Id == id);
        return summary is null ? null : ToDto(summary, all);
    }

    public TaskDto Require(string id) => Get(id) ?? throw new QuantosException("Task not found.", 404);

    public IReadOnlyList<TaskDto> List(string? workspace = null, bool includeClosed = true, bool includeArchived = false)
    {
        var all = AllSummaries();
        return all.Where(s => (workspace is null || s.Workspace == workspace) && (includeArchived || !s.Archived))
            .Select(s => ToDto(s, all))
            .Where(t => includeClosed || !TaskStatus.IsClosed(t.Status))
            .ToList();
    }

    private List<NodeSummary> AllSummaries() => Cache.List(type: NodeTypes.Task, includeArchived: true, limit: 1_000_000).ToList();

    private static TaskDto ToDto(NodeSummary s, IReadOnlyList<NodeSummary> all)
    {
        var p = s.Properties;
        var status = Props.Str(p, TaskKeys.Status) ?? TaskStatus.Todo;
        var children = all.Where(c => Props.LinkId(Props.Str(c.Properties, TaskKeys.Parent)) == s.Id).ToList();
        var deps = Props.List(p, TaskKeys.DependsOn).Select(d => Props.LinkId(d) ?? d).ToList();
        var blocked = deps.Any(d => all.FirstOrDefault(t => t.Id == d) is not { } dep || !TaskStatus.IsClosed(Props.Str(dep.Properties, TaskKeys.Status)));
        var block = Props.Map(p, TaskKeys.TimeBlock);
        return new TaskDto(
            s.Id, s.Title, s.Workspace, s.Path, status, Props.Int(p, TaskKeys.Priority), Props.Str(p, TaskKeys.Due), Props.Str(p, TaskKeys.Planned),
            Props.Int(p, TaskKeys.Effort), Props.Str(p, TaskKeys.Project), Props.List(p, TaskKeys.Assignees), Props.Str(p, TaskKeys.WaitingOn),
            Props.LinkId(Props.Str(p, TaskKeys.Parent)), deps, Props.Str(p, TaskKeys.Recurrence), Props.Str(p, TaskKeys.Series),
            Props.Str(p, TaskKeys.Occurrence), Props.Str(p, TaskKeys.Completed), Props.Str(p, TaskKeys.Source),
            block is not null && block.GetValueOrDefault("start") is { } bs && block.GetValueOrDefault("end") is { } be ? new TimeBlockDto(bs, be) : null,
            Props.Maps(p, TaskKeys.Reminders).Where(m => m.GetValueOrDefault("at") is not null)
                .Select(m => new ReminderDto(m["at"]!, m.GetValueOrDefault("snoozed_until"), m.GetValueOrDefault("fired"))).ToList(),
            s.Tags, s.Modified, s.Created, s.Archived, blocked, children.Count,
            children.Count(c => TaskStatus.IsClosed(Props.Str(c.Properties, TaskKeys.Status))),
            EffortRollup(s, all, []), Props.Str(p, TaskKeys.Stage), Props.Str(p, TaskKeys.MyDay), Props.Int(p, TaskKeys.MyDayOrder),
            Props.Int(p, TaskKeys.RolledOver) ?? 0);
    }

    /// <summary>Own effort plus all subtasks' effort (BUILD-PLAN 8B.7). Loops are cut, never followed.</summary>
    private static int? EffortRollup(NodeSummary s, IReadOnlyList<NodeSummary> all, HashSet<string> seen)
    {
        if (!seen.Add(s.Id)) return null;
        var own = Props.Int(s.Properties, TaskKeys.Effort);
        var sum = own ?? 0;
        var any = own.HasValue;
        foreach (var child in all.Where(c => Props.LinkId(Props.Str(c.Properties, TaskKeys.Parent)) == s.Id))
        {
            var r = EffortRollup(child, all, seen);
            if (r.HasValue) { sum += r.Value; any = true; }
        }
        return any ? sum : null;
    }

    // ---- changes ------------------------------------------------------------------------------

    public TaskDto SetStatus(string id, string status, Actor actor)
    {
        if (!TaskStatus.All.Contains(status)) throw new QuantosException($"Unknown status '{status}'.");
        var before = Require(id);
        var set = new Dictionary<string, object?> { [TaskKeys.Status] = status };
        var remove = new List<string>();
        if (TaskStatus.IsClosed(status) && !TaskStatus.IsClosed(before.Status)) set[TaskKeys.Completed] = _writes.Clock.Stamp();
        if (!TaskStatus.IsClosed(status)) remove.Add(TaskKeys.Completed);
        if (before.Status != status || (TaskStatus.IsClosed(status) && before.Completed is null))
        {
            var result = _writes.UpdateFields(id, null, set, remove.Where(k => before.Completed is not null || k != TaskKeys.Completed), actor);
            if (result.Status is SaveStatus.Conflict or SaveStatus.Invalid or SaveStatus.NotFound) throw new QuantosException(result.Message ?? "Couldn't update the task.", 409);
        }
        SyncInline(id, TaskStatus.IsClosed(status), null, actor);
        if (TaskStatus.IsClosed(status) && !TaskStatus.IsClosed(before.Status)) GenerateNext(Require(id), actor);
        return Require(id);
    }

    private static readonly HashSet<string> Editable =
    [
        TaskKeys.Priority, TaskKeys.Due, TaskKeys.Planned, TaskKeys.Effort, TaskKeys.Assignees, TaskKeys.Project, TaskKeys.WaitingOn,
        TaskKeys.Parent, TaskKeys.DependsOn, TaskKeys.Recurrence, TaskKeys.Reminders, TaskKeys.TimeBlock, TaskKeys.Stage, TaskKeys.MyDay,
        TaskKeys.MyDayOrder, NodeKeys.Tags
    ];

    public TaskDto Update(string id, IReadOnlyDictionary<string, object?> set, IReadOnlyList<string> remove, Actor actor)
    {
        var current = Require(id);
        var clean = new Dictionary<string, object?>();
        foreach (var (key, value) in set)
        {
            if (key == TaskKeys.Status) { SetStatus(id, Props.Str(value) ?? TaskStatus.Todo, actor); continue; }
            if (!Editable.Contains(key) && NodeKeys.Reserved.Contains(key)) throw new QuantosException($"'{key}' can't be changed here.");
            clean[key] = key switch
            {
                TaskKeys.Due => ValidDate(Props.Str(value), "due"),
                TaskKeys.Planned => ValidDate(Props.Str(value), "planned"),
                TaskKeys.MyDay => ValidDate(Props.Str(value), "my day"),
                TaskKeys.Priority => Props.Str(value) is null ? null : int.TryParse(Props.Str(value), out var pr) && pr is >= 1 and <= 4 ? pr : throw new QuantosException("Priority is 1 (highest) to 4."),
                TaskKeys.Effort => Props.Str(value) is null ? null : int.TryParse(Props.Str(value), out var ef) && ef >= 0 ? ef : throw new QuantosException("Effort is a number of minutes."),
                TaskKeys.Parent => ValidateParent(id, Props.LinkId(Props.Str(value)) ?? Props.Str(value)),
                TaskKeys.DependsOn => ValidateDependencies(id, value),
                TaskKeys.Recurrence => Props.Str(value) is { } rule && Recurrence.Parse(rule) is null ? throw new QuantosException("Recurrence isn't recognised. Try daily, weekly, weekdays, monthly or an RRULE.") : value,
                _ => value
            };
        }
        // A newly recurring task starts a series anchored on its scheduled date.
        if (clean.ContainsKey(TaskKeys.Recurrence) && Props.Str(clean[TaskKeys.Recurrence]) is not null && current.Series is null)
        {
            clean[TaskKeys.Series] = Ulid.New();
            clean[TaskKeys.Occurrence] = (Props.Date(current.Planned) ?? Props.Date(current.Due) ?? _writes.Clock.Today).ToString("yyyy-MM-dd");
        }
        var nulls = clean.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList();
        foreach (var k in nulls) clean.Remove(k);
        if (clean.Count > 0 || nulls.Count > 0 || remove.Count > 0)
        {
            var result = _writes.UpdateFields(id, null, clean, [.. remove, .. nulls], actor);
            if (result.Status is SaveStatus.Conflict or SaveStatus.Invalid or SaveStatus.NotFound) throw new QuantosException(result.Message ?? "Couldn't update the task.", 409);
        }
        return Require(id);
    }

    private string? ValidateParent(string id, string? parent)
    {
        if (parent is null) return null;
        if (parent == id) throw new QuantosException("A task can't be its own parent.");
        // Walk up to make sure we don't create a loop.
        var seen = new HashSet<string> { id };
        for (var cursor = parent; cursor is not null;)
        {
            if (!seen.Add(cursor)) throw new QuantosException("That would make a loop of subtasks.");
            cursor = Get(cursor)?.Parent;
        }
        return parent;
    }

    private List<string> ValidateDependencies(string id, object? value)
    {
        var deps = Props.List(new Dictionary<string, object?> { ["d"] = value }, "d").Select(d => Props.LinkId(d) ?? d).Distinct().ToList();
        if (deps.Contains(id)) throw new QuantosException("A task can't depend on itself.");
        // Reject cycles: nothing we depend on may (transitively) depend on us.
        var stack = new Stack<string>(deps);
        var seen = new HashSet<string>();
        while (stack.Count > 0)
        {
            var next = stack.Pop();
            if (!seen.Add(next)) continue;
            var t = Get(next);
            if (t is null) continue;
            if (t.DependsOn.Contains(id)) throw new QuantosException($"“{t.Title}” already depends on this task, so this would make a loop.");
            foreach (var d in t.DependsOn) stack.Push(d);
        }
        return deps;
    }

    /// <summary>Recurrence: the next occurrence is a separate task, calculated from the scheduled date, never generated twice.</summary>
    public Node? GenerateNext(TaskDto task, Actor actor)
    {
        var rule = Recurrence.Parse(task.Recurrence);
        if (rule is null) return null;
        var scheduled = Props.Date(task.Occurrence) ?? Props.Date(task.Planned) ?? Props.Date(task.Due) ?? _writes.Clock.Today;
        var series = task.Series;
        if (series is null)
        {
            series = Ulid.New();
            _writes.UpdateFields(task.Id, null, new Dictionary<string, object?> { [TaskKeys.Series] = series, [TaskKeys.Occurrence] = scheduled.ToString("yyyy-MM-dd") }, null, Actor.System);
        }
        var next = rule.Next(scheduled);
        var nextKey = next.ToString("yyyy-MM-dd");
        var existing = Cache.Query("WHERE type='task' AND json_extract(props, '$.series') = $s AND json_extract(props, '$.occurrence') = $o",
            ("$s", series), ("$o", nextKey));
        if (existing.Count > 0) return null;

        var delta = next.DayNumber - scheduled.DayNumber;
        string? Shift(string? value) => value is null ? null : ShiftDate(value, delta);
        var node = _writes.Require(task.Id);
        var body = FrontmatterDocumentBody(node);
        var created = Create(new CreateTaskRequest
        {
            Title = task.Title, Workspace = task.Workspace, Body = body, Due = Shift(task.Due), Planned = Shift(task.Planned) ?? (task.Due is null ? nextKey : null),
            Priority = task.Priority, Effort = task.Effort, Project = task.Project, Assignees = task.Assignees, Tags = task.Tags, Parent = task.Parent,
            WaitingOn = null, Stage = null, Reminders = task.Reminders.Select(r => ShiftDate(r.At, delta)).ToList()
        }, actor);
        _writes.UpdateFields(created.Id, null, new Dictionary<string, object?>
        {
            [TaskKeys.Recurrence] = task.Recurrence, [TaskKeys.Series] = series, [TaskKeys.Occurrence] = nextKey
        }, null, Actor.System);
        return _writes.Require(created.Id);
    }

    private static string FrontmatterDocumentBody(Node node) => node.Body;

    /// <summary>Rollover changes the planned date only; the due date never moves automatically (confirmed).</summary>
    public int Rollover(string workspace, DateOnly today, Actor actor)
    {
        var count = 0;
        foreach (var t in List(workspace, includeClosed: false))
        {
            if (Props.Date(t.Planned) is not { } planned || planned >= today) continue;
            _writes.UpdateFields(t.Id, null, new Dictionary<string, object?>
            {
                [TaskKeys.Planned] = today.ToString("yyyy-MM-dd"),
                [TaskKeys.RolledOver] = t.RolledOver + 1
            }, null, actor);
            count++;
        }
        return count;
    }

    public TaskDto Snooze(string id, TimeSpan duration, Actor actor)
    {
        var task = Require(id);
        var until = _writes.Clock.Now.Add(duration).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        var reminders = task.Reminders.ToList();
        if (reminders.Count == 0) reminders.Add(new ReminderDto(until, null, null));
        var index = reminders.FindIndex(r => r.Fired is not null);
        if (index < 0) index = 0;
        reminders[index] = reminders[index] with { SnoozedUntil = until, Fired = null };
        return Update(id, new Dictionary<string, object?> { [TaskKeys.Reminders] = ReminderList(reminders) }, [], actor);
    }

    public static List<object?> ReminderList(IEnumerable<ReminderDto> reminders) =>
        reminders.Select(r =>
        {
            var map = new Dictionary<string, object?> { ["at"] = r.At };
            if (r.SnoozedUntil is not null) map["snoozed_until"] = r.SnoozedUntil;
            if (r.Fired is not null) map["fired"] = r.Fired;
            return (object?)map;
        }).ToList();

    // ---- inline task lines --------------------------------------------------------------------

    [GeneratedRegex(@"^(?<lead>[ \t]*[-*+][ \t]+)\[(?<check>[ xX])\](?<gap>[ \t]+)(?<text>.*?)(?<marker>[ \t]*<!--task:(?<id>[0-9A-HJKMNP-TV-Z]{26})-->)?[ \t]*$")]
    private static partial Regex TaskLine();

    /// <summary>New "- [ ] text" lines in a note become task nodes, bound by an invisible marker.</summary>
    public string BindInlineTasks(FrontmatterDocument doc, string body, Actor actor)
    {
        var workspace = doc.GetString(NodeKeys.Workspace) ?? "";
        var type = doc.GetString(NodeKeys.Type) ?? NodeTypes.Note;
        var noteId = doc.GetString(NodeKeys.Id);
        if (workspace.Length == 0 || type is NodeTypes.Template or NodeTypes.Task || noteId is null || !InlineTasksEnabled()) return body;
        var planned = type == NodeTypes.Daily ? doc.GetString("date") : null;
        var nl = body.Contains("\r\n") ? "\r\n" : "\n";
        var lines = body.Split('\n');
        var fence = false;
        var changed = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.TrimStart().StartsWith("```") || line.TrimStart().StartsWith("~~~")) { fence = !fence; continue; }
            if (fence) continue;
            var m = TaskLine().Match(line);
            if (!m.Success || m.Groups["id"].Success) continue;
            var text = m.Groups["text"].Value.Trim();
            if (text.Length == 0) continue;
            var checkedBox = m.Groups["check"].Value is "x" or "X";
            var task = Create(new CreateTaskRequest
            {
                Title = TitleFromLine(text), Workspace = workspace, Source = noteId, Planned = planned,
                Status = checkedBox ? TaskStatus.Done : TaskStatus.Todo
            }, actor);
            lines[i] = line + $" <!--task:{task.Id}-->" + (lines[i].EndsWith('\r') ? "\r" : "");
            changed = true;
        }
        _ = nl;
        return changed ? string.Join('\n', lines) : body;
    }

    private static string TitleFromLine(string text) =>
        Regex.Replace(text, @"\[\[[0-9A-HJKMNP-TV-Z]{26}(?:#[^\]|]+)?\|([^\]]+)\]\]", "$1").Trim();

    private bool InlineTasksEnabled()
    {
        var settings = Path.Combine(_writes.Vault.Root, "settings.json");
        if (!File.Exists(settings)) return true;
        try { return !File.ReadAllText(settings).Contains("\"inlineTasks\": false", StringComparison.OrdinalIgnoreCase) && !File.ReadAllText(settings).Contains("\"inlineTasks\":false", StringComparison.OrdinalIgnoreCase); }
        catch (IOException) { return true; }
    }

    /// <summary>After a note is saved: a checkbox toggled in the text completes or reopens its task; editing the line in the task's source note renames it.</summary>
    public void AfterNoteWritten(Node note, Actor actor)
    {
        if (note.Type == NodeTypes.Task || _syncDepth > 2) return;
        _syncDepth++;
        try
        {
            foreach (var (id, isChecked, text, _) in Links.InlineTasks(note.Body))
            {
                var task = Get(id);
                if (task is null) continue; // trashed or missing: the line shows a placeholder with Restore
                if (isChecked != TaskStatus.IsClosed(task.Status)) SetStatus(id, isChecked ? TaskStatus.Done : TaskStatus.Todo, actor);
                var title = TitleFromLine(text);
                if (task.Source == note.Id && title.Length > 0 && title != task.Title)
                {
                    _writes.Rename(id, title, actor);
                    SyncInline(id, null, title, actor, except: note.Id);
                }
            }
        }
        finally { _syncDepth--; }
    }

    /// <summary>Brings every inline line bound to a task in line with the task (checkbox and text).</summary>
    public void SyncInline(string taskId, bool? done, string? title, Actor actor, string? except = null)
    {
        foreach (var (noteId, isChecked, text) in Cache.InlineTaskRefs(taskId))
        {
            if (noteId == except) continue;
            var needsCheck = done.HasValue && isChecked != done.Value;
            var needsText = title is not null && TitleFromLine(text) != title;
            if (!needsCheck && !needsText) continue;
            var note = _writes.Get(noteId);
            if (note is null || note.ReadOnly) continue;
            var body = Regex.Replace(note.Body, @"^(?<lead>[ \t]*[-*+][ \t]+)\[(?<check>[ xX])\](?<gap>[ \t]+)(?<text>.*?)(?<marker>[ \t]*<!--task:" + taskId + "-->)",
                m => m.Groups["lead"].Value + "[" + (done.HasValue ? (done.Value ? "x" : " ") : m.Groups["check"].Value) + "]" + m.Groups["gap"].Value +
                     (title ?? m.Groups["text"].Value) + m.Groups["marker"].Value, RegexOptions.Multiline);
            if (body == note.Body) continue;
            _syncDepth++;
            try { _writes.UpdateFields(noteId, null, null, null, actor, body); }
            finally { _syncDepth--; }
        }
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static string? ValidDate(string? value, string what)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return value;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return value.Length > 10 ? dt.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture) : dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        throw new QuantosException($"The {what} date isn't a date (use YYYY-MM-DD).");
    }

    private string ValidDateTime(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)
            ? dt.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)
            : throw new QuantosException("A reminder needs a date and time.");

    private static string ShiftDate(string value, int days)
    {
        if (value.Length == 10 && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.AddDays(days).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        return value;
    }
}

/// <summary>Hooks the write service calls so inline task lines stay bound to task nodes.</summary>
public interface ITaskHooks
{
    string BindInlineTasks(FrontmatterDocument doc, string body, Actor actor);
    void AfterNoteWritten(Node note, Actor actor);
}
