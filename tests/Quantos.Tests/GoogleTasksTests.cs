using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Quantos.Core;
using Quantos.Core.GoogleTasks;
using Quantos.Core.Security;
using Quantos.Core.Tasks;
using TaskStatus = Quantos.Core.Tasks.TaskStatus;

namespace Quantos.Tests;

/// <summary>An in-memory Google Tasks with its own clock, so tests can edit "on Google" and count calls.</summary>
internal sealed class FakeGoogle : IGoogleTasksApi
{
    public Dictionary<string, List<GTask>> Lists { get; } = new() { ["L1"] = [] };
    public List<string> Calls { get; } = [];
    public Exception? FailLists;
    public Exception? FailGet;
    public Exception? FailListTasks;
    public int? FailDeleteNumber;
    public Exception? FailDelete;
    public volatile bool BlockListTasks;
    public Exception? FailPatch, FailMove;
    private int _deletes;
    private int _tick;
    private int _id;

    private string Stamp() => new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero).AddSeconds(++_tick).ToString("o", CultureInfo.InvariantCulture);
    /// <summary>Moves Google's clock forward, so later changes are further apart than the fetch overlap.</summary>
    public void Skip(TimeSpan by) => _tick += (int)by.TotalSeconds;
    public int Mutations => Calls.Count(c => c.StartsWith("Insert") || c.StartsWith("Patch") || c.StartsWith("Move") || c.StartsWith("Delete"));
    public GTask Find(string id, string list = "L1") => Lists[list].Single(t => t.Id == id);
    public GTask ByTitle(string title, string list = "L1") => Lists[list].Single(t => t.Title == title && !t.Deleted);

    /// <summary>A change made on Google by the person.</summary>
    public GTask Edit(string id, Func<GTask, GTask> change, string list = "L1")
    {
        var i = Lists[list].FindIndex(t => t.Id == id);
        Lists[list][i] = change(Lists[list][i]) with { Updated = Stamp() };
        return Lists[list][i];
    }

    public GTask Add(string title, string? due = null, string? notes = null, bool completed = false, string? parent = null, string list = "L1")
    {
        var t = new GTask("g" + ++_id, title, notes, due, completed, completed ? "2026-10-29T10:00:00.000Z" : null, parent, Stamp(), "e", false, false, false);
        Lists[list].Add(t);
        return t;
    }

    public Task<IReadOnlyList<GTaskList>> ListTaskLists(CancellationToken ct) => FailLists is { } boom ? Task.FromException<IReadOnlyList<GTaskList>>(boom) : Task.FromResult<IReadOnlyList<GTaskList>>(Lists.Keys.Select(k => new GTaskList(k, k)).ToList());
    public Task<GTaskList> CreateTaskList(string title, CancellationToken ct) { Lists[title] = []; return Task.FromResult(new GTaskList(title, title)); }

    public async Task<GTaskPage> ListTasks(string listId, DateTimeOffset? updatedMin, string? pageToken, CancellationToken ct)
    {
        Calls.Add("ListTasks");
        if (FailListTasks is { } listFailure) throw listFailure;
        if (BlockListTasks) await Task.Delay(Timeout.Infinite, ct); // until the caller cancels
        if (!Lists.TryGetValue(listId, out var all)) throw new GoogleApiException(404, "notFound", "Not found");
        var items = all.Where(t => updatedMin is null || DateTimeOffset.Parse(t.Updated, CultureInfo.InvariantCulture) > updatedMin).ToList();
        var skip = pageToken is null ? 0 : int.Parse(pageToken);
        var page = items.Skip(skip).Take(100).ToList();
        return new GTaskPage(page, skip + 100 < items.Count ? (skip + 100).ToString() : null);
    }

    public Task<GTask?> Get(string listId, string id, CancellationToken ct)
    {
        Calls.Add("Get " + id);
        if (FailGet is { } getFailure) throw getFailure;
        return Task.FromResult(Lists[listId].FirstOrDefault(t => t.Id == id));
    }

    public Task<GTask> Insert(string listId, GTaskWrite w, string? parent, CancellationToken ct)
    {
        Calls.Add("Insert " + w.Title.Value);
        var t = Add(w.Title.Value ?? "", w.Due.HasValue ? w.Due.Value : null, w.Notes.HasValue ? w.Notes.Value : null, w.Completed.HasValue && w.Completed.Value, parent, listId);
        return Task.FromResult(t);
    }

    public Task<GTask> Patch(string listId, string id, GTaskWrite w, CancellationToken ct)
    {
        Calls.Add("Patch " + id);
        if (FailPatch is { } patchFailure) throw patchFailure;
        return Task.FromResult(Edit(id, t =>
        {
            if (w.Title.HasValue) t = t with { Title = w.Title.Value ?? "" };
            if (w.Notes.HasValue) t = t with { Notes = w.Notes.Value };
            if (w.Due.HasValue) t = t with { Due = w.Due.Value };
            if (w.Completed.HasValue) t = t with { Completed = w.Completed.Value, CompletedAt = w.Completed.Value ? "2026-10-30T11:00:00.000Z" : null };
            return t;
        }, listId));
    }

    public Task<GTask> Move(string listId, string id, string? parent, CancellationToken ct)
    {
        Calls.Add("Move " + id);
        if (FailMove is { } moveFailure) throw moveFailure;
        return Task.FromResult(Edit(id, t => t with { Parent = parent }, listId));
    }

    public Task Delete(string listId, string id, CancellationToken ct)
    {
        Calls.Add("Delete " + id);
        if (FailDelete is { } deleteFailure) throw deleteFailure;
        if (++_deletes == FailDeleteNumber) throw new GoogleApiException(503, "network", "down");
        Edit(id, t => t with { Deleted = true }, listId);
        return Task.CompletedTask;
    }
}

/// <summary>Round-trips the state through JSON on every save, like the file store, and can fail a chosen save.</summary>
internal sealed class MemoryStateStore : IGoogleTasksStateStore
{
    private string _json = JsonSerializer.Serialize(new GoogleTasksState(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public int Saves { get; private set; }
    public int FailOnSave { get; set; } = -1;
    public GoogleTasksState Load() => JsonSerializer.Deserialize<GoogleTasksState>(_json, Json)!;
    public void Save(GoogleTasksState state)
    {
        if (++Saves == FailOnSave) throw new IOException("simulated crash");
        _json = JsonSerializer.Serialize(state, Json);
    }
    public void Delete() => _json = JsonSerializer.Serialize(new GoogleTasksState(), Json);
}

public class GoogleTasksTests : IDisposable
{
    private readonly TestVault _v = new();
    private readonly FakeGoogle _g = new();
    private readonly MemoryStateStore _state = new();
    private static readonly GoogleListMapping Map = new("default", "L1", "Mine", DateTimeOffset.UtcNow.AddDays(-30), true);

    public void Dispose() => _v.Dispose();

    private TaskService T => _v.Session.Tasks;
    private GoogleTasksSyncEngine Engine() => new(_v.Writes, T, _v.Session.Clock, _state, _g);
    private Task<GoogleSyncReport> Sync(GoogleTasksSyncEngine? e = null) => (e ?? Engine()).Run([Map], CancellationToken.None);
    private string Add(string title, string? due = null, string? body = null, string? parent = null) =>
        T.Create(new CreateTaskRequest { Title = title, Due = due, Body = body ?? "", Parent = parent }, Actor.User).Id;
    private TaskDto Task_(string id) => T.Require(id);
    private Node Node_(string id) => _v.Writes.Get(id)!;

    // ---- mapper -------------------------------------------------------------------------------

    [Theory]
    [InlineData("  Buy\r\nmilk  ", "Buy milk")]
    [InlineData("See [[Plan]] <!--x-->", "See [Plan] x")]
    [InlineData("", "(untitled)")]
    public void Titles_are_normalised(string input, string expected) => Assert.Equal(expected, GoogleTasksMapper.NormTitle(input));

    [Fact]
    public void Notes_links_become_text_and_unsupported_forms_stop_notes_sync()
    {
        Assert.Equal("see Plan and Alias", GoogleTasksMapper.NotesFromBody("see [[01JABCDEFGHJKMNPQRSTVWXYZ0|Plan]] and [[Other|Alias]]").Text);
        Assert.Equal("a\nb", GoogleTasksMapper.NotesFromBody("a\r\nb\r\n\r\n").Text);
        Assert.False(GoogleTasksMapper.NotesFromBody("embed ![[pic]]").Eligible);
        Assert.False(GoogleTasksMapper.NotesFromBody("see [[Note#Heading]]").Eligible);
        Assert.False(GoogleTasksMapper.NotesFromBody(new string('x', 8001)).Eligible);
        Assert.True(GoogleTasksMapper.NotesFromBody(new string('x', 8000)).Eligible);
    }

    [Fact]
    public void Clock_format_matches_stamp_in_the_device_zone()
    {
        var c = _v.Session.Clock;
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d[+-]\d\d:\d\d$", c.Format(DateTimeOffset.Parse("2026-10-30T10:00:00Z", CultureInfo.InvariantCulture)));
        Assert.Equal(DateTimeOffset.Parse("2026-10-30T10:00:00Z", CultureInfo.InvariantCulture), DateTimeOffset.Parse(c.Format(DateTimeOffset.Parse("2026-10-30T10:00:00Z", CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture));
    }

    // ---- push, import and steady state ----------------------------------------------------------

    [Fact]
    public async Task A_new_local_task_is_pushed_with_hints_and_the_next_cycle_writes_nothing()
    {
        var id = Add("Buy milk", "2026-11-02", "two litres");
        var report = await Sync();
        var google = _g.ByTitle("Buy milk");
        Assert.Equal("2026-11-02", google.Due);
        Assert.Equal("two litres", google.Notes);
        Assert.Equal(google.Id, Node_(id).Properties["gtask_id"]);
        Assert.Equal("L1", Node_(id).Properties["gtask_list"]);
        Assert.Equal(1, report.Pushed);
        var mutations = _g.Mutations;
        var modified = Node_(id).Hash;
        var again = await Sync();
        Assert.Equal(mutations, _g.Mutations);
        Assert.Equal(modified, Node_(id).Hash);
        Assert.Equal(0, again.Pushed + again.Pulled);
    }

    [Fact]
    public async Task A_google_task_is_imported_with_its_state_and_not_imported_twice()
    {
        _g.Add("From phone", "2026-11-05", "n");
        _g.Add("Old done", completed: true);
        await Sync();
        var t = T.List().Single(x => x.Title == "From phone");
        Assert.Equal("2026-11-05", t.Due);
        Assert.Equal("n", Node_(t.Id).Body.Trim());
        Assert.Equal(TaskStatus.Done, T.List().Single(x => x.Title == "Old done").Status);
        await Sync();
        Assert.Equal(2, T.List().Count);
    }

    [Fact]
    public async Task Recurring_archived_and_readonly_tasks_are_not_pushed()
    {
        T.Create(new CreateTaskRequest { Title = "Weekly", Recurrence = "weekly", Due = "2026-11-02" }, Actor.User);
        var archived = Add("Archived");
        _v.Writes.UpdateFields(archived, null, new Dictionary<string, object?> { ["archived"] = true }, null, Actor.User, allowReserved: true);
        await Sync();
        Assert.Empty(_g.Lists["L1"]);
    }

    // ---- merge ----------------------------------------------------------------------------------

    [Fact]
    public async Task Edits_on_each_side_travel_and_a_multi_field_pull_has_no_conflict()
    {
        var id = Add("Plan trip", "2026-11-02", "a");
        await Sync();
        var gid = _g.ByTitle("Plan trip").Id;
        _g.Edit(gid, t => t with { Title = "Plan the trip", Notes = "b", Due = "2026-11-09", Completed = true, CompletedAt = "2026-10-30T09:00:00.000Z" });
        var report = await Sync();
        var t = Task_(id);
        Assert.Equal("Plan the trip", t.Title);
        Assert.Equal("2026-11-09", t.Due);
        Assert.Equal(TaskStatus.Done, t.Status);
        Assert.Equal("b", Node_(id).Body.Trim());
        Assert.Equal(0, report.Skipped);
        // and a local edit goes back
        T.Update(id, new Dictionary<string, object?> { ["due"] = "2026-12-01" }, [], Actor.User);
        await Sync();
        Assert.Equal("2026-12-01", _g.Find(gid).Due);
        var steady = _g.Mutations;
        await Sync();
        Assert.Equal(steady, _g.Mutations);
    }

    [Fact]
    public async Task When_both_sides_change_a_field_the_local_value_wins()
    {
        var id = Add("Report", "2026-11-02");
        await Sync();
        var gid = _g.ByTitle("Report").Id;
        T.Update(id, new Dictionary<string, object?> { ["due"] = "2026-11-20" }, [], Actor.User);
        _g.Edit(gid, t => t with { Due = "2026-11-25" });
        var report = await Sync();
        Assert.Equal("2026-11-20", _g.Find(gid).Due);
        Assert.Equal("2026-11-20", Task_(id).Due);
        Assert.Equal(1, report.Conflicts);
    }

    [Fact]
    public async Task A_local_due_time_survives_and_cancelled_pushes_as_completed_and_reopens_to_todo()
    {
        var id = Add("Call", "2026-11-02T15:30:00-07:00");
        await Sync();
        var gid = _g.ByTitle("Call").Id;
        Assert.Equal("2026-11-02", _g.Find(gid).Due);
        await Sync();
        Assert.Equal("2026-11-02T15:30:00-07:00", Task_(id).Due);
        T.SetStatus(id, TaskStatus.Cancelled, Actor.User);
        await Sync();
        Assert.True(_g.Find(gid).Completed);
        _g.Edit(gid, t => t with { Completed = false, CompletedAt = null });
        await Sync();
        Assert.Equal(TaskStatus.Todo, Task_(id).Status);
    }

    [Fact]
    public async Task A_locked_task_is_not_edited_by_google_but_is_still_pushed()
    {
        var id = Add("Locked task", "2026-11-02");
        _v.Writes.UpdateFields(id, null, new Dictionary<string, object?> { ["locked"] = true }, null, Actor.User, allowReserved: true);
        await Sync();
        var gid = _g.ByTitle("Locked task").Id;
        Assert.Equal(gid, Node_(id).Properties["gtask_id"]);
        _g.Edit(gid, t => t with { Due = "2026-12-12" });
        var report = await Sync();
        Assert.Equal("2026-11-02", Task_(id).Due);
        Assert.True(report.Skipped >= 1);
    }

    [Fact]
    public async Task A_google_edit_that_could_not_be_applied_is_fetched_again_and_applied_once_it_can()
    {
        var id = Add("Held back", "2026-11-02");
        _v.Writes.UpdateFields(id, null, new Dictionary<string, object?> { ["locked"] = true }, null, Actor.User, allowReserved: true);
        await Sync();
        var gid = _g.ByTitle("Held back").Id;
        _g.Edit(gid, t => t with { Due = "2026-12-12" });
        await Sync(); // locked: skipped, and the cursor must not move past this change
        await Sync();
        Assert.Equal("2026-11-02", Task_(id).Due);
        _v.Writes.UpdateFields(id, null, null, ["locked"], Actor.User, allowReserved: true);
        await Sync();
        Assert.Equal("2026-12-12", Task_(id).Due);
    }

    [Fact]
    public async Task A_google_edit_made_while_a_task_was_suspended_is_pulled_when_it_resumes()
    {
        var id = Add("Before", "2026-11-02");
        await Sync();
        var gid = _g.ByTitle("Before").Id;
        _v.Writes.UpdateFields(id, null, new Dictionary<string, object?> { ["archived"] = true }, null, Actor.User, allowReserved: true);
        await Sync(); // suspended
        _g.Edit(gid, t => t with { Title = "After", Due = "2026-12-01" });
        await Sync(); // still suspended: Google's change is fetched, nothing is applied
        _g.Skip(TimeSpan.FromMinutes(10)); _g.Add("Unrelated newer task");
        await Sync(); // the cursor moves well past the edit, beyond the one-minute fetch overlap
        Assert.Equal("Before", Task_(id).Title);
        _v.Writes.UpdateFields(id, null, null, ["archived"], Actor.User, allowReserved: true);
        await Sync(); // resumes
        Assert.Equal("After", Task_(id).Title);
        Assert.Equal("2026-12-01", Task_(id).Due);
    }

    [Fact]
    public async Task A_failed_attempt_to_resume_a_suspended_task_is_retried_and_the_google_edit_is_still_pulled()
    {
        var id = Add("Before", "2026-11-02");
        await Sync();
        var gid = _g.ByTitle("Before").Id;
        _v.Writes.UpdateFields(id, null, new Dictionary<string, object?> { ["archived"] = true }, null, Actor.User, allowReserved: true);
        await Sync();
        _g.Edit(gid, t => t with { Title = "After" });
        await Sync();
        _g.Skip(TimeSpan.FromMinutes(10)); _g.Add("Unrelated newer task");
        await Sync(); // the cursor moves well past the edit, beyond the one-minute fetch overlap
        _v.Writes.UpdateFields(id, null, null, ["archived"], Actor.User, allowReserved: true);
        _g.FailGet = new GoogleApiException(503, "network", "down");
        await Assert.ThrowsAsync<GoogleApiException>(() => Sync()); // the resume is interrupted
        Assert.True(_state.Load().Links[id].AskGoogle);              // and the next run will ask Google again
        _g.FailGet = null;
        await Sync();
        Assert.Equal("After", Task_(id).Title);
        Assert.False(_state.Load().Links[id].AskGoogle);
    }

    [Fact]
    public async Task A_pull_held_back_by_a_lock_when_a_suspended_task_resumes_is_applied_once_it_is_unlocked()
    {
        var id = Add("Before", "2026-11-02");
        await Sync();
        var gid = _g.ByTitle("Before").Id;
        _v.Writes.UpdateFields(id, null, new Dictionary<string, object?> { ["archived"] = true }, null, Actor.User, allowReserved: true);
        await Sync();
        _g.Edit(gid, t => t with { Title = "After" });
        _g.Skip(TimeSpan.FromMinutes(10)); _g.Add("Unrelated newer task");
        await Sync(); // the cursor moves well past the edit
        _v.Writes.UpdateFields(id, null, new Dictionary<string, object?> { ["locked"] = true }, ["archived"], Actor.User, allowReserved: true);
        await Sync(); // resumes through Get, but the pull is refused by the lock
        Assert.Equal("Before", Task_(id).Title);
        Assert.True(_state.Load().Links[id].AskGoogle); // still waiting to be resumed
        _v.Writes.UpdateFields(id, null, null, ["locked"], Actor.User, allowReserved: true);
        await Sync();
        Assert.Equal("After", Task_(id).Title);
        Assert.False(_state.Load().Links[id].AskGoogle);
    }

    [Fact]
    public async Task A_task_whose_resume_is_still_pending_is_deleted_in_google_when_it_is_trashed()
    {
        var id = Add("Resume then trash", "2026-11-02");
        await Sync();
        var gid = _g.ByTitle("Resume then trash").Id;
        _v.Writes.UpdateFields(id, null, new Dictionary<string, object?> { ["archived"] = true }, null, Actor.User, allowReserved: true);
        await Sync();
        _g.Skip(TimeSpan.FromMinutes(10)); _g.Add("Unrelated newer task");
        await Sync(); // the cursor moves beyond the one-minute fetch overlap, so the resume has to ask Google
        _v.Writes.UpdateFields(id, null, null, ["archived"], Actor.User, allowReserved: true);
        _g.FailGet = new GoogleApiException(503, "network", "down");
        await Assert.ThrowsAsync<GoogleApiException>(() => Sync()); // the resume fails; the task is eligible again
        _v.Writes.Trash(id, Actor.User);
        _g.FailGet = null;
        await Sync();
        Assert.True(_g.Find(gid).Deleted); // not left behind to come back as a duplicate
    }

    [Fact]
    public void A_long_title_is_never_cut_through_an_emoji()
    {
        var title = new string('a', 1023) + "😀" + "tail";
        var cut = GoogleTasksMapper.Truncate(title);
        Assert.Equal(1023, cut.Length);
        Assert.False(char.IsHighSurrogate(cut[^1]));
        Assert.Equal("{\"title\":\"" + cut + "\"}", GoogleTasksApi.Body(new GTaskWrite { Title = Optional<string?>.Of(cut) }).ToJsonString());
        Assert.Equal(1024, GoogleTasksMapper.Truncate(new string('b', 1500)).Length);
    }

    [Fact]
    public async Task A_long_title_is_cut_for_google_without_looping()
    {
        Add(new string('t', 1500));
        await Sync();
        Assert.Equal(1024, _g.Lists["L1"].Single().Title.Length);
        var m = _g.Mutations;
        await Sync();
        Assert.Equal(m, _g.Mutations);
    }

    [Fact]
    public async Task Notes_with_an_embed_are_not_synced_but_other_fields_are()
    {
        var id = Add("Embedded", "2026-11-02", "see ![[picture]]");
        await Sync();
        var g = _g.ByTitle("Embedded");
        Assert.Null(g.Notes);
        _g.Edit(g.Id, t => t with { Due = "2026-11-03" });
        await Sync();
        Assert.Equal("2026-11-03", Task_(id).Due);
        Assert.Contains("![[picture]]", Node_(id).Body);
    }

    // ---- parent ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_parent_is_pushed_before_its_child_and_only_leaves_get_a_google_parent()
    {
        var parent = Add("Parent");
        var child = Add("Child", parent: parent);
        await Sync();
        Assert.Equal(_g.ByTitle("Parent").Id, _g.ByTitle("Child").Parent);
        Assert.Null(_g.ByTitle("Parent").Parent);
        // Google moves the child to the top level
        _g.Edit(_g.ByTitle("Child").Id, t => t with { Parent = null });
        await Sync();
        Assert.Null(Task_(child).Parent);
    }

    [Fact]
    public async Task A_child_whose_parent_is_added_later_is_moved_under_it()
    {
        var other = Add("Orphan");
        await Sync();
        var parent = Add("New parent");
        T.Update(other, new Dictionary<string, object?> { ["parent"] = parent }, [], Actor.User);
        await Sync(); // the parent is pushed in this cycle...
        Assert.Null(_g.ByTitle("Orphan").Parent);
        await Sync(); // ...and the child is moved under it in the next
        Assert.Equal(_g.ByTitle("New parent").Id, _g.ByTitle("Orphan").Parent);
    }

    // ---- links, hints, crashes ------------------------------------------------------------------

    [Fact]
    public async Task Losing_the_state_relinks_from_hints_without_duplicates()
    {
        var id = Add("Stays one", "2026-11-02");
        await Sync();
        _state.Delete();
        await Sync();
        Assert.Single(_g.Lists["L1"]);
        Assert.Single(T.List());
        Assert.Equal(_g.Lists["L1"][0].Id, Node_(id).Properties["gtask_id"]);
    }

    [Fact]
    public async Task A_crash_after_the_insert_does_not_duplicate_the_task()
    {
        Add("Crash me", "2026-11-02");
        _state.FailOnSave = 3; // 1: intent recovery; 2: the intent; 3: the link after the insert
        await Assert.ThrowsAsync<IOException>(() => Sync());
        _state.FailOnSave = -1;
        Assert.Single(_g.Lists["L1"]);
        await Sync();
        Assert.Single(_g.Lists["L1"]);
        Assert.Single(T.List());
    }

    [Fact]
    public async Task A_crash_after_the_insert_with_no_hint_is_recovered_from_the_intent()
    {
        var id = Add("Intent me", "2026-11-02");
        // simulate: the intent was saved, the insert happened, nothing else did
        var s = _state.Load();
        s.Intents.Add(new GoogleIntent { TaskId = id, ListId = "L1", CursorAtIntent = null, TitleNorm = "Intent me", Due = "2026-11-02" });
        _state.Save(s);
        _g.Add("Intent me", "2026-11-02");
        await Sync();
        Assert.Single(_g.Lists["L1"]);
        Assert.Equal(_g.Lists["L1"][0].Id, Node_(id).Properties["gtask_id"]);
    }

    [Fact]
    public async Task A_hint_that_failed_to_write_is_repaired_on_the_next_cycle()
    {
        var id = Add("Hint repair");
        await Sync();
        _v.Writes.UpdateFields(id, null, null, ["gtask_id", "gtask_list"], Actor.User, derived: true);
        Assert.False(Node_(id).Properties.ContainsKey("gtask_id"));
        await Sync();
        Assert.Equal(_g.Lists["L1"][0].Id, Node_(id).Properties["gtask_id"]);
        Assert.Single(_g.Lists["L1"]);
    }

    [Fact]
    public async Task The_older_of_two_tasks_with_the_same_google_hint_is_kept_and_the_redundant_copy_trashed()
    {
        var original = Add("Same", "2026-11-02");
        await Sync();
        var gid = (string)Node_(original).Properties["gtask_id"]!;
        await Task.Delay(1100); // created stamps have one-second resolution
        var copy = T.Create(new CreateTaskRequest { Title = "Same", Due = "2026-11-02", Extra = new Dictionary<string, object?> { ["gtask_id"] = gid, ["gtask_list"] = "L1" } }, Actor.User).Id;
        await Sync();
        Assert.Null(_v.Writes.Get(copy));
        Assert.NotNull(_v.Writes.Get(original));
        Assert.False(_g.Find(gid).Deleted);
        await Sync();
        Assert.False(_g.Find(gid).Deleted);
    }

    [Fact]
    public void Extra_accepts_only_gtask_keys()
    {
        Assert.Throws<QuantosException>(() => T.Create(new CreateTaskRequest { Title = "x", Extra = new Dictionary<string, object?> { ["status"] = "done" } }, Actor.User));
    }

    // ---- deletes --------------------------------------------------------------------------------

    [Fact]
    public async Task A_google_delete_trashes_a_pristine_task_but_keeps_a_task_with_local_data()
    {
        var plain = Add("Plain");
        var rich = Add("Rich");
        await Sync();
        T.Update(rich, new Dictionary<string, object?> { ["priority"] = 2 }, [], Actor.User);
        await Sync();
        _g.Edit(_g.ByTitle("Plain").Id, t => t with { Deleted = true });
        _g.Edit(_g.ByTitle("Rich").Id, t => t with { Deleted = true });
        await Sync();
        Assert.Null(_v.Writes.Get(plain));
        Assert.NotNull(_v.Writes.Get(rich));
        Assert.False(Node_(rich).Properties.ContainsKey("gtask_id"));
        await Sync();
        Assert.Equal(1, _g.Lists["L1"].Count(t => !t.Deleted && t.Title == "Rich")); // pushed again as new
    }

    [Fact]
    public async Task A_local_delete_is_sent_to_google_unless_google_changed_it()
    {
        var a = Add("Delete me");
        var b = Add("Edited elsewhere");
        await Sync();
        _g.Edit(_g.ByTitle("Edited elsewhere").Id, t => t with { Due = "2026-12-24" });
        _v.Writes.Trash(a, Actor.User);
        _v.Writes.Trash(b, Actor.User);
        await Sync();
        Assert.True(_g.Lists["L1"].Single(t => t.Title == "Delete me").Deleted);
        Assert.False(_g.Lists["L1"].Single(t => t.Title == "Edited elsewhere").Deleted);
        await Sync();
        Assert.Contains(T.List(), t => t.Title == "Edited elsewhere"); // returns as a new task
    }

    [Fact]
    public async Task A_suspended_task_that_is_trashed_never_deletes_the_google_task()
    {
        var id = Add("Suspended");
        await Sync();
        _v.Writes.UpdateFields(id, null, new Dictionary<string, object?> { ["archived"] = true }, null, Actor.User, allowReserved: true);
        await Sync();
        _v.Writes.Trash(id, Actor.User);
        await Sync();
        Assert.False(_g.ByTitle("Suspended").Deleted);
    }

    [Fact]
    public async Task The_delete_guard_holds_a_large_batch_and_apply_or_cancel_resolves_it()
    {
        var ids = Enumerable.Range(0, 20).Select(i => Add("T" + i)).ToList();
        await Sync();
        foreach (var id in ids.Take(11)) _v.Writes.Trash(id, Actor.User);
        var held = await Sync();
        Assert.Equal(11, held.PendingDeletes);
        Assert.Equal(20, _g.Lists["L1"].Count(t => !t.Deleted));
        var applied = await Engine().ApplyPendingDeletes(CancellationToken.None);
        Assert.Equal(11, applied.Deleted);
        Assert.Equal(9, _g.Lists["L1"].Count(t => !t.Deleted));
    }

    [Fact]
    public async Task A_failure_part_way_through_apply_keeps_the_unhandled_deletions_held()
    {
        var ids = Enumerable.Range(0, 20).Select(i => Add("T" + i)).ToList();
        await Sync();
        foreach (var id in ids.Take(11)) _v.Writes.Trash(id, Actor.User);
        await Sync();
        Assert.Equal(11, _state.Load().PendingDeletes.Count);
        _g.FailDeleteNumber = 4;
        await Assert.ThrowsAsync<GoogleApiException>(() => Engine().ApplyPendingDeletes(CancellationToken.None));
        Assert.Equal(8, _state.Load().PendingDeletes.Count); // three were done and left the list; the fourth and the rest remain
        _g.FailDeleteNumber = null;
        await Engine().ApplyPendingDeletes(CancellationToken.None);
        Assert.Empty(_state.Load().PendingDeletes);
        Assert.Equal(9, _g.Lists["L1"].Count(t => !t.Deleted));
    }

    [Fact]
    public async Task A_change_that_can_never_be_applied_stops_holding_the_cursor_after_a_day_not_after_a_number_of_runs()
    {
        var id = Add("Stuck", "2026-11-02");
        _v.Writes.UpdateFields(id, null, new Dictionary<string, object?> { ["locked"] = true }, null, Actor.User, allowReserved: true);
        await Sync();
        var edited = _g.Edit(_g.ByTitle("Stuck").Id, t => t with { Due = "2026-12-12" });
        var editedAt = DateTimeOffset.Parse(edited.Updated, CultureInfo.InvariantCulture);
        for (var i = 0; i < 15; i++) await Sync(); // many runs in a row (every edit triggers one) must not release the hold
        Assert.True(DateTimeOffset.Parse(_state.Load().Cursors["L1"]!, CultureInfo.InvariantCulture) < editedAt);
        Assert.Single(_state.Load().HoldSince);
        var s = _state.Load();
        s.HoldSince[s.HoldSince.Keys.Single()] = DateTimeOffset.UtcNow.AddHours(-25);
        _state.Save(s);
        await Sync();
        Assert.True(DateTimeOffset.Parse(_state.Load().Cursors["L1"]!, CultureInfo.InvariantCulture) >= editedAt);
    }

    [Fact]
    public async Task A_transient_failure_during_an_automatic_delete_does_not_hold_later_deletes_for_the_person()
    {
        var ids = Enumerable.Range(0, 5).Select(i => Add("T" + i)).ToList();
        await Sync();
        foreach (var id in ids.Take(3)) _v.Writes.Trash(id, Actor.User);
        _g.FailDeleteNumber = 2;
        await Assert.ThrowsAsync<GoogleApiException>(() => Sync());
        var left = _state.Load().PendingDeletes;
        Assert.NotEmpty(left);
        Assert.All(left, p => Assert.False(p.Held)); // not waiting for the person
        _g.FailDeleteNumber = null;
        var next = await Sync(); // the leftovers run again by themselves
        Assert.Equal(0, next.PendingDeletes);
        Assert.Equal(2, _g.Lists["L1"].Count(t => !t.Deleted));
    }

    [Fact]
    public async Task When_a_batch_is_held_every_waiting_deletion_becomes_visible_so_apply_runs_only_what_is_listed()
    {
        var ids = Enumerable.Range(0, 30).Select(i => Add("T" + i)).ToList();
        await Sync();
        foreach (var id in ids.Take(3)) _v.Writes.Trash(id, Actor.User);
        _g.FailDeleteNumber = 2;
        await Assert.ThrowsAsync<GoogleApiException>(() => Sync());
        Assert.Contains(_state.Load().PendingDeletes, p => !p.Held); // a leftover of the transient failure
        _g.FailDeleteNumber = null;
        foreach (var id in ids.Skip(3).Take(12)) _v.Writes.Trash(id, Actor.User);
        await Sync(); // 12 new ones are more than the limit, so everything waiting is held
        var waiting = _state.Load().PendingDeletes;
        Assert.NotEmpty(waiting);
        Assert.All(waiting, p => Assert.True(p.Held));
    }

    [Fact]
    public async Task Leftovers_of_repeated_transient_failures_cannot_pile_up_past_the_guard_unseen()
    {
        var ids = Enumerable.Range(0, 40).Select(i => Add("T" + i)).ToList();
        await Sync();
        foreach (var id in ids.Take(7)) _v.Writes.Trash(id, Actor.User);
        _g.FailDeleteNumber = 2; // the first delete works, the second fails: leftovers stay, not held
        await Assert.ThrowsAsync<GoogleApiException>(() => Sync());
        var leftovers = _state.Load().PendingDeletes.Count;
        Assert.InRange(leftovers, 1, 10);
        Assert.All(_state.Load().PendingDeletes, p => Assert.False(p.Held));
        _g.FailDeleteNumber = null;
        foreach (var id in ids.Skip(7).Take(6)) _v.Writes.Trash(id, Actor.User); // few new ones, but with the leftovers more than the limit
        var held = await Sync();
        Assert.True(held.PendingDeletes > 10);
        Assert.All(_state.Load().PendingDeletes, p => Assert.True(p.Held));
        Assert.True(_g.Lists["L1"].Count(t => !t.Deleted) > 40 - 13); // nothing more ran
    }

    [Fact]
    public async Task When_google_refuses_to_retire_one_moved_task_it_still_syncs_in_the_new_list_and_one_problem_is_reported()
    {
        _v.Writes.Vault.EnsureWorkspace("work");
        _g.Lists["L2"] = [];
        var ids = new[] { Add("Mover A", "2026-11-02"), Add("Mover B", "2026-11-03") };
        await Sync();
        foreach (var id in ids) _v.Writes.MoveToWorkspace(id, "work", Actor.User);
        var work = new GoogleListMapping("work", "L2", "Work", DateTimeOffset.UtcNow.AddDays(-30), true);
        _g.FailDelete = new GoogleApiException(400, "invalid", "refused");
        var report = await Engine().Run([Map, work], CancellationToken.None);
        _g.FailDelete = null;
        Assert.Single(report.Problems, p => p.Workspace == "work" && p.Reason.Contains("2 moved tasks"));
        Assert.True(report.Skipped >= 2);
        foreach (var id in ids) Assert.Equal("L2", _state.Load().Links[id].ListId);
        Assert.Equal(2, _g.Lists["L2"].Count(t => !t.Deleted));
        Assert.Equal(2, _g.Lists["L1"].Count(t => !t.Deleted)); // the old copies are left for the person
    }

    [Fact]
    public async Task A_hint_from_another_list_on_a_task_linked_here_is_only_repaired()
    {
        var id = Add("Shared vault task", "2026-11-02");
        await Sync();
        var gid = _g.ByTitle("Shared vault task").Id;
        // another device wrote a hint for a different list into the shared vault
        _v.Writes.UpdateFields(id, null, new Dictionary<string, object?> { ["gtask_id"] = "other", ["gtask_list"] = "L9" }, null, Actor.User, derived: true);
        var mutations = _g.Mutations;
        await Sync();
        Assert.Equal("L1", Node_(id).Properties["gtask_list"]);
        Assert.Equal(gid, Node_(id).Properties["gtask_id"]);
        Assert.Equal(mutations, _g.Mutations); // no second Google task, no delete
        Assert.Single(_g.Lists["L1"]);
    }

    [Fact]
    public async Task A_failed_seed_patch_does_not_leave_a_link_with_an_empty_base()
    {
        var id = Add("Seed", "2026-11-02");
        await Sync();
        _state.Delete(); // lose the state: the next run relinks from the hint and patches differences
        _g.Edit(_g.ByTitle("Seed").Id, t => t with { Due = "2026-12-12" });
        _g.FailPatch = new GoogleApiException(503, "network", "down");
        await Assert.ThrowsAsync<GoogleApiException>(() => Sync());
        Assert.Empty(_state.Load().Links);
        _g.FailPatch = null;
        await Sync();
        Assert.Equal("2026-11-02", _g.ByTitle("Seed").Due); // Quantos won on the first link
        Assert.Equal("2026-11-02", Task_(id).Due);
    }

    [Fact]
    public async Task A_failure_while_retiring_the_old_google_task_keeps_the_link_and_hint_for_the_next_run()
    {
        _v.Writes.Vault.EnsureWorkspace("work");
        _g.Lists["L2"] = [];
        var id = Add("Mover", "2026-11-02");
        await Sync();
        var oldGid = _g.ByTitle("Mover").Id;
        _v.Writes.MoveToWorkspace(id, "work", Actor.User);
        var work = new GoogleListMapping("work", "L2", "Work", DateTimeOffset.UtcNow.AddDays(-30), true);
        _g.FailDeleteNumber = 1;
        await Assert.ThrowsAsync<GoogleApiException>(() => Engine().Run([Map, work], CancellationToken.None));
        Assert.Equal("L1", Node_(id).Properties["gtask_list"]); // hint and link survive the failure
        Assert.Equal("L1", _state.Load().Links[id].ListId);
        Assert.False(_g.Find(oldGid).Deleted);
        _g.FailDeleteNumber = null;
        await Engine().Run([Map, work], CancellationToken.None);
        Assert.True(_g.Find(oldGid).Deleted);
        Assert.Single(_g.Lists["L2"].Where(t => t.Title == "Mover" && !t.Deleted));
        Assert.Single(T.List(), t => t.Title == "Mover");
    }

    [Fact]
    public async Task An_intent_survives_a_failed_recovery_and_links_the_task_on_the_next_run()
    {
        var id = Add("Intent patch", "2026-11-02", "local notes");
        var s = _state.Load();
        s.Intents.Add(new GoogleIntent { TaskId = id, ListId = "L1", CursorAtIntent = null, TitleNorm = "Intent patch", Due = "2026-11-02" });
        _state.Save(s);
        _g.Add("Intent patch", "2026-11-02", "different notes"); // the recovered task differs, so linking patches it
        _g.FailPatch = new GoogleApiException(503, "network", "down");
        await Assert.ThrowsAsync<GoogleApiException>(() => Sync());
        Assert.Single(_state.Load().Intents);
        _g.FailPatch = null;
        await Sync();
        Assert.Single(_g.Lists["L1"]);
        Assert.Empty(_state.Load().Intents);
        Assert.Equal("local notes", _g.Lists["L1"][0].Notes);
    }

    [Fact]
    public async Task A_missing_parent_when_moving_does_not_make_the_child_look_deleted()
    {
        var parent = Add("Gone parent");
        var child = Add("Child");
        await Sync();
        _g.FailMove = new GoogleApiException(404, "notFound", "parent not found");
        T.Update(child, new Dictionary<string, object?> { ["parent"] = parent }, [], Actor.User);
        await Sync();
        Assert.NotNull(_v.Writes.Get(child));
        Assert.Contains(_state.Load().Links.Keys, k => k == child);
        Assert.Empty(_state.Load().PendingDeletes);
    }

    [Fact]
    public async Task A_task_moved_to_another_mapped_workspace_retires_its_google_task_in_the_old_list()
    {
        _v.Writes.Vault.EnsureWorkspace("work");
        _g.Lists["L2"] = [];
        var id = Add("Mover", "2026-11-02");
        await Sync();
        var oldGid = _g.ByTitle("Mover").Id;
        _v.Writes.MoveToWorkspace(id, "work", Actor.User);
        var work = new GoogleListMapping("work", "L2", "Work", DateTimeOffset.UtcNow.AddDays(-30), true);
        await Engine().Run([Map, work], CancellationToken.None);
        Assert.True(_g.Find(oldGid).Deleted);
        Assert.Single(_g.Lists["L2"].Where(t => t.Title == "Mover" && !t.Deleted));
        await Engine().Run([Map, work], CancellationToken.None);
        Assert.Single(_g.Lists["L2"].Where(t => t.Title == "Mover" && !t.Deleted));
        Assert.Single(T.List(), t => t.Title == "Mover");
    }

    [Fact]
    public async Task Ten_deletions_out_of_twenty_run_immediately()
    {
        var ids = Enumerable.Range(0, 20).Select(i => Add("T" + i)).ToList();
        await Sync();
        foreach (var id in ids.Take(10)) _v.Writes.Trash(id, Actor.User);
        var r = await Sync();
        Assert.Equal(0, r.PendingDeletes);
        Assert.Equal(10, _g.Lists["L1"].Count(t => !t.Deleted));
    }

    [Fact]
    public async Task Cancelling_held_local_deletes_returns_the_google_tasks_as_new_local_tasks()
    {
        var ids = Enumerable.Range(0, 20).Select(i => Add("T" + i)).ToList();
        await Sync();
        foreach (var id in ids.Take(11)) _v.Writes.Trash(id, Actor.User);
        await Sync();
        await Engine().CancelPendingDeletes(CancellationToken.None);
        await Sync();
        Assert.Equal(20, _g.Lists["L1"].Count(t => !t.Deleted));
        Assert.Equal(20, T.List().Count);
    }

    // ---- task-level errors, list problems, preview ----------------------------------------------

    [Fact]
    public async Task A_missing_list_pauses_the_mapping_with_a_problem()
    {
        var r = await Engine().Run([Map with { ListId = "gone" }], CancellationToken.None);
        Assert.Single(r.Problems);
    }

    [Fact]
    public async Task Preview_counts_and_changes_nothing()
    {
        Add("Local one");
        Add("Local two");
        _g.Add("Remote one");
        T.Create(new CreateTaskRequest { Title = "Recurring", Recurrence = "daily" }, Actor.User);
        var before = Node_(T.List()[0].Id).Hash;
        var p = await Engine().Preview(Map, CancellationToken.None);
        Assert.Equal(2, p.CreateInGoogle);
        Assert.Equal(1, p.ImportToQuantos);
        Assert.Equal(0, p.Link);
        Assert.Equal(1, p.Excluded);
        Assert.Equal(0, _g.Mutations);
        Assert.Equal(3 + 0, T.List().Count + 0 - 0);
        Assert.Empty(_state.Load().Links);
    }

    [Fact]
    public async Task Resetting_a_mapping_forgets_its_links_and_cursor()
    {
        Add("A");
        await Sync();
        Assert.NotEmpty(_state.Load().Links);
        Engine().ResetMapping("default", "L1");
        var s = _state.Load();
        Assert.Empty(s.Links);
        Assert.False(s.Cursors.ContainsKey("L1"));
    }

    [Fact]
    public async Task More_than_one_page_of_google_tasks_is_imported()
    {
        for (var i = 0; i < 130; i++) _g.Add("G" + i);
        await Sync();
        Assert.Equal(130, T.List().Count);
    }
}

public class GoogleTasksApiTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return respond(request);
        }
    }

    private sealed class Tokens : IGoogleAccessTokens
    {
        public int Refreshes;
        public Task<string> Get(bool force, CancellationToken ct) { if (force) Refreshes++; return Task.FromResult(force ? "fresh" : "stale"); }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public void Writes_send_explicit_nulls_and_reopen_clears_completed()
    {
        var body = GoogleTasksApi.Body(new GTaskWrite { Due = Optional<string?>.Of(null), Notes = Optional<string?>.Of(null), Completed = Optional<bool>.Of(false) });
        Assert.Equal("{\"notes\":null,\"due\":null,\"status\":\"needsAction\",\"completed\":null}", body.ToJsonString());
        Assert.Equal("{\"title\":\"x\",\"due\":\"2026-11-02T00:00:00.000Z\",\"status\":\"completed\"}",
            GoogleTasksApi.Body(new GTaskWrite { Title = Optional<string?>.Of("x"), Due = Optional<string?>.Of("2026-11-02"), Completed = Optional<bool>.Of(true) }).ToJsonString());
        Assert.Equal("{}", GoogleTasksApi.Body(new GTaskWrite()).ToJsonString());
    }

    [Fact]
    public async Task List_asks_for_everything_pages_and_reads_the_utc_date()
    {
        var h = new Handler(r => r.RequestUri!.Query.Contains("pageToken=p2")
            ? Json("{\"items\":[{\"id\":\"b\",\"title\":\"B\",\"status\":\"completed\",\"deleted\":true,\"assignmentInfo\":{}}]}")
            : Json("{\"items\":[{\"id\":\"a\",\"title\":\"A\",\"due\":\"2026-11-02T00:00:00.000Z\",\"updated\":\"u\"}],\"nextPageToken\":\"p2\"}"));
        var api = new GoogleTasksApi(new HttpClient(h), new Tokens());
        var first = await api.ListTasks("L", DateTimeOffset.Parse("2026-10-30T10:00:00Z", CultureInfo.InvariantCulture), null, default);
        Assert.Equal("2026-11-02", first.Items[0].Due);
        var second = await api.ListTasks("L", null, first.NextPageToken, default);
        Assert.True(second.Items[0].Deleted && second.Items[0].Completed && second.Items[0].Assigned);
        var q = h.Requests[0].RequestUri!.Query;
        Assert.Contains("showCompleted=true", q); Assert.Contains("showDeleted=true", q); Assert.Contains("showHidden=true", q); Assert.Contains("showAssigned=true", q);
        Assert.Contains("updatedMin=2026-10-30T10%3A00%3A00.000Z", q);
    }

    [Fact]
    public async Task A_reply_that_is_not_the_documented_json_is_a_502_google_error_not_a_crash()
    {
        var api = new GoogleTasksApi(new HttpClient(new Handler(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/lists")
            ? Json("<html>oops</html>") : Json("{\"title\":\"no id\"}"))), new Tokens());
        var html = await Assert.ThrowsAsync<GoogleApiException>(() => api.ListTaskLists(default));
        Assert.Equal(502, html.Status);
        var missing = await Assert.ThrowsAsync<GoogleApiException>(() => api.Insert("L", new GTaskWrite(), null, default));
        Assert.Equal(502, missing.Status);
        // valid JSON of the wrong shape
        var shape = new GoogleTasksApi(new HttpClient(new Handler(_ => Json("{\"items\":\"not an array\"}"))), new Tokens());
        Assert.Equal(502, (await Assert.ThrowsAsync<GoogleApiException>(() => shape.ListTasks("L", null, null, default))).Status);
        Assert.Equal(502, (await Assert.ThrowsAsync<GoogleApiException>(() => shape.ListTaskLists(default))).Status);
        var array = new GoogleTasksApi(new HttpClient(new Handler(_ => Json("[1,2]"))), new Tokens());
        Assert.Equal(502, (await Assert.ThrowsAsync<GoogleApiException>(() => array.ListTasks("L", null, null, default))).Status);
    }

    [Fact]
    public async Task A_401_refreshes_once_then_retries_and_a_second_401_needs_reconnect()
    {
        var calls = 0;
        var tokens = new Tokens();
        var api = new GoogleTasksApi(new HttpClient(new Handler(r => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Json("{\"id\":\"x\"}"))), tokens);
        Assert.NotNull(await api.Get("L", "x", default));
        Assert.Equal(1, tokens.Refreshes);
        var always = new GoogleTasksApi(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))), new Tokens());
        await Assert.ThrowsAsync<GoogleAuthException>(() => always.Get("L", "x", default));
    }

    [Fact]
    public async Task Errors_carry_reason_and_retry_after_and_delete_treats_missing_as_success()
    {
        var api = new GoogleTasksApi(new HttpClient(new Handler(r =>
        {
            if (r.Method == HttpMethod.Delete) return Json("{}", HttpStatusCode.NotFound);
            var response = Json("{\"error\":{\"message\":\"slow down\",\"errors\":[{\"reason\":\"rateLimitExceeded\"}]}}", (HttpStatusCode)429);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        })), new Tokens());
        var ex = await Assert.ThrowsAsync<GoogleApiException>(() => api.Patch("L", "x", new GTaskWrite(), default));
        Assert.Equal(429, ex.Status);
        Assert.Equal("rateLimitExceeded", ex.Reason);
        Assert.Equal(TimeSpan.FromSeconds(30), ex.RetryAfter);
        await api.Delete("L", "x", default);
    }
}

public class GoogleOAuthTests
{
    private sealed class Handler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public List<string> Urls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Bodies.Add(body); Urls.Add(request.RequestUri!.ToString());
            return respond(request, body);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static string State(string url) => System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["state"]!;

    [Fact]
    public void Only_the_google_consent_page_is_an_acceptable_url()
    {
        Assert.True(GoogleOAuth.IsConsentUrl("https://accounts.google.com/o/oauth2/v2/auth?client_id=x"));
        Assert.False(GoogleOAuth.IsConsentUrl("http://accounts.google.com/o/oauth2/v2/auth"));
        Assert.False(GoogleOAuth.IsConsentUrl("https://evil.example/o/oauth2/v2/auth"));
        Assert.False(GoogleOAuth.IsConsentUrl("https://accounts.google.com.evil.example/o/oauth2/v2/auth"));
        Assert.False(GoogleOAuth.IsConsentUrl("https://user@accounts.google.com/o/oauth2/v2/auth"));
        Assert.False(GoogleOAuth.IsConsentUrl("https://accounts.google.com/other"));
        Assert.False(GoogleOAuth.IsConsentUrl(null));
    }

    [Fact]
    public async Task A_wrong_state_leaves_the_flow_and_the_right_one_completes_it_once()
    {
        var h = new Handler((_, _) => Json("{\"access_token\":\"a\",\"refresh_token\":\"r\",\"expires_in\":3600,\"scope\":\"https://www.googleapis.com/auth/tasks\"}"));
        var oauth = new GoogleOAuth(new HttpClient(h));
        var url = oauth.StartFlow("cid", "http://127.0.0.1:1/oauth/google/callback");
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("access_type=offline", url);
        Assert.Equal(GoogleOAuth.CallbackResult.WrongState, (await oauth.Complete("c", "nope", "cid", "sec", default)).Result);
        Assert.True(oauth.FlowPending);
        var ok = await oauth.Complete("c", State(url), "cid", "sec", default);
        Assert.Equal(GoogleOAuth.CallbackResult.Completed, ok.Result);
        Assert.Equal("r", ok.Tokens!.RefreshToken);
        Assert.Contains("code_verifier=", h.Bodies[0]);
        Assert.Equal(GoogleOAuth.CallbackResult.NoFlow, (await oauth.Complete("c", State(url), "cid", "sec", default)).Result);
    }

    [Fact]
    public async Task An_expired_flow_is_discarded_and_a_missing_scope_stores_nothing()
    {
        var now = DateTimeOffset.UtcNow;
        var oauth = new GoogleOAuth(new HttpClient(new Handler((_, _) => Json("{\"access_token\":\"a\",\"refresh_token\":\"r\",\"scope\":\"email\"}"))));
        oauth.UseClock(() => now);
        var url = oauth.StartFlow("cid", "http://127.0.0.1:1/cb");
        var scope = await oauth.Complete("c", State(url), "cid", "sec", default);
        Assert.Equal(GoogleOAuth.CallbackResult.Failed, scope.Result);
        Assert.Contains("Google Tasks box", scope.Message);
        var url2 = oauth.StartFlow("cid", "http://127.0.0.1:1/cb");
        now = now.AddMinutes(11);
        Assert.Equal(GoogleOAuth.CallbackResult.Expired, (await oauth.Complete("c", State(url2), "cid", "sec", default)).Result);
        Assert.False(oauth.FlowPending);
    }

    [Fact]
    public async Task A_slow_token_endpoint_is_reported_as_a_timeout_not_thrown_as_a_cancellation()
    {
        var oauth = new GoogleOAuth(new HttpClient(new Handler((_, _) => throw new TaskCanceledException("timed out"))));
        var ex = await Assert.ThrowsAsync<GoogleApiException>(() => oauth.Refresh("cid", "sec", "refresh", default));
        Assert.Equal("timeout", ex.Reason);
        var url = oauth.StartFlow("cid", "http://127.0.0.1:1/cb");
        var done = await oauth.Complete("c", State(url), "cid", "sec", default);
        Assert.Equal(GoogleOAuth.CallbackResult.Retry, done.Result);
    }

    [Fact]
    public async Task A_network_failure_keeps_the_code_so_the_exchange_can_finish_when_the_app_is_back_in_front()
    {
        var blocked = true;
        var h = new Handler((_, _) => blocked
            ? throw new HttpRequestException("Connection failure")
            : Json("{\"access_token\":\"a\",\"refresh_token\":\"r\",\"expires_in\":3600,\"scope\":\"https://www.googleapis.com/auth/tasks\"}"));
        var now = DateTimeOffset.UtcNow;
        var oauth = new GoogleOAuth(new HttpClient(h));
        oauth.UseClock(() => now);
        var url = oauth.StartFlow("cid", "http://127.0.0.1:1/cb");
        Assert.Equal(GoogleOAuth.CallbackResult.Retry, (await oauth.Complete("code1", State(url), "cid", "sec", default)).Result);
        Assert.True(oauth.HasDeferred);
        Assert.True(oauth.FlowPending);
        Assert.Equal(GoogleOAuth.CallbackResult.Retry, (await oauth.CompleteDeferred("cid", "sec", default)).Result); // still blocked: kept again
        blocked = false;
        var done = await oauth.CompleteDeferred("cid", "sec", default);
        Assert.Equal(GoogleOAuth.CallbackResult.Completed, done.Result);
        Assert.Contains("code=code1", h.Bodies[^1]);
        Assert.False(oauth.HasDeferred);
        Assert.Equal(GoogleOAuth.CallbackResult.NoFlow, (await oauth.CompleteDeferred("cid", "sec", default)).Result);

        blocked = true;
        var url2 = oauth.StartFlow("cid", "http://127.0.0.1:1/cb");
        await oauth.Complete("code2", State(url2), "cid", "sec", default);
        now = now.AddMinutes(9);
        Assert.False(oauth.HasDeferred);
        Assert.Equal(GoogleOAuth.CallbackResult.Expired, (await oauth.CompleteDeferred("cid", "sec", default)).Result);
    }

    [Fact]
    public async Task A_token_refresh_in_flight_when_credentials_change_is_not_cached()
    {
        var release = new TaskCompletionSource();
        var refreshes = 0;
        var handler = new Handler((_, _) => { Interlocked.Increment(ref refreshes); release.Task.Wait(); return Json("{\"access_token\":\"old-account\",\"expires_in\":3600}"); });
        var config = new GoogleTasksConfig { ClientId = "cid", ProtectedClientSecret = GoogleTasksSettingsStore.Seal("sec"), ProtectedRefreshToken = GoogleTasksSettingsStore.Seal("rt") };
        var source = new GoogleTokenSource(new GoogleOAuth(new HttpClient(handler)), () => config);
        var inFlight = Task.Run(() => source.Get(false, default)); // the handler blocks, so run the call off the test thread
        while (Volatile.Read(ref refreshes) == 0) await Task.Delay(10);
        source.Forget(); // credentials changed while the refresh was running
        release.SetResult();
        Assert.Equal("old-account", await inFlight);
        await source.Get(false, default); // must refresh again instead of reusing the old account's token
        Assert.Equal(2, refreshes);
    }

    [Fact]
    public async Task Invalid_grant_needs_reconnect_without_leaking_secrets_and_revoke_uses_a_form_body()
    {
        var h = new Handler((r, _) => r.RequestUri!.AbsolutePath.Contains("revoke") ? Json("{}") : Json("{\"error\":\"invalid_grant\"}", HttpStatusCode.BadRequest));
        var oauth = new GoogleOAuth(new HttpClient(h));
        var ex = await Assert.ThrowsAsync<GoogleAuthException>(() => oauth.Refresh("cid", "TOPSECRET", "REFRESHTOKEN", default));
        Assert.DoesNotContain("TOPSECRET", ex.Message);
        Assert.DoesNotContain("REFRESHTOKEN", ex.Message);
        Assert.True(await oauth.Revoke("REFRESHTOKEN", default));
        // Google answers 400 for a token that is already revoked: that counts as revoked
        Assert.True(await new GoogleOAuth(new HttpClient(new Handler((_, _) => Json("{\"error\":\"invalid_token\"}", HttpStatusCode.BadRequest)))).Revoke("t", default));
        Assert.False(await new GoogleOAuth(new HttpClient(new Handler((_, _) => Json("{}", HttpStatusCode.InternalServerError)))).Revoke("t", default));
        Assert.DoesNotContain("REFRESHTOKEN", h.Urls.Last());
        Assert.Contains("token=REFRESHTOKEN", h.Bodies.Last());
    }
}
