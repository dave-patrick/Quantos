using System.Globalization;
using Quantos.Core;
using Quantos.Core.Security;
using Quantos.Core.Tasks;

namespace Quantos.Tests;

/// <summary>Release 1.1 sprints, goal rollups and analytics.</summary>
public class PlanningTests
{
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public void A_sprint_collects_its_tasks_burns_down_and_carries_over()
    {
        using var v = new TestVault();
        var plan = v.Session.Planning;
        var today = v.Writes.Clock.Today;
        var sprint = plan.CreateSprint(new CreateSprintRequest("default", "Sprint 1", D(today.AddDays(-3)), D(today.AddDays(4)), "Ship beta", 600), Actor.User);
        var next = plan.CreateSprint(new CreateSprintRequest("default", "Sprint 2", D(today.AddDays(5)), D(today.AddDays(12)), null, null), Actor.User);
        var link = new Dictionary<string, object?> { ["sprint"] = $"[[{sprint.Id}|Sprint 1]]" };
        var a = v.Session.Tasks.Create(new CreateTaskRequest { Title = "A", Effort = 60 }, Actor.User);
        var b = v.Session.Tasks.Create(new CreateTaskRequest { Title = "B", Effort = 30 }, Actor.User);
        v.Session.Tasks.Create(new CreateTaskRequest { Title = "Backlog" }, Actor.User);
        v.Writes.UpdateFields(a.Id, null, link, [], Actor.User);
        v.Writes.UpdateFields(b.Id, null, link, [], Actor.User);
        v.Session.Tasks.Update(a.Id, new Dictionary<string, object?> { ["status"] = "done" }, [], Actor.User);

        var detail = plan.SprintDetail(sprint.Id, Actor.User);
        Assert.Equal("active", detail.Sprint.State);
        Assert.Equal((2, 1, 90, 60), (detail.Sprint.Tasks, detail.Sprint.Done, detail.Sprint.Effort, detail.Sprint.EffortDone));
        Assert.Equal("min", detail.Unit);
        Assert.Equal(8, detail.Burndown.Count);
        Assert.Equal(90, detail.Burndown[0].Ideal);
        Assert.Equal(30, detail.Burndown.Single(p => p.Date == D(today)).Remaining);
        Assert.Null(detail.Burndown.Last().Remaining);

        Assert.Equal(["Backlog"], plan.Backlog("default", Actor.User).Select(t => t.Title));
        Assert.Equal(1, plan.CarryOver(sprint.Id, next.Id, Actor.User));
        Assert.Equal(["B"], plan.SprintDetail(next.Id, Actor.User).Tasks.Select(t => t.Title));
        Assert.Equal("planned", plan.Sprints("default", Actor.User).Single(s => s.Id == next.Id).State);
    }

    [Fact]
    public void Goals_roll_up_projects_and_subgoals()
    {
        using var v = new TestVault();
        var goal = v.Writes.Create(new CreateNodeRequest { Title = "Grow revenue", Type = PlanningService.Goal }, Actor.User);
        var sub = v.Writes.Create(new CreateNodeRequest { Title = "New market", Type = PlanningService.Goal, Properties = new Dictionary<string, object?> { ["parent"] = $"[[{goal.Id}|Grow revenue]]" } }, Actor.User);
        var project = v.Writes.Create(new CreateNodeRequest { Title = "Launch", Type = NodeTypes.Project, Properties = new Dictionary<string, object?> { ["goal"] = $"[[{sub.Id}|New market]]" } }, Actor.User);
        var t1 = v.Session.Tasks.Create(new CreateTaskRequest { Title = "One", Project = $"[[{project.Id}|Launch]]" }, Actor.User);
        v.Session.Tasks.Create(new CreateTaskRequest { Title = "Two", Project = $"[[{project.Id}|Launch]]" }, Actor.User);
        v.Session.Tasks.Create(new CreateTaskRequest { Title = "Three", Project = $"[[{goal.Id}|Grow revenue]]", Due = D(v.Writes.Clock.Today.AddDays(-1)) }, Actor.User);
        v.Session.Tasks.Update(t1.Id, new Dictionary<string, object?> { ["status"] = "done" }, [], Actor.User);

        var goals = v.Session.Planning.Goals("default", Actor.User);
        var top = goals.Single(g => g.Id == goal.Id);
        Assert.Equal((3, 1, 1), (top.Tasks, top.Done, top.Overdue));
        Assert.Equal([sub.Id], top.Children);
        Assert.Equal("at risk", top.Health);
        Assert.Equal(0.5, goals.Single(g => g.Id == project.Id).Progress);

        var stats = v.Session.Planning.Analytics("default", 4, Actor.User);
        Assert.Equal(2, stats.Open);
        Assert.Equal(1, stats.Overdue);
        Assert.Equal(1, stats.Weeks[^1].Completed);
        Assert.Equal(3, stats.Weeks[^1].Created);
    }
}
