using Quantos.Core;
using Quantos.Core.Security;
using Quantos.Core.Tasks;
using TaskStatus = Quantos.Core.Tasks.TaskStatus;

namespace Quantos.Tests;

/// <summary>BUILD-PLAN section 13, "Tasks", and the inline-task rules in sections 9 and 13.</summary>
public class TaskAcceptanceTests
{
    private static TaskService T(TestVault v) => v.Session.Tasks;

    [Fact] // 13 tasks.1
    public void Recurring_occurrences_are_separate_and_never_duplicated()
    {
        using var v = new TestVault();
        var node = T(v).Create(new CreateTaskRequest { Title = "Weekly review", Planned = "2026-10-12", Due = "2026-10-13", Recurrence = "weekly" }, Actor.User);
        T(v).SetStatus(node.Id, TaskStatus.Done, Actor.User);
        var all = T(v).List();
        Assert.Equal(2, all.Count);
        var next = all.Single(t => t.Id != node.Id);
        Assert.Equal("2026-10-19", next.Occurrence);
        Assert.Equal("2026-10-19", next.Planned);
        Assert.Equal("2026-10-20", next.Due);
        Assert.Equal(TaskStatus.Todo, next.Status);
        Assert.Equal(all.Single(t => t.Id == node.Id).Series, next.Series);
        // Reopening and completing again doesn't create a second copy of the same occurrence.
        T(v).SetStatus(node.Id, TaskStatus.Todo, Actor.User);
        T(v).SetStatus(node.Id, TaskStatus.Done, Actor.User);
        Assert.Null(T(v).GenerateNext(T(v).Require(node.Id), Actor.User));
        Assert.Equal(2, T(v).List().Count);
        // Each occurrence keeps its own completion history.
        Assert.NotNull(T(v).Require(node.Id).Completed);
        Assert.Null(T(v).Require(next.Id).Completed);
    }

    [Fact]
    public void Next_occurrence_comes_from_the_scheduled_date_not_completion()
    {
        var weekdays = Recurrence.Parse("weekdays")!;
        Assert.Equal(new DateOnly(2026, 10, 19), weekdays.Next(new DateOnly(2026, 10, 16))); // Fri -> Mon
        Assert.Equal(new DateOnly(2026, 11, 30), Recurrence.Parse("FREQ=MONTHLY")!.Next(new DateOnly(2026, 10, 31)));
        Assert.Equal(new DateOnly(2026, 10, 29), Recurrence.Parse("FREQ=DAILY;INTERVAL=14")!.Next(new DateOnly(2026, 10, 15)));
    }

    [Fact] // Monthly series on the 31st don't drift to the 28th after February
    public void Monthly_and_yearly_series_keep_their_day()
    {
        Assert.Equal(new DateOnly(2027, 3, 31), Recurrence.Parse("monthly")!.Next(new DateOnly(2027, 2, 28), anchorDay: 31));
        Assert.Equal(new DateOnly(2032, 2, 29), Recurrence.Parse("yearly")!.Next(new DateOnly(2031, 2, 28), anchorDay: 29));

        using var v = new TestVault();
        var task = T(v).Create(new CreateTaskRequest { Title = "Pay rent", Due = "2027-01-31", Recurrence = "monthly" }, Actor.User);
        var dues = new List<string?>();
        for (var i = 0; i < 3; i++)
        {
            T(v).SetStatus(task.Id, TaskStatus.Done, Actor.User);
            task = v.Writes.Require(T(v).List().Single(t => t.Status == TaskStatus.Todo).Id);
            dues.Add(T(v).Require(task.Id).Due);
        }
        Assert.Equal(["2027-02-28", "2027-03-31", "2027-04-30"], dues);
    }
    [Fact] // 13 tasks.2
    public void Complete_snooze_trash_and_restore_a_task()
    {
        using var v = new TestVault();
        var node = T(v).Create(new CreateTaskRequest { Title = "Call Sam", Reminders = ["2026-01-01T09:00:00+00:00"] }, Actor.User);
        Assert.Equal(1, v.Session.Reminders.Tick());
        var snoozed = T(v).Snooze(node.Id, TimeSpan.FromMinutes(10), Actor.User);
        Assert.NotNull(snoozed.Reminders[0].SnoozedUntil);
        Assert.Null(snoozed.Reminders[0].Fired);
        Assert.Equal(TaskStatus.Done, T(v).SetStatus(node.Id, TaskStatus.Done, Actor.User).Status);
        var entry = v.Writes.Trash(node.Id, Actor.User);
        Assert.Null(T(v).Get(node.Id));
        v.Writes.Restore(entry.EntryId, Actor.User);
        Assert.Equal(TaskStatus.Done, T(v).Require(node.Id).Status);
    }

    [Fact] // 13 tasks.3
    public void Rollover_changes_planned_dates_but_not_due_dates()
    {
        using var v = new TestVault();
        var node = T(v).Create(new CreateTaskRequest { Title = "Draft memo", Planned = "2026-10-01", Due = "2026-10-05" }, Actor.User);
        Assert.Equal(1, T(v).Rollover("default", new DateOnly(2026, 10, 15), Actor.User));
        var t = T(v).Require(node.Id);
        Assert.Equal("2026-10-15", t.Planned);
        Assert.Equal("2026-10-05", t.Due);
        Assert.Equal(1, t.RolledOver);
    }

    [Fact] // 13 tasks.4
    public void Dependencies_block_and_subtasks_roll_up()
    {
        using var v = new TestVault();
        var a = T(v).Create(new CreateTaskRequest { Title = "Collect data", Effort = 30 }, Actor.User);
        var b = T(v).Create(new CreateTaskRequest { Title = "Write report", Effort = 60, DependsOn = [a.Id] }, Actor.User);
        var sub = T(v).Create(new CreateTaskRequest { Title = "Outline", Effort = 15, Parent = b.Id, Due = "2026-11-01", Assignees = ["Sam"] }, Actor.User);
        Assert.True(T(v).Require(b.Id).Blocked);
        Assert.Equal(75, T(v).Require(b.Id).EffortRollup);
        Assert.Equal(1, T(v).Require(b.Id).SubtaskCount);
        Assert.Equal("2026-11-01", T(v).Require(sub.Id).Due);
        Assert.Throws<QuantosException>(() => T(v).Update(a.Id, new Dictionary<string, object?> { ["depends_on"] = new List<object?> { b.Id } }, [], Actor.User));
        Assert.Throws<QuantosException>(() => T(v).Update(b.Id, new Dictionary<string, object?> { ["parent"] = sub.Id }, [], Actor.User));
        T(v).SetStatus(a.Id, TaskStatus.Done, Actor.User);
        Assert.False(T(v).Require(b.Id).Blocked);
    }

    [Fact] // 13 tasks.5
    public void Reminders_fire_once_even_with_several_clients_or_a_restart()
    {
        using var v = new TestVault();
        T(v).Create(new CreateTaskRequest { Title = "Pay invoice", Reminders = ["2026-01-01T09:00:00+00:00"] }, Actor.User);
        Assert.Equal(1, v.Session.Reminders.Tick());
        Assert.Equal(0, v.Session.Reminders.Tick());
        v.Restart();
        Assert.Equal(0, v.Session.Reminders.Tick());
        Assert.Single(v.Session.Notifications.List());
    }

    [Fact]
    public void Inline_checkboxes_become_tasks_and_sync_both_ways()
    {
        using var v = new TestVault();
        var note = v.Writes.Create(new CreateNodeRequest { Title = "Plan", Body = "- [ ] Buy milk\n- [ ] \n" }, Actor.User);
        var task = Assert.Single(T(v).List());
        Assert.Equal("Buy milk", task.Title);
        Assert.Equal(note.Id, task.Source);
        Assert.Contains($"- [ ] Buy milk <!--task:{task.Id}-->", note.Body);

        // Checking the box in the note completes the task.
        var current = v.Writes.Require(note.Id);
        v.Writes.Save(note.Id, current.Content.Replace("- [ ] Buy milk", "- [x] Buy milk"), current.Hash, Actor.User);
        Assert.Equal(TaskStatus.Done, T(v).Require(task.Id).Status);

        // Reopening the task elsewhere updates the note.
        T(v).SetStatus(task.Id, TaskStatus.Todo, Actor.User);
        Assert.Contains("- [ ] Buy milk <!--task:", v.Writes.Require(note.Id).Body);

        // Editing the line in its source note renames the task.
        current = v.Writes.Require(note.Id);
        v.Writes.Save(note.Id, current.Content.Replace("Buy milk", "Buy oat milk"), current.Hash, Actor.User);
        Assert.Equal("Buy oat milk", T(v).Require(task.Id).Title);
    }

    [Fact] // Same tokens as the quick-add box (frontend/tests/logic.test.ts)
    public void Quick_add_tokens_parse_like_the_frontend()
    {
        var today = new DateOnly(2026, 10, 15); // a Thursday
        var q = QuickAdd.Parse("Call Sam due:fri !2 ~1h #client +apollo-launch every week @tomorrow", today);
        Assert.Equal("Call Sam", q.Title);
        Assert.Equal(2, q.Priority);
        Assert.Equal("2026-10-16", q.Due);
        Assert.Equal("2026-10-16", q.Planned);
        Assert.Equal(60, q.Effort);
        Assert.Equal(["client"], q.Tags);
        Assert.Equal("apollo launch", q.Project);
        Assert.Equal("weekly", q.Recurrence);
        Assert.Equal("2026-10-18", QuickAdd.ParseDate("+3d", today));
        Assert.Equal("2026-10-29", QuickAdd.ParseDate("+2w", today));
        Assert.Equal("2026-10-22", QuickAdd.ParseDate("thu", today));
        Assert.Equal("2027-01-01", QuickAdd.ParseDate("1/1", today));
        Assert.Null(QuickAdd.ParseDate("31/2", today));
        Assert.Null(QuickAdd.ParseDate("banana", today));
        Assert.Equal("Fix bug due:never", QuickAdd.Parse("Fix bug #12a due:never", today).Title);
        Assert.Equal("FREQ=WEEKLY;INTERVAL=2", QuickAdd.Parse("Payroll every 2 weeks", today).Recurrence);
    }

    [Fact]
    public void Inline_task_lines_understand_quick_add_tokens()
    {
        using var v = new TestVault();
        var project = v.Writes.Create(new CreateNodeRequest { Title = "Apollo launch" }, Actor.User);
        var note = v.Writes.Create(new CreateNodeRequest { Title = "Plan", Body = "- [ ] Call Sam due:tomorrow !1 #client +apollo-launch\n" }, Actor.User);
        var task = Assert.Single(T(v).List());
        Assert.Equal("Call Sam", task.Title);
        Assert.Equal(v.Writes.Clock.Today.AddDays(1).ToString("yyyy-MM-dd"), task.Due);
        Assert.Equal(1, task.Priority);
        Assert.Contains("client", task.Tags);
        Assert.Equal($"[[{project.Id}|Apollo launch]]", task.Project);
        // The note keeps the words; the details live on the task. A later save doesn't rename it back.
        Assert.Contains($"- [ ] Call Sam <!--task:{task.Id}-->", note.Body);
        var current = v.Writes.Require(note.Id);
        v.Writes.Save(note.Id, current.Content + "\nMore text.\n", current.Hash, Actor.User);
        Assert.Equal("Call Sam", T(v).Require(task.Id).Title);

        // Tokens added to the line later update the task, and the line is tidied again.
        current = v.Writes.Require(note.Id);
        v.Writes.Save(note.Id, current.Content.Replace("- [ ] Call Sam <!--", "- [ ] Call Sam back due:2026-12-24 !3 #urgent <!--"), current.Hash, Actor.User);
        var updated = T(v).Require(task.Id);
        Assert.Equal("Call Sam back", updated.Title);
        Assert.Equal("2026-12-24", updated.Due);
        Assert.Equal(3, updated.Priority);
        Assert.Equal(["client", "urgent"], updated.Tags);
        Assert.Contains($"- [ ] Call Sam back <!--task:{task.Id}-->", v.Writes.Require(note.Id).Body);

        // Typing after the hidden marker (cursor at the end of the line) extends the same task.
        current = v.Writes.Require(note.Id);
        v.Writes.Save(note.Id, current.Content.Replace($"<!--task:{task.Id}-->", $"<!--task:{task.Id}--> today"), current.Hash, Actor.User);
        Assert.Single(T(v).List());
        Assert.Equal("Call Sam back today", T(v).Require(task.Id).Title);
        Assert.Contains($"- [ ] Call Sam back today <!--task:{task.Id}-->", v.Writes.Require(note.Id).Body);
    }

    [Fact]
    public void Capturing_a_task_understands_quick_add_tokens()
    {
        using var v = new TestVault();
        var node = v.Session.Capture.Capture("Renew passport due:2026-12-01 !2", "default", CaptureTarget.Task, Actor.User);
        var task = T(v).Require(node.Id);
        Assert.Equal("Renew passport", task.Title);
        Assert.Equal("2026-12-01", task.Due);
        Assert.Equal(2, task.Priority);
    }
    [Fact] // 13 editing.4-5
    public void Moving_or_copying_task_lines_never_deletes_or_duplicates_tasks()
    {
        using var v = new TestVault();
        var a = v.Writes.Create(new CreateNodeRequest { Title = "A", Body = "- [ ] Ship it\n" }, Actor.User);
        var task = Assert.Single(T(v).List());
        var line = v.Writes.Require(a.Id).Body.Trim();

        // Move the line from A to B.
        var b = v.Writes.Create(new CreateNodeRequest { Title = "B", Body = "Moved:\n" + line + "\n" }, Actor.User);
        var currentA = v.Writes.Require(a.Id);
        v.Writes.Save(a.Id, currentA.Content.Replace(line, ""), currentA.Hash, Actor.User);
        Assert.NotNull(T(v).Get(task.Id));

        // Copy it into a third note: still one task, referenced twice.
        v.Writes.Create(new CreateNodeRequest { Title = "C", Body = line + "\n" }, Actor.User);
        Assert.Single(T(v).List());
        Assert.Equal(2, v.Session.Cache.InlineTaskRefs(task.Id).Count);

        // Completing it ticks every reference.
        T(v).SetStatus(task.Id, TaskStatus.Done, Actor.User);
        Assert.Contains("- [x] Ship it", v.Writes.Require(b.Id).Body);
        Assert.All(v.Session.Cache.InlineTaskRefs(task.Id), r => Assert.True(r.Checked));
    }

    [Fact] // 13 editing.2
    public void Capture_appends_to_todays_note_without_overwriting_it()
    {
        using var v = new TestVault();
        var daily = v.Writes.GetOrCreateDaily("default", v.Session.Clock.Today, Actor.User);
        var opened = v.Writes.Require(daily.Id);
        v.Session.Capture.Capture("Idea from the corridor", "default", CaptureTarget.Daily, Actor.User);
        var after = v.Writes.Require(daily.Id);
        Assert.Contains(CaptureService.Heading, after.Body);
        Assert.Contains("Idea from the corridor", after.Body);
        // An editor holding the old version gets a conflict, never a silent overwrite.
        Assert.Equal(SaveStatus.Conflict, v.Writes.Save(daily.Id, opened.Content + "typed meanwhile\n", opened.Hash, Actor.User).Status);
        var task = v.Session.Capture.Capture("Email Pat", "default", CaptureTarget.Task, Actor.User);
        Assert.Equal(NodeTypes.Task, task.Type);
    }

    [Fact] // 13 editing.3 (backend half)
    public void The_watcher_picks_up_outside_edits_and_keeps_both_versions()
    {
        using var v = new TestVault();
        v.Session.StartWatching();
        var node = v.Writes.Create(new CreateNodeRequest { Title = "Watched", Body = "app text" }, Actor.User);
        var events = new List<ChangeEvent>();
        v.Session.Changes.Changed += events.Add;
        File.WriteAllText(v.Abs(node.Path), node.Content.Replace("app text", "outside text"));
        for (var i = 0; i < 50 && !events.Any(e => e.Origin == "external"); i++) Thread.Sleep(100);
        Assert.Contains(events, e => e.Kind == ChangeKind.Updated && e.Id == node.Id && e.Origin == "external");
        Assert.Contains("outside text", v.Writes.Require(node.Id).Body);
        var versions = v.Writes.History.List(node.Id).Select(x => v.Writes.History.Read(node.Id, x.Version)!).ToList();
        Assert.Contains(versions, c => c.Contains("app text"));
        Assert.Contains(versions, c => c.Contains("outside text"));
    }

    [Fact]
    public void Calendar_export_contains_dated_tasks()
    {
        using var v = new TestVault();
        T(v).Create(new CreateTaskRequest { Title = "Board meeting, prep", Due = "2026-10-20" }, Actor.User);
        var emoji = T(v).Create(new CreateTaskRequest { Title = "Plan " + string.Concat(Enumerable.Repeat("🎉", 40)), Due = "2026-10-21" }, Actor.User);
        var done = T(v).Create(new CreateTaskRequest { Title = "Already finished", Due = "2026-10-22" }, Actor.User);
        T(v).SetStatus(done.Id, TaskStatus.Done, Actor.User);
        var ics = Calendar.Build(T(v).List(includeClosed: true), "Test");
        // Events, not to-dos: Outlook and Google Calendar ignore VTODO in imported files.
        Assert.DoesNotContain("VTODO", ics);
        Assert.Contains("DTSTART;VALUE=DATE:20261020\r\nDTEND;VALUE=DATE:20261021", ics);
        Assert.Contains("SUMMARY:Board meeting\\, prep", ics);
        Assert.Contains("TRANSP:TRANSPARENT", ics);
        Assert.DoesNotContain("Already finished", ics);
        // Folded lines stay within 75 octets and never split a character.
        foreach (var line in ics.Split("\r\n")) Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) <= 75, line);
        Assert.DoesNotContain('\uFFFD', System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(ics)));
        Assert.Contains(string.Concat(Enumerable.Repeat("🎉", 40)), ics.Replace("\r\n ", ""));
        Assert.Contains(emoji.Id, ics);
    }
}
