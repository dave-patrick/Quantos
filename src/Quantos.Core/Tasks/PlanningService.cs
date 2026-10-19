using System.Globalization;
using Quantos.Core.Cache;
using Quantos.Core.Security;

namespace Quantos.Core.Tasks;

public sealed record SprintDto(string Id, string Title, string? Start, string? End, string? Goal, string State, int? Capacity,
    int Tasks, int Done, int Effort, int EffortDone);
public sealed record BurnPoint(string Date, double? Remaining, double Ideal);
public sealed record SprintDetail(SprintDto Sprint, IReadOnlyList<TaskDto> Tasks, IReadOnlyList<BurnPoint> Burndown, string Unit);
public sealed record CreateSprintRequest(string Workspace, string Title, string Start, string End, string? Goal, int? Capacity);

public sealed record GoalDto(string Id, string Title, string Type, string? Parent, string? Target, string Health, double Progress,
    double? Expected, int Tasks, int Done, int Overdue, int Blocked, IReadOnlyList<string> Children);
public sealed record WeekCount(string Week, int Created, int Completed);
public sealed record TaskAnalytics(IReadOnlyDictionary<string, int> ByStatus, int Open, int Overdue, int Blocked, int Waiting,
    int DueThisWeek, double? CycleDays, IReadOnlyList<WeekCount> Weeks);

/// <summary>
/// Sprints, goal rollups and status analytics (Release 1.1). Sprints and goals are ordinary notes
/// (<c>type: sprint</c>, <c>type: goal</c>); tasks join a sprint with a <c>sprint: [[link]]</c> property and roll up
/// to goals and projects through <c>project</c>. Projects join a goal with <c>goal</c>, goals nest with <c>parent</c>.
/// Everything here is computed from those files; nothing extra is stored.
/// </summary>
public sealed class PlanningService(WriteService writes, TaskService tasks)
{
    public const string Sprint = "sprint", Goal = "goal", Start = "start", End = "end", Capacity = "capacity", Target = "target",
        GoalKey = "goal", ParentKey = "parent", SprintGoal = "sprint_goal";

    private DateOnly Today => writes.Clock.Today;

    // ---- sprints ------------------------------------------------------------------------------

    public IReadOnlyList<SprintDto> Sprints(string workspace, Actor actor)
    {
        writes.Permissions.Demand(actor, Permission.Read, "Read sprints");
        var (all, props) = Tasks(workspace);
        return writes.Cache.List(workspace, Sprint, includeArchived: false, limit: 10_000)
            .Select(s => ToSprint(s, Members(s.Id, all, props)))
            .OrderByDescending(s => s.Start).ToList();
    }

    public SprintDetail SprintDetail(string id, Actor actor)
    {
        writes.Permissions.Demand(actor, Permission.Read, "Read a sprint");
        var node = writes.Require(id);
        if (node.Type != Sprint) throw new QuantosException("That note isn't a sprint.", 404);
        var summary = writes.Cache.List(node.Workspace, Sprint, includeArchived: true, limit: 10_000).First(s => s.Id == id);
        var (all, props) = Tasks(node.Workspace);
        var members = Members(id, all, props);
        var dto = ToSprint(summary, members);
        return new SprintDetail(dto, members, Burndown(dto, members, out var unit), unit);
    }

    public Node CreateSprint(CreateSprintRequest r, Actor actor)
    {
        var start = Props.Date(r.Start) ?? throw new QuantosException("Give the sprint a start date.");
        var end = Props.Date(r.End) ?? throw new QuantosException("Give the sprint an end date.");
        if (end < start) throw new QuantosException("A sprint has to end after it starts.");
        if (end.DayNumber - start.DayNumber > 120) throw new QuantosException("Sprints are at most 120 days; use a goal for longer work.");
        var props = new Dictionary<string, object?> { [Start] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), [End] = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        if (!string.IsNullOrWhiteSpace(r.Goal)) props[SprintGoal] = r.Goal.Trim();
        if (r.Capacity is > 0) props[Capacity] = r.Capacity;
        return writes.Create(new CreateNodeRequest { Title = r.Title.Trim(), Type = Sprint, Workspace = r.Workspace, Properties = props }, actor);
    }

    /// <summary>Moves a sprint's unfinished tasks to another sprint (or back to the backlog when <paramref name="toId"/> is null).</summary>
    public int CarryOver(string fromId, string? toId, Actor actor)
    {
        var from = writes.Require(fromId);
        Node? to = toId is null ? null : writes.Require(toId);
        if (to is not null && to.Type != Sprint) throw new QuantosException("Carry tasks over to a sprint.");
        var (all, props) = Tasks(from.Workspace);
        var moved = 0;
        foreach (var t in Members(fromId, all, props).Where(t => t.Status is not (TaskStatus.Done or TaskStatus.Cancelled)))
        {
            var r = to is null
                ? writes.UpdateFields(t.Id, null, null, [Sprint], actor)
                : writes.UpdateFields(t.Id, null, new Dictionary<string, object?> { [Sprint] = $"[[{to.Id}|{to.Title}]]" }, null, actor);
            if (r.Status == SaveStatus.Saved) moved++;
        }
        return moved;
    }

    /// <summary>Open tasks not in any sprint (a task whose sprint note is gone counts as unplanned).</summary>
    public IReadOnlyList<TaskDto> Backlog(string workspace, Actor actor)
    {
        writes.Permissions.Demand(actor, Permission.Read, "Read the backlog");
        var (all, props) = Tasks(workspace);
        var sprints = writes.Cache.List(workspace, Sprint, includeArchived: true, limit: 10_000).Select(s => s.Id).ToHashSet();
        return all.Where(t => t.Status is not (TaskStatus.Done or TaskStatus.Cancelled) && !t.Archived
            && !(props.TryGetValue(t.Id, out var p) && Props.LinkId(Props.Str(p, Sprint)) is { } id && sprints.Contains(id))).ToList();
    }

    private SprintDto ToSprint(NodeSummary s, IReadOnlyList<TaskDto> members)
    {
        var start = Props.Date(Props.Str(s.Properties, Start));
        var end = Props.Date(Props.Str(s.Properties, End));
        var state = string.Equals(Props.Str(s.Properties, "status"), "done", StringComparison.OrdinalIgnoreCase) || (end is { } e && e < Today) ? "done"
            : start is { } st && st > Today ? "planned" : "active";
        var live = members.Where(t => t.Status != TaskStatus.Cancelled).ToList();
        var done = live.Where(t => t.Status == TaskStatus.Done).ToList();
        return new SprintDto(s.Id, s.Title, start?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), end?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Props.Str(s.Properties, SprintGoal), state, Props.Int(s.Properties, Capacity),
            live.Count, done.Count, live.Sum(t => t.Effort ?? 0), done.Sum(t => t.Effort ?? 0));
    }

    /// <summary>Remaining work per day: effort in minutes when every task has an estimate, otherwise a task count.</summary>
    private List<BurnPoint> Burndown(SprintDto s, IReadOnlyList<TaskDto> members, out string unit)
    {
        var live = members.Where(t => t.Status != TaskStatus.Cancelled).ToList();
        var byEffort = live.Count > 0 && live.All(t => t.Effort is > 0);
        unit = byEffort ? "min" : "tasks";
        var points = new List<BurnPoint>();
        if (Props.Date(s.Start) is not { } start || Props.Date(s.End) is not { } end) return points;
        double Weight(TaskDto t) => byEffort ? t.Effort!.Value : 1;
        var total = live.Sum(Weight);
        var span = Math.Max(1, end.DayNumber - start.DayNumber);
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            double? remaining = d > Today ? null : live.Where(t => !(t.Status == TaskStatus.Done && Props.Date(t.Completed) is { } c && c <= d)).Sum(Weight);
            points.Add(new BurnPoint(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), remaining, Math.Round(total * (1 - (double)(d.DayNumber - start.DayNumber) / span), 2)));
        }
        return points;
    }

    private (IReadOnlyList<TaskDto> All, Dictionary<string, IReadOnlyDictionary<string, object?>> Props) Tasks(string workspace)
    {
        var all = tasks.List(workspace, includeClosed: true);
        var props = writes.Cache.List(workspace, NodeTypes.Task, includeArchived: false, limit: 1_000_000)
            .ToDictionary(s => s.Id, s => (IReadOnlyDictionary<string, object?>)s.Properties);
        return (all, props);
    }

    private static List<TaskDto> Members(string sprintId, IReadOnlyList<TaskDto> all, Dictionary<string, IReadOnlyDictionary<string, object?>> props) =>
        all.Where(t => props.TryGetValue(t.Id, out var p) && Props.LinkId(Props.Str(p, Sprint)) == sprintId).ToList();

    // ---- goals and analytics ----------------------------------------------------------------

    public IReadOnlyList<GoalDto> Goals(string workspace, Actor actor)
    {
        writes.Permissions.Demand(actor, Permission.Read, "Read goals");
        // Projects are notes typed or tagged "project", and any note that tasks name as their project.
        var all = tasks.List(workspace, includeClosed: true).Where(t => t.Status != TaskStatus.Cancelled).ToList();
        var named = all.Select(t => Props.LinkId(t.Project)).OfType<string>().ToHashSet();
        var nodes = writes.Cache.List(workspace, null, includeArchived: false, limit: 1_000_000)
            .Where(n => n.Type is Goal or NodeTypes.Project || (n.Type != NodeTypes.Task && (named.Contains(n.Id) || n.Tags.Contains(NodeTypes.Project, StringComparer.OrdinalIgnoreCase))))
            .ToList();
        var byId = nodes.ToDictionary(n => n.Id);
        string? ParentOf(NodeSummary n) => Props.LinkId(Props.Str(n.Properties, n.Type == Goal ? ParentKey : GoalKey)) is { } p && byId.ContainsKey(p) && p != n.Id ? p : null;
        var children = nodes.GroupBy(ParentOf).Where(g => g.Key is not null).ToDictionary(g => g.Key!, g => g.Select(n => n.Id).ToList());
        var direct = all.Where(t => Props.LinkId(t.Project) is { } p && byId.ContainsKey(p)).GroupBy(t => Props.LinkId(t.Project)!).ToDictionary(g => g.Key, g => g.ToList());

        List<TaskDto> Under(string id, HashSet<string> seen)
        {
            if (!seen.Add(id)) return [];
            var list = direct.TryGetValue(id, out var d) ? new List<TaskDto>(d) : [];
            foreach (var c in children.GetValueOrDefault(id) ?? []) list.AddRange(Under(c, seen));
            return list;
        }

        return nodes.Select(n =>
        {
            var under = Under(n.Id, []).DistinctBy(t => t.Id).ToList();
            var done = under.Count(t => t.Status == TaskStatus.Done);
            var overdue = under.Count(t => t.Status != TaskStatus.Done && Props.Date(t.Due) is { } due && due < Today);
            var blocked = under.Count(t => t.Status != TaskStatus.Done && t.Blocked);
            var progress = under.Count == 0 ? 0 : (double)done / under.Count;
            var target = Props.Date(Props.Str(n.Properties, Target)) ?? Props.Date(Props.Str(n.Properties, "due"));
            var created = Props.Date(n.Created);
            double? expected = target is { } tg && created is { } cr && tg > cr ? Math.Clamp((double)(Today.DayNumber - cr.DayNumber) / (tg.DayNumber - cr.DayNumber), 0, 1) : null;
            var finished = string.Equals(Props.Str(n.Properties, "status"), "done", StringComparison.OrdinalIgnoreCase) || (under.Count > 0 && done == under.Count);
            var health = finished ? "done"
                : under.Count == 0 ? "no tasks"
                : target is { } t2 && t2 < Today ? "off track"
                : expected is { } x ? (progress >= x - 0.1 ? "on track" : progress >= x - 0.25 ? "at risk" : "off track")
                : overdue > 0 ? "at risk" : "on track";
            return new GoalDto(n.Id, n.Title, n.Type == Goal ? Goal : NodeTypes.Project, ParentOf(n), target?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), health,
                Math.Round(progress, 3), expected is null ? null : Math.Round(expected.Value, 3), under.Count, done, overdue, blocked, children.GetValueOrDefault(n.Id) ?? []);
        }).OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public TaskAnalytics Analytics(string workspace, int weeks, Actor actor)
    {
        writes.Permissions.Demand(actor, Permission.Read, "Read task analytics");
        weeks = Math.Clamp(weeks, 1, 52);
        var all = tasks.List(workspace, includeClosed: true);
        var open = all.Where(t => t.Status is not (TaskStatus.Done or TaskStatus.Cancelled)).ToList();
        var monday = Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7));
        var weekEnd = monday.AddDays(6);
        var list = new List<WeekCount>();
        for (var i = weeks - 1; i >= 0; i--)
        {
            var from = monday.AddDays(-7 * i);
            var to = from.AddDays(6);
            bool In(string? v) => Props.Date(v) is { } d && d >= from && d <= to;
            list.Add(new WeekCount(from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), all.Count(t => In(t.Created)), all.Count(t => t.Status == TaskStatus.Done && In(t.Completed))));
        }
        var recent = all.Where(t => t.Status == TaskStatus.Done && Props.Date(t.Completed) is { } c && c >= Today.AddDays(-30) && Props.Date(t.Created) is not null)
            .Select(t => Props.Date(t.Completed)!.Value.DayNumber - Props.Date(t.Created)!.Value.DayNumber).ToList();
        return new TaskAnalytics(
            TaskStatus.All.ToDictionary(s => s, s => all.Count(t => t.Status == s)),
            open.Count,
            open.Count(t => Props.Date(t.Due) is { } d && d < Today),
            open.Count(t => t.Blocked),
            open.Count(t => t.Status == TaskStatus.Waiting),
            open.Count(t => Props.Date(t.Due) is { } d && d >= Today && d <= weekEnd),
            recent.Count > 0 ? Math.Round(recent.Average(), 1) : null,
            list);
    }
}
