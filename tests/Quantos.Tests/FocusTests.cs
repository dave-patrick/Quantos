using Quantos.Core;
using Quantos.Core.Security;
using Quantos.Core.Tasks;

namespace Quantos.Tests;

/// <summary>Release 1.1 focus sessions: written to the daily note and the task, read back as totals.</summary>
public class FocusTests
{
    [Fact]
    public void A_session_lands_in_the_daily_note_and_on_the_task()
    {
        using var v = new TestVault();
        var task = v.Session.Tasks.Create(new CreateTaskRequest { Title = "Write report" }, Actor.User);
        var now = v.Writes.Clock.Now;
        var start = now.AddMinutes(-30);
        v.Session.Focus.Log(task.Workspace, start, start.AddMinutes(25), task.Id, null, Actor.User);
        v.Session.Focus.Log(task.Workspace, now.AddMinutes(-4), now, null, "Inbox  zero", Actor.User);

        var daily = v.Writes.GetOrCreateDaily(task.Workspace, DateOnly.FromDateTime(start.DateTime), Actor.User);
        Assert.Contains(FocusService.Heading, daily.Body);
        Assert.Contains($"· 25 min · [[{task.Id}|Write report]]", daily.Body);
        Assert.Contains("· 4 min · Inbox zero", daily.Body);
        Assert.Equal("25", Convert.ToString(v.Writes.Require(task.Id).Properties[FocusService.TimeSpent]));

        var summary = v.Session.Focus.Summary(task.Workspace, 7, Actor.User);
        Assert.Equal(7, summary.Days.Count);
        Assert.Equal(29, summary.Minutes);
        var top = summary.ByTask[0];
        Assert.Equal((task.Id, "Write report", 25), (top.TaskId, top.Label, top.Minutes));
    }

    [Fact]
    public void Nonsense_sessions_are_refused()
    {
        using var v = new TestVault();
        var now = v.Writes.Clock.Now;
        Assert.Throws<QuantosException>(() => v.Session.Focus.Log("default", now, now.AddSeconds(20), null, null, Actor.User));
        Assert.Throws<QuantosException>(() => v.Session.Focus.Log("default", now, now.AddMinutes(-5), null, null, Actor.User));
    }
}
