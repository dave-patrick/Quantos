using System.Globalization;
using Quantos.Core.Security;
using Quantos.Core.Tasks;
using TaskStatus = Quantos.Core.Tasks.TaskStatus;
using static Quantos.Core.GoogleTasks.GoogleTasksMapper;

namespace Quantos.Core.GoogleTasks;

/// <summary>
/// Two-way sync of five task fields (title, notes, due date, done state, parent) between Quantos task nodes and Google Tasks.
/// Each side is compared with what it had at the last sync (the link's Base), so no clocks are involved: a field changed on one
/// side is copied to the other; changed on both sides, the Quantos value wins. Everything else about a task stays local.
/// See documentation/GOOGLE-TASKS-SYNC-DESIGN.md.
/// </summary>
public sealed class GoogleTasksSyncEngine(WriteService writes, TaskService tasks, Clock clock, IGoogleTasksStateStore store, IGoogleTasksApi api)
{
    public static readonly Actor GoogleActor = new(ActorKind.System, "google-tasks");
    private static readonly HashSet<string> PristineKeys = ["tz", "status", "completed", "due", "parent", HintId, HintList];
    private const int BatchSave = 25;
    private const int MissingCyclesBeforeDelete = 3;

    private enum Act { None, Pull, Push, Conflict, Settle }

    /// <summary>Everything one run needs: the state it edits and the totals it reports.</summary>
    private sealed class Ctx(GoogleTasksState state, HashSet<string> trashIds)
    {
        public GoogleTasksState S { get; } = state;
        public HashSet<string> TrashIds { get; set; } = trashIds;
        /// <summary>Per list: the earliest Google change that could not be applied this run. The cursor must stay before it so it is fetched again.</summary>
        public Dictionary<string, DateTimeOffset> HoldCursor { get; } = [];
        public HashSet<string> HeldThisRun { get; } = [];
        /// <summary>Task id → parent id for every task, read once per run (null until first needed).</summary>
        public Dictionary<string, string?>? ParentById { get; set; }
        public List<GoogleMappingProblem> Problems { get; } = [];
        public int Pushed, Pulled, Deleted, Skipped, Conflicts;
        public int Operations;
        public List<PendingDelete> NewPending { get; } = [];
    }

    private sealed class Local(TaskDto t, Node n)
    {
        public TaskDto T { get; set; } = t;
        public Node N { get; set; } = n;
    }

    private readonly SemaphoreSlim _gate = new(1, 1);

    // ---- public surface ---------------------------------------------------------------------

    public async Task<GoogleSyncReport> Run(IReadOnlyList<GoogleListMapping> confirmedMappings, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var s = store.Load();
            var ctx = new Ctx(s, Trashed());
            try
            {
                foreach (var m in confirmedMappings)
                {
                    ct.ThrowIfCancellationRequested();
                    try { await SyncMapping(ctx, m, ct); }
                    catch (GoogleApiException ex) when (ex.Status == 404 && ex.Reason is null or "notFound") { ctx.Problems.Add(new(m.Workspace, "List missing: " + m.ListTitle)); }
                    // A local failure (for example a workspace that was renamed) stops only that mapping.
                    catch (QuantosException ex) { ctx.Problems.Add(new(m.Workspace, ex.Message)); }
                }
                await ProcessDeletes(ctx, confirmedMappings, ct);
            }
            finally { store.Save(s); } // everything is idempotent, so a partial run's progress is kept
            return Report(ctx);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Counts only; no local writes, no Google changes, no state changes.</summary>
    public async Task<GoogleSyncPreview> Preview(GoogleListMapping mapping, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var m = mapping with { ImportCutoff = cutoff };
        var s = store.Load();
        var google = new List<GTask>();
        string? page = null;
        do
        {
            var p = await api.ListTasks(m.ListId, null, page, ct);
            google.AddRange(p.Items);
            page = p.NextPageToken;
        } while (page is not null);
        var live = google.Where(g => !g.Deleted).ToList();
        var locals = LoadLocals(m.Workspace);
        var linkedByTask = s.Links.Where(kv => kv.Value.ListId == m.ListId).ToDictionary(kv => kv.Key, kv => kv.Value);
        var gids = live.ToDictionary(g => g.Id);
        int excluded = 0, link = 0, create = 0;
        var matchedGids = new HashSet<string>();
        foreach (var l in locals.Values)
        {
            var hint = HintOf(l.N);
            var isLinked = linkedByTask.ContainsKey(l.T.Id);
            if (!Eligible(l, m, isLinked || (hint.List == m.ListId && hint.Id is not null && gids.ContainsKey(hint.Id)))) { excluded++; continue; }
            if (isLinked) { link++; matchedGids.Add(linkedByTask[l.T.Id].Gid); }
            else if (hint.List == m.ListId && hint.Id is not null && gids.ContainsKey(hint.Id)) { link++; matchedGids.Add(hint.Id); }
            else create++;
        }
        int import = 0;
        foreach (var g in live)
        {
            if (g.Assigned || BeforeCutoff(g, cutoff)) { if (!matchedGids.Contains(g.Id)) excluded++; continue; }
            if (!matchedGids.Contains(g.Id)) import++;
        }
        return new GoogleSyncPreview(cutoff, create, import, link, excluded);
    }

    public async Task<GoogleSyncReport> ApplyPendingDeletes(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var s = store.Load();
            var ctx = new Ctx(s, Trashed());
            // Entries leave the held list one at a time as they are handled, so a failure part-way keeps the rest.
            try { await ExecuteDeletes(ctx, s.PendingDeletes.ToList(), ct); }
            finally { store.Save(s); }
            return Report(ctx);
        }
        finally { _gate.Release(); }
    }

    public async Task CancelPendingDeletes(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var s = store.Load();
            foreach (var d in s.PendingDeletes)
            {
                s.Links.Remove(d.TaskId);
                if (d.Direction == DeleteDirection.ToGoogle) s.Cursors[d.ListId] = null;
                else if (writes.Get(d.TaskId) is not null) WriteHints(d.TaskId, null, null);
            }
            s.PendingDeletes.Clear();
            store.Save(s);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Forgets the links, cursor, in-flight inserts and held deletions for one workspace/list pair.</summary>
    public void ResetMapping(string workspace, string listId)
    {
        _gate.Wait();
        try
        {
            var s = store.Load();
            foreach (var id in s.Links.Where(kv => kv.Value.ListId == listId).Select(kv => kv.Key).ToList()) s.Links.Remove(id);
            s.Cursors.Remove(listId);
            s.Intents.RemoveAll(i => i.ListId == listId);
            s.PendingDeletes.RemoveAll(d => d.ListId == listId);
            foreach (var key in s.HoldSince.Keys.Where(k => k.StartsWith(listId + "|", StringComparison.Ordinal)).ToList()) s.HoldSince.Remove(key);
            store.Save(s);
        }
        finally { _gate.Release(); }
    }

    private GoogleSyncReport Report(Ctx c) =>
        new(c.Pushed, c.Pulled, c.Deleted, c.S.Links.Count, c.Skipped, c.Conflicts, c.S.PendingDeletes.Count(p => p.Held), c.Problems);

    // ---- one mapping ------------------------------------------------------------------------

    private async Task SyncMapping(Ctx c, GoogleListMapping m, CancellationToken ct)
    {
        var s = c.S;
        var cutoff = m.ImportCutoff ?? DateTimeOffset.UtcNow.AddDays(-30);
        var cursorText = s.Cursors.GetValueOrDefault(m.ListId);
        DateTimeOffset? cursor = DateTimeOffset.TryParse(cursorText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

        // 1. fetch every changed Google task, then apply
        var fetched = new Dictionary<string, GTask>();
        string? page = null;
        do
        {
            var p = await api.ListTasks(m.ListId, cursor?.AddMinutes(-1), page, ct);
            foreach (var g in p.Items) fetched[g.Id] = g;
            page = p.NextPageToken;
        } while (page is not null);

        var locals = LoadLocals(m.Workspace);
        var byGid = s.Links.Where(kv => kv.Value.ListId == m.ListId).ToDictionary(kv => kv.Value.Gid, kv => kv.Key);

        // stale hints: a hint for another list in a workspace mapped here is cleared
        var refused = 0;
        foreach (var l in locals.Values.ToList())
        {
            var hint = HintOf(l.N);
            if (hint.List is not null && hint.List != m.ListId && !l.N.ReadOnly)
            {
                var hasOldLink = s.Links.TryGetValue(l.T.Id, out var old);
                if (hasOldLink && old!.ListId == m.ListId) { RepairHint(l, old); continue; } // linked here: a hint from elsewhere is only repaired
                if (hasOldLink)
                {
                    // The task moved here from a workspace mapped to another list: retire its Google task there, so it is not left
                    // behind (and re-imported into the old workspace later). A failure that stops the run (offline, rate limit) leaves the
                    // link and hint in place so the next run tries again. If Google refuses this one task, retiring is given up: the old
                    // copy stays in the old list (reported), so the task is not blocked from syncing in its new one.
                    try { await RetireOldLink(c, old!, ct); }
                    catch (GoogleApiException ex) when (IsTaskLevel(ex)) { c.Skipped++; refused++; }
                }
                // The link goes only once the hint is cleared, so a hint write that fails (a conflict, a locked task) repeats the whole step next run.
                if (!WriteHints(l.T.Id, null, null)) { c.Skipped++; continue; }
                Refresh(l);
                s.Links.Remove(l.T.Id);
            }
        }
        if (refused > 0)
            c.Problems.Add(new(m.Workspace, $"Couldn't remove {refused} moved task{(refused == 1 ? "" : "s")} from {(refused == 1 ? "its" : "their")} previous Google list; delete {(refused == 1 ? "it" : "them")} there if you no longer want {(refused == 1 ? "it" : "them")}."));

        await RecoverIntents(c, m, fetched, locals, byGid, ct);
        ResolveDuplicateHints(c, m, locals, byGid);

        // 2. link or import each fetched Google task (parents first)
        foreach (var g in fetched.Values.OrderBy(g => g.Parent is null ? 0 : 1))
        {
            ct.ThrowIfCancellationRequested();
            if (g.Assigned) { c.Skipped++; continue; }
            if (byGid.TryGetValue(g.Id, out var linkedTask))
            {
                if (g.Deleted && s.Links.TryGetValue(linkedTask, out var dead)) Queue(c, linkedTask, dead, locals.GetValueOrDefault(linkedTask)?.T.Title, DeleteDirection.ToQuantos);
                continue;
            }
            if (g.Deleted) continue;
            var match = locals.Values.FirstOrDefault(l => HintOf(l.N) is { } h && h.Id == g.Id && h.List == m.ListId && !s.Links.ContainsKey(l.T.Id));
            try
            {
                if (match is not null)
                {
                    if (!Eligible(match, m, true)) { c.Skipped++; continue; }
                    fetched[g.Id] = await SeedLink(c, m, match, g, ct); // the merge below must see the patched Google task, not the stale copy
                    byGid[g.Id] = match.T.Id;
                }
                else if (!BeforeCutoff(g, cutoff))
                {
                    var created = Import(c, m, g, locals, byGid);
                    if (created is not null) byGid[g.Id] = created;
                }
            }
            catch (GoogleApiException ex) when (IsTaskLevel(ex)) { c.Skipped++; Hold(c, m.ListId, g); }
            SaveBatch(c);
        }

        // hints that point at Google tasks that no longer exist (restored from trash, cancelled deletes) are cleared
        foreach (var l in locals.Values.ToList())
        {
            var hint = HintOf(l.N);
            if (hint.Id is null || hint.List != m.ListId || s.Links.ContainsKey(l.T.Id) || fetched.ContainsKey(hint.Id)) continue;
            if (!Eligible(l, m, true)) continue; // recurring, archived or read-only: left alone, not asked about every run
            try
            {
                var existing = await api.Get(m.ListId, hint.Id, ct);
                if (existing is null || existing.Deleted) { if (WriteHints(l.T.Id, null, null)) Refresh(l); }
                else if (!s.Links.Values.Any(k => k.Gid == hint.Id && k.ListId == m.ListId))
                {
                    await SeedLink(c, m, l, existing, ct); // existing is not in the fetched set, so there is no stale copy to replace
                    byGid[existing.Id] = l.T.Id;
                }
            }
            catch (GoogleApiException ex) when (IsTaskLevel(ex)) { c.Skipped++; }
        }

        // 3. linked pairs
        var linkedHere = s.Links.Where(kv => kv.Value.ListId == m.ListId).ToList();
        foreach (var (taskId, link) in linkedHere)
        {
            ct.ThrowIfCancellationRequested();
            if (!locals.TryGetValue(taskId, out var local)) continue; // gone from this workspace: the delete pass decides
            if (!Eligible(local, m, true)) { link.Suspended = true; link.AskGoogle = true; c.Skipped++; continue; }
            // Eligible again: it is no longer suspended (so trashing it deletes its Google task as usual). AskGoogle stays set until a merge has
            // succeeded with nothing held back, because a failure or a held pull must make the next run ask Google again.
            link.Suspended = false;
            var askGoogle = link.AskGoogle;
            link.Missing = 0;
            try
            {
                RepairHint(local, link);
                fetched.TryGetValue(link.Gid, out var g);
                // Google edits made while the task was suspended were fetched (and passed by the cursor) back then: ask for the task now,
                // so the first merge after it resumes sees what Google has instead of assuming nothing changed.
                if (g is null && askGoogle)
                {
                    g = await api.Get(link.ListId, link.Gid, ct);
                    if (g is null || g.Deleted) { Queue(c, taskId, link, local.T.Title, DeleteDirection.ToQuantos); continue; }
                }
                if (g is { Deleted: true }) continue; // queued above
                if (g is { Assigned: true }) { c.Skipped++; link.AskGoogle = false; continue; }
                await MergePair(c, m, local, link, g, locals, byGid, ct);
                // A pull that could not be applied (the task is locked, the person was editing it) was held: keep asking Google, because for a
                // task resumed through Get the cursor is already past that edit. This does not pin the cursor (that hold ends after a day); the
                // cost is one Get per run for a task whose pull stays held, which is rare and better than silently losing the Google edit.
                link.AskGoogle = c.HeldThisRun.Contains(m.ListId + "|" + link.Gid);
            }
            catch (GoogleApiException ex) when (ex.Status is 404 or 410) { Queue(c, taskId, link, local.T.Title, DeleteDirection.ToQuantos); }
            catch (GoogleApiException ex) when (IsTaskLevel(ex)) { c.Skipped++; Hold(c, m.ListId, fetched.GetValueOrDefault(link.Gid)); }
            SaveBatch(c);
        }

        // 4. push the new local tasks (top level first so children can name their parent)
        var candidates = locals.Values.Where(l => !s.Links.ContainsKey(l.T.Id) && HintOf(l.N).Id is null && Eligible(l, m, false)).ToList();
        foreach (var l in candidates.OrderBy(l => l.T.Parent is null ? 0 : 1))
        {
            ct.ThrowIfCancellationRequested();
            try { await PushNew(c, m, l, locals, ct); }
            catch (GoogleApiException ex) when (IsTaskLevel(ex)) { c.Skipped++; }
        }

        // 5. the cursor moves only when the whole mapping was applied and was not reset meanwhile
        if (fetched.Count > 0)
        {
            var max = fetched.Values.Select(g => DateTimeOffset.TryParse(g.Updated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var u) ? u : (DateTimeOffset?)null)
                .Where(u => u is not null).Select(u => u!.Value).DefaultIfEmpty().Max();
            // A change that could not be applied (locked task, the person editing it, a task-level error) is fetched again next time.
            // A held change older than the cursor came from a direct Get (AskGoogle), not from this fetch: pinning the cursor for it
            // would only re-fetch everything since, so it is ignored here.
            if (c.HoldCursor.TryGetValue(m.ListId, out var hold) && hold.AddMilliseconds(-1) < max && (cursor is null || hold > cursor.Value))
                max = hold.AddMilliseconds(-1);
            if (max != default && (cursor is null || max > cursor)) s.Cursors[m.ListId] = max.ToString("o", CultureInfo.InvariantCulture);
        }
        // A held gid that was applied (or no longer needs holding) stops counting.
        foreach (var key in s.HoldSince.Keys.Where(k => k.StartsWith(m.ListId + "|", StringComparison.Ordinal) && !c.HeldThisRun.Contains(k)).ToList())
            s.HoldSince.Remove(key);
        store.Save(s);
    }

    // ---- local snapshot and eligibility -------------------------------------------------------

    private Dictionary<string, Local> LoadLocals(string workspace)
    {
        var map = new Dictionary<string, Local>();
        foreach (var t in tasks.List(workspace, includeClosed: true, includeArchived: true))
            if (writes.Get(t.Id) is { } n) map[t.Id] = new Local(t, n);
        return map;
    }

    private void Refresh(Local l)
    {
        if (writes.Get(l.T.Id) is { } n) l.N = n;
        if (tasks.Get(l.T.Id) is { } t) l.T = t;
    }

    private static (string? Id, string? List) HintOf(Node n) =>
        (n.Properties.GetValueOrDefault(HintId) is { } i ? Convert.ToString(i, CultureInfo.InvariantCulture) : null,
         n.Properties.GetValueOrDefault(HintList) is { } l ? Convert.ToString(l, CultureInfo.InvariantCulture) : null);

    private static bool BeforeCutoff(GTask g, DateTimeOffset cutoff) =>
        g.Completed && (DateTimeOffset.TryParse(g.CompletedAt ?? g.Updated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) && at < cutoff);

    private bool Eligible(Local l, GoogleListMapping m, bool linkedOrHinted)
    {
        var t = l.T;
        if (t.Archived || l.N.ReadOnly) return false;
        if (!string.IsNullOrEmpty(t.Recurrence) || !string.IsNullOrEmpty(t.Series)) return false;
        if (!linkedOrHinted && TaskStatus.IsClosed(t.Status))
        {
            var cutoff = m.ImportCutoff ?? DateTimeOffset.UtcNow.AddDays(-30);
            var when = DateTimeOffset.TryParse(t.Completed ?? t.Modified, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : (DateTimeOffset?)null;
            if (when is { } w && w < cutoff) return false;
        }
        return true;
    }

    private HashSet<string> Trashed() => writes.Exclusive(() => writes.TrashBin.List().Select(e => e.Id).ToHashSet());

    private static bool Pristine(Node n) =>
        !n.Locked && !n.ReadOnly && n.Tags.Count == 0 && n.Aliases.Count == 0 && n.Properties.Keys.All(PristineKeys.Contains);

    // ---- hints --------------------------------------------------------------------------------

    private bool WriteHints(string taskId, string? gid, string? list)
    {
        var n = writes.Get(taskId);
        if (n is null) return false;
        var set = gid is null ? null : new Dictionary<string, object?> { [HintId] = gid, [HintList] = list };
        var remove = gid is null ? new[] { HintId, HintList } : null;
        var r = writes.UpdateFields(taskId, n.Hash, set, remove, GoogleActor, derived: true);
        return r.Status is SaveStatus.Saved or SaveStatus.Unchanged;
    }

    private void RepairHint(Local l, GoogleLink link)
    {
        var h = HintOf(l.N);
        if (h.Id == link.Gid && h.List == link.ListId) return;
        if (WriteHints(l.T.Id, link.Gid, link.ListId)) Refresh(l);
    }

    private void ResolveDuplicateHints(Ctx c, GoogleListMapping m, Dictionary<string, Local> locals, Dictionary<string, string> byGid)
    {
        var groups = locals.Values.Where(l => HintOf(l.N) is { } h && h.Id is not null && h.List == m.ListId)
            .GroupBy(l => HintOf(l.N).Id!).Where(g => g.Count() > 1);
        foreach (var group in groups)
        {
            var ordered = group.OrderBy(l => DateTimeOffset.TryParse(l.T.Created, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : DateTimeOffset.MaxValue)
                .ThenBy(l => l.T.Id, StringComparer.Ordinal).ToList();
            var owner = ordered[0];
            byGid[group.Key] = owner.T.Id;
            if (c.S.Links.TryGetValue(owner.T.Id, out _) == false)
            {
                var stale = ordered.Skip(1).FirstOrDefault(o => c.S.Links.ContainsKey(o.T.Id));
                if (stale is not null) { c.S.Links[owner.T.Id] = c.S.Links[stale.T.Id]; }
            }
            foreach (var other in ordered.Skip(1))
            {
                c.S.Links.Remove(other.T.Id);
                var redundant = Pristine(other.N) && other.T.SubtaskCount == 0 && SameContent(owner, other);
                if (redundant) { try { writes.Trash(other.T.Id, GoogleActor); locals.Remove(other.T.Id); continue; } catch (QuantosException) { } }
                if (!other.N.ReadOnly && WriteHints(other.T.Id, null, null)) Refresh(other);
            }
        }
    }

    private static bool SameContent(Local a, Local b) =>
        NormTitle(a.T.Title) == NormTitle(b.T.Title) && LocalDue(a.T.Due) == LocalDue(b.T.Due) && IsClosed(a.T.Status) == IsClosed(b.T.Status)
        && Hash(NotesFromBody(a.N.Body).Text) == Hash(NotesFromBody(b.N.Body).Text);

    // ---- crash recovery -----------------------------------------------------------------------

    private async Task RecoverIntents(Ctx c, GoogleListMapping m, Dictionary<string, GTask> fetched, Dictionary<string, Local> locals, Dictionary<string, string> byGid, CancellationToken ct)
    {
        foreach (var intent in c.S.Intents.Where(i => i.ListId == m.ListId).ToList())
        {
            // The intent is dropped once it is settled (linked, nothing to recover, or a task-level failure), never before a
            // failure that stops the run, so the next run can still find the Google task Quantos created.
            if (c.S.Links.ContainsKey(intent.TaskId) || !locals.TryGetValue(intent.TaskId, out var local)) { c.S.Intents.Remove(intent); continue; }
            DateTimeOffset? since = DateTimeOffset.TryParse(intent.CursorAtIntent, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var s0) ? s0 : null;
            var matches = fetched.Values.Where(g => !g.Deleted && !byGid.ContainsKey(g.Id) && Truncate(NormTitle(g.Title)) == intent.TitleNorm && g.Due == intent.Due
                && (since is null || (DateTimeOffset.TryParse(g.Updated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var u) && u > since))).ToList();
            if (matches.Count == 1 && Eligible(local, m, true))
            {
                try { fetched[matches[0].Id] = await SeedLink(c, m, local, matches[0], ct); byGid[matches[0].Id] = intent.TaskId; }
                catch (GoogleApiException ex) when (IsTaskLevel(ex)) { c.Skipped++; }
            }
            // several matches: a duplicate may result and is left for the person to tidy
            c.S.Intents.Remove(intent);
        }
        store.Save(c.S);
    }

    // ---- creating links -----------------------------------------------------------------------

    private async Task RetireOldLink(Ctx c, GoogleLink old, CancellationToken ct)
    {
        var google = await api.Get(old.ListId, old.Gid, ct);
        if (google is null || google.Deleted) return;
        var b = old.Base;
        var edited = NormTitle(google.Title) != b.Title.G || google.Due != b.Due.G || google.Completed != b.Done.G
            || (NotesFitFromGoogle(google.Notes) && Hash(NormNotes(google.Notes)) != b.Notes.G);
        if (edited) return;
        await api.Delete(old.ListId, old.Gid, ct);
        c.Deleted++;
    }

    /// <summary>The first time a pair is linked without a write (hint relink, rebuild): where the sides differ, Quantos wins.</summary>
    private async Task<GTask> SeedLink(Ctx c, GoogleListMapping m, Local l, GTask g, CancellationToken ct)
    {
        var link = new GoogleLink { Gid = g.Id, ListId = m.ListId, GoogleUpdated = g.Updated };
        var v = LocalValues(c, l, m.ListId);
        var write = new GTaskWrite();
        var differ = false;
        if (Truncate(v.Title) != NormTitle(g.Title)) { write = write with { Title = Optional<string?>.Of(Truncate(v.Title)) }; differ = true; }
        if (v.NotesOk && NotesFitFromGoogle(g.Notes) && v.NotesHash != Hash(NormNotes(g.Notes))) { write = write with { Notes = Optional<string?>.Of(v.NotesText.Length == 0 ? null : v.NotesText) }; differ = true; }
        if (v.Due != g.Due) { write = write with { Due = Optional<string?>.Of(v.Due) }; differ = true; }
        if (v.Done != g.Completed) { write = write with { Completed = Optional<bool>.Of(v.Done) }; differ = true; }
        var google = g;
        if (differ) { google = await api.Patch(m.ListId, g.Id, write, ct); c.Conflicts++; c.Pushed++; }
        SetBaseFromBoth(link, v, google, parentPushed: false);
        // parent: where they differ, Quantos wins on the next merge, which starts from this base
        link.Base.Parent.L = ParentOfGoogle(c, g.Parent, m.ListId);
        link.Base.Parent.G = g.Parent;
        c.S.Links[l.T.Id] = link; // only once the Patch (if any) succeeded and the base is complete
        return google;
    }

    private static void SetBaseFromBoth(GoogleLink link, LocalValue v, GTask g, bool parentPushed)
    {
        link.GoogleUpdated = g.Updated;
        link.Base.Title.L = v.Title; link.Base.Title.G = NormTitle(g.Title);
        link.Base.Notes.L = v.NotesHash; link.Base.Notes.G = Hash(NormNotes(g.Notes));
        link.Base.Due.L = v.Due; link.Base.Due.G = g.Due;
        link.Base.Done.L = v.Done; link.Base.Done.G = g.Completed;
        if (parentPushed) { link.Base.Parent.L = v.Parent; link.Base.Parent.G = g.Parent; }
    }

    private string? Import(Ctx c, GoogleListMapping m, GTask g, Dictionary<string, Local> locals, Dictionary<string, string> byGid)
    {
        string? parentId = null;
        if (g.Parent is not null && byGid.TryGetValue(g.Parent, out var pid) && locals.TryGetValue(pid, out var pl) && pl.T.Parent is null) parentId = pid;
        var created = tasks.Create(new CreateTaskRequest
        {
            Title = NormTitle(g.Title),
            Workspace = m.Workspace,
            Body = NotesFitFromGoogle(g.Notes) ? NormNotes(g.Notes) : "",
            Status = g.Completed ? TaskStatus.Done : TaskStatus.Todo,
            Due = g.Due,
            Parent = parentId,
            Completed = g.Completed && DateTimeOffset.TryParse(g.CompletedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? clock.Format(at) : null,
            Extra = new Dictionary<string, object?> { [HintId] = g.Id, [HintList] = m.ListId }
        }, GoogleActor);
        var node = writes.Get(created.Id)!;
        var dto = tasks.Get(created.Id)!;
        var local = new Local(dto, node);
        locals[created.Id] = local;
        if (c.ParentById is not null) c.ParentById[created.Id] = parentId;
        var v = LocalValues(c, local, m.ListId);
        var link = new GoogleLink { Gid = g.Id, ListId = m.ListId };
        SetBaseFromBoth(link, v, g, parentPushed: true);
        link.Base.Parent.L = parentId; link.Base.Parent.G = parentId is null ? null : g.Parent;
        if (g.Parent is not null && parentId is null) link.Base.Parent.G = g.Parent; // recorded, not applied: counted once
        c.S.Links[created.Id] = link;
        c.Pulled++;
        return created.Id;
    }

    private async Task PushNew(Ctx c, GoogleListMapping m, Local l, Dictionary<string, Local> locals, CancellationToken ct)
    {
        var v = LocalValues(c, l, m.ListId);
        string? parentGid = v.Parent is not null && c.S.Links.TryGetValue(v.Parent, out var pl) && pl.ListId == m.ListId ? pl.Gid : null;
        var write = new GTaskWrite
        {
            Title = Optional<string?>.Of(Truncate(v.Title)),
            Notes = v.NotesOk && v.NotesText.Length > 0 ? Optional<string?>.Of(v.NotesText) : Optional<string?>.None,
            Due = v.Due is null ? Optional<string?>.None : Optional<string?>.Of(v.Due),
            Completed = v.Done ? Optional<bool>.Of(true) : Optional<bool>.None
        };
        c.S.Intents.Add(new GoogleIntent { TaskId = l.T.Id, ListId = m.ListId, CursorAtIntent = c.S.Cursors.GetValueOrDefault(m.ListId), TitleNorm = Truncate(v.Title), Due = v.Due });
        store.Save(c.S);
        var created = await api.Insert(m.ListId, write, parentGid, ct);
        WriteHints(l.T.Id, created.Id, m.ListId);
        var link = new GoogleLink { Gid = created.Id, ListId = m.ListId };
        SetBaseFromBoth(link, v, created, parentPushed: true);
        link.Base.Parent.L = parentGid is null ? null : v.Parent; link.Base.Parent.G = created.Parent;
        c.S.Links[l.T.Id] = link;
        c.S.Intents.RemoveAll(i => i.TaskId == l.T.Id && i.ListId == m.ListId);
        c.Pushed++;
        Refresh(l);
        SaveBatch(c); // the intent saved before the insert already makes a crash here recoverable
    }

    // ---- merging a linked pair ----------------------------------------------------------------

    private sealed record LocalValue(string Title, string NotesText, string NotesHash, bool NotesOk, string? Due, bool Done, string? Parent);

    /// <summary>Whether a task has no parent of its own. Read from one listing per run instead of rebuilding the task index for every question.</summary>
    private bool IsTopLevel(Ctx c, string id)
    {
        c.ParentById ??= tasks.List(null, true, true).ToDictionary(t => t.Id, t => t.Parent);
        return c.ParentById.TryGetValue(id, out var parent) ? parent is null : tasks.Get(id) is { Parent: null };
    }

    private LocalValue LocalValues(Ctx c, Local l, string listId)
    {
        var (text, ok) = NotesFromBody(l.N.Body);
        // Only a leaf task under a top-level linked task is given a Google parent.
        string? parent = null;
        if (l.T.SubtaskCount == 0 && l.T.Parent is { } p && c.S.Links.TryGetValue(p, out var pl) && pl.ListId == listId
            && IsTopLevel(c, p)) parent = p;
        return new LocalValue(NormTitle(l.T.Title), text, Hash(text), ok, LocalDue(l.T.Due), IsClosed(l.T.Status), parent);
    }

    private string? ParentOfGoogle(Ctx c, string? gparent, string listId) =>
        gparent is null ? null : c.S.Links.FirstOrDefault(kv => kv.Value.ListId == listId && kv.Value.Gid == gparent).Key;

    private static Act Decide(bool changedL, bool changedG, bool equal) =>
        !changedL && !changedG ? Act.None
        : changedG && !changedL ? Act.Pull
        : changedL && !changedG ? Act.Push
        : equal ? Act.Settle : Act.Conflict;

    private async Task MergePair(Ctx c, GoogleListMapping m, Local l, GoogleLink link, GTask? g, Dictionary<string, Local> locals, Dictionary<string, string> byGid, CancellationToken ct)
    {
        var b = link.Base;
        var v = LocalValues(c, l, m.ListId);
        var live = g is not null;
        var gTitle = live ? NormTitle(g!.Title) : b.Title.G ?? Untitled;
        var gNotesHash = live ? Hash(NormNotes(g!.Notes)) : b.Notes.G ?? Hash("");
        var gNotesFit = !live || NotesFitFromGoogle(g!.Notes);
        var gDue = live ? g!.Due : b.Due.G;
        var gDone = live ? g!.Completed : b.Done.G;
        var gParent = live ? g!.Parent : b.Parent.G;
        var gParentTask = ParentOfGoogle(c, gParent, m.ListId);

        var title = Decide(v.Title != b.Title.L, gTitle != b.Title.G, Truncate(v.Title) == gTitle);
        var notesActive = v.NotesOk && gNotesFit;
        var notes = notesActive ? Decide(v.NotesHash != b.Notes.L, gNotesHash != b.Notes.G, v.NotesHash == gNotesHash) : Act.None;
        var due = Decide(v.Due != b.Due.L, gDue != b.Due.G, v.Due == gDue);
        var done = Decide(v.Done != b.Done.L, gDone != b.Done.G, v.Done == gDone);
        var parent = Decide(v.Parent != b.Parent.L, gParent != b.Parent.G, v.Parent == gParentTask);
        c.Conflicts += new[] { title, notes, due, done, parent }.Count(a => a == Act.Conflict);
        bool Push(Act a) => a is Act.Push or Act.Conflict;

        // ---- pull into the task: one coalesced write, then the title ----
        var locked = l.N.Locked || l.N.ReadOnly;
        var pulls = new[] { title, notes, due, done, parent }.Any(a => a == Act.Pull);
        var applied = new HashSet<string>();
        if (pulls && locked) { c.Skipped++; Hold(c, m.ListId, g); }
        else if (pulls)
        {
            var set = new Dictionary<string, object?>();
            var remove = new List<string>();
            string? body = null;
            if (notes == Act.Pull) body = NormNotes(g!.Notes);
            if (due == Act.Pull) { if (gDue is null) remove.Add(TaskKeys.Due); else if (LocalDue(l.T.Due) != gDue) set[TaskKeys.Due] = gDue; }
            if (done == Act.Pull)
            {
                if (gDone) { set[TaskKeys.Status] = TaskStatus.Done; set[TaskKeys.Completed] = live && DateTimeOffset.TryParse(g!.CompletedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? clock.Format(at) : clock.Stamp(); }
                else { set[TaskKeys.Status] = TaskStatus.Todo; remove.Add(TaskKeys.Completed); }
            }
            var parentApplicable = true;
            if (parent == Act.Pull)
            {
                if (gParent is null) remove.Add(TaskKeys.Parent);
                else if (gParentTask is { } target && target != l.T.Id && locals.TryGetValue(target, out var tl) && tl.T.Parent is null && l.T.SubtaskCount == 0) set[TaskKeys.Parent] = target;
                else { parentApplicable = false; b.Parent.G = gParent; c.Skipped++; }
            }
            var saved = true;
            if (set.Count > 0 || remove.Count > 0 || body is not null)
            {
                var r = writes.UpdateFields(l.T.Id, l.N.Hash, set, remove, GoogleActor, body);
                saved = r.Status is SaveStatus.Saved or SaveStatus.Unchanged;
                if (!saved) { c.Skipped++; Hold(c, m.ListId, g); }
            }
            if (saved)
            {
                if (notes == Act.Pull) applied.Add("notes");
                if (due == Act.Pull) applied.Add("due");
                if (done == Act.Pull) applied.Add("done");
                if (parent == Act.Pull && parentApplicable)
                {
                    applied.Add("parent");
                    if (c.ParentById is not null) c.ParentById[l.T.Id] = set.TryGetValue(TaskKeys.Parent, out var newParent) ? Props.Str(newParent) : null;
                }
            }
            if (title == Act.Pull && saved) Refresh(l);
            if (title == Act.Pull)
            {
                var renamed = writes.Exclusive(() =>
                {
                    var cur = writes.Get(l.T.Id);
                    if (cur is null || NormTitle(cur.Title) != b.Title.L) return false; // the person retitled it meanwhile: theirs wins
                    try { writes.Rename(l.T.Id, gTitle, GoogleActor); return true; } catch (QuantosException) { return false; }
                });
                if (renamed) applied.Add("title"); else { c.Skipped++; Hold(c, m.ListId, g); }
            }
            if (applied.Count > 0) c.Pulled++;
            Refresh(l);
        }

        // ---- push to Google ----
        var write = new GTaskWrite();
        var wrote = false;
        if (Push(title)) { write = write with { Title = Optional<string?>.Of(Truncate(v.Title)) }; wrote = true; }
        if (Push(notes)) { write = write with { Notes = Optional<string?>.Of(v.NotesText.Length == 0 ? null : v.NotesText) }; wrote = true; }
        if (Push(due)) { write = write with { Due = Optional<string?>.Of(v.Due) }; wrote = true; }
        if (Push(done)) { write = write with { Completed = Optional<bool>.Of(v.Done) }; wrote = true; }
        GTask? result = g;
        if (wrote) result = await api.Patch(link.ListId, link.Gid, write, ct);
        var moved = false;
        if (Push(parent))
        {
            var targetGid = v.Parent is not null && c.S.Links.TryGetValue(v.Parent, out var pl) ? pl.Gid : null;
            if (v.Parent is null || targetGid is not null)
            {
                // A 404 here may be the new parent (deleted in Google), not this task: skip the move and try again later.
                try { result = await api.Move(link.ListId, link.Gid, targetGid, ct); moved = true; }
                catch (GoogleApiException ex) when (ex.Status is 404 or 410) { c.Skipped++; }
            }
        }
        if (wrote || moved) c.Pushed++;

        // ---- new base: each side as it now is ----
        var nowL = LocalValues(c, l, m.ListId);
        result ??= g;
        GTask? rg = result;
        void Set(Act a, string name, Action fromBoth, Action pulledOrNone)
        {
            if (a is Act.Settle or Act.Push or Act.Conflict) fromBoth();
            else if (a == Act.Pull && applied.Contains(name)) pulledOrNone();
        }
        if (rg is not null)
        {
            Set(title, "title", () => { b.Title.L = v.Title; b.Title.G = NormTitle(rg.Title); }, () => { b.Title.L = nowL.Title; b.Title.G = gTitle; });
            if (notesActive) Set(notes, "notes", () => { b.Notes.L = v.NotesHash; b.Notes.G = Hash(NormNotes(rg.Notes)); }, () => { b.Notes.L = nowL.NotesHash; b.Notes.G = gNotesHash; });
            Set(due, "due", () => { b.Due.L = v.Due; b.Due.G = rg.Due; }, () => { b.Due.L = nowL.Due; b.Due.G = gDue; });
            Set(done, "done", () => { b.Done.L = v.Done; b.Done.G = rg.Completed; }, () => { b.Done.L = nowL.Done; b.Done.G = gDone; });
            if (parent is Act.Push or Act.Conflict) { if (moved) { b.Parent.L = v.Parent; b.Parent.G = rg.Parent; } }
            else if (parent == Act.Settle) { b.Parent.L = v.Parent; b.Parent.G = gParent; }
            else if (parent == Act.Pull && applied.Contains("parent")) { b.Parent.L = nowL.Parent; b.Parent.G = gParent; }
            link.GoogleUpdated = rg.Updated;
        }
    }

    // ---- deletes ------------------------------------------------------------------------------

    private static void Queue(Ctx c, string taskId, GoogleLink link, string? title, DeleteDirection direction)
    {
        if (c.NewPending.Any(p => p.TaskId == taskId) || c.S.PendingDeletes.Any(p => p.TaskId == taskId)) return;
        c.NewPending.Add(new PendingDelete { TaskId = taskId, Gid = link.Gid, ListId = link.ListId, Title = title ?? "", Direction = direction });
    }

    private async Task ProcessDeletes(Ctx c, IReadOnlyList<GoogleListMapping> mappings, CancellationToken ct)
    {
        var lists = mappings.Select(m => m.ListId).ToHashSet();
        // Quantos -> Google: a linked task that is gone from the vault
        foreach (var (taskId, link) in c.S.Links.Where(kv => lists.Contains(kv.Value.ListId)).ToList())
        {
            if (writes.Get(taskId) is not null) { link.Missing = 0; continue; }
            if (link.Suspended) { c.S.Links.Remove(taskId); continue; } // a suspended task never causes a Google delete
            if (c.TrashIds.Contains(taskId)) { Queue(c, taskId, link, null, DeleteDirection.ToGoogle); continue; }
            if (++link.Missing >= MissingCyclesBeforeDelete) Queue(c, taskId, link, null, DeleteDirection.ToGoogle);
        }
        var heldBefore = c.S.PendingDeletes.Count(p => p.Held);
        c.S.PendingDeletes.AddRange(c.NewPending);
        var threshold = Math.Max(10.0, c.S.Links.Count * 0.25);
        // Deletions already waiting for the person keep everything new waiting too; otherwise a big batch is held and a small one runs.
        // Entries left over from a transient failure are not held for the person: they run again with the next batch.
        // Counted over everything waiting, not only this run's: leftovers of repeated transient failures cannot pile up past the limit unseen.
        if (heldBefore > 0 || c.S.PendingDeletes.Count > threshold)
        {
            // Everything waiting becomes visible to the person (including leftovers of an earlier failure): Apply runs exactly what is listed.
            foreach (var p in c.S.PendingDeletes) p.Held = true;
            return;
        }
        await ExecuteDeletes(c, c.S.PendingDeletes.ToList(), ct);
    }

    private Dictionary<string, List<string>> HintOwners()
    {
        var owners = new Dictionary<string, List<string>>();
        foreach (var t in tasks.List(null, true, true))
        {
            if (writes.Get(t.Id) is not { } n || HintOf(n) is not { Id: not null } h) continue;
            var key = h.List + "|" + h.Id;
            if (!owners.TryGetValue(key, out var list)) owners[key] = list = [];
            list.Add(t.Id);
        }
        return owners;
    }

    private async Task ExecuteDeletes(Ctx c, List<PendingDelete> entries, CancellationToken ct)
    {
        if (entries.Count == 0) return;
        c.TrashIds = Trashed();
        var owners = HintOwners();
        // Google-side deletions of children go before their parents
        foreach (var d in entries.OrderBy(e => e.Direction == DeleteDirection.ToQuantos && writes.Get(e.TaskId) is { } n && tasks.Get(n.Id)?.Parent is not null ? 0 : 1))
        {
            ct.ThrowIfCancellationRequested();
            if (c.S.Links.TryGetValue(d.TaskId, out var link))
            {
                try
                {
                    if (d.Direction == DeleteDirection.ToQuantos) ExecuteToQuantos(c, d, link);
                    else await ExecuteToGoogle(c, d, link, owners, ct);
                }
                catch (GoogleApiException ex) when (IsTaskLevel(ex)) { c.Skipped++; }
            }
            // Handled (done, or dropped because it no longer applies): only now does it leave the held list.
            c.S.PendingDeletes.RemoveAll(p => p.TaskId == d.TaskId);
            store.Save(c.S);
        }
    }

    private void ExecuteToQuantos(Ctx c, PendingDelete d, GoogleLink link)
    {
        var node = writes.Get(d.TaskId);
        if (node is null) { c.S.Links.Remove(d.TaskId); return; }
        var dto = tasks.Get(d.TaskId);
        var untouched = dto is not null && NormTitle(dto.Title) == link.Base.Title.L && IsClosed(dto.Status) == link.Base.Done.L
            && LocalDue(dto.Due) == link.Base.Due.L && Hash(NotesFromBody(node.Body).Text) == link.Base.Notes.L;
        if (dto is not null && Pristine(node) && dto.SubtaskCount == 0 && untouched)
        {
            try { writes.Trash(d.TaskId, GoogleActor); c.Deleted++; }
            catch (QuantosException) { c.Skipped++; WriteHints(d.TaskId, null, null); } // could not be trashed: keep it, unlinked
        }
        else { WriteHints(d.TaskId, null, null); c.Skipped++; }
        c.S.Links.Remove(d.TaskId);
    }

    private async Task ExecuteToGoogle(Ctx c, PendingDelete d, GoogleLink link, Dictionary<string, List<string>> owners, CancellationToken ct)
    {
        if (writes.Get(d.TaskId) is not null) { c.S.Links.Remove(d.TaskId); return; } // it came back
        if (owners.TryGetValue(d.ListId + "|" + d.Gid, out var others) && others.FirstOrDefault(o => o != d.TaskId) is { } owner)
        {
            c.S.Links.Remove(d.TaskId);
            c.S.Links[owner] = link; // another live task carries this Google task: keep it, delete nothing
            return;
        }
        var google = await api.Get(d.ListId, d.Gid, ct);
        if (google is null || google.Deleted) { c.S.Links.Remove(d.TaskId); return; }
        var b = link.Base;
        var edited = NormTitle(google.Title) != b.Title.G || google.Due != b.Due.G || google.Completed != b.Done.G || google.Parent != b.Parent.G
            || (NotesFitFromGoogle(google.Notes) && Hash(NormNotes(google.Notes)) != b.Notes.G);
        if (edited)
        {
            // keep the edited Google task; it returns as a new task
            c.S.Links.Remove(d.TaskId); c.S.Cursors[d.ListId] = null; c.Conflicts++;
            return;
        }
        await api.Delete(d.ListId, d.Gid, ct);
        c.S.Links.Remove(d.TaskId); // only after Google confirmed, so a failed attempt can be retried
        c.Deleted++;
    }

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>Errors about a single task. Rate limits, auth and a missing API affect the whole run and are rethrown.</summary>
    private static bool IsTaskLevel(GoogleApiException ex) =>
        ex.Status is >= 400 and < 500 and not 401 and not 429
        && ex.Reason is not ("rateLimitExceeded" or "userRateLimitExceeded" or "quotaExceeded" or "insufficientPermissions" or "accessNotConfigured" or "SERVICE_DISABLED");

    private static readonly TimeSpan MaxHold = TimeSpan.FromDays(1);

    /// <summary>
    /// Keeps the cursor before a Google change that could not be applied so it is fetched again. A change still failing a day after
    /// it first failed (a task that stays locked, one Google keeps rejecting) is let go, so it cannot pin the cursor forever.
    /// </summary>
    private void Hold(Ctx c, string listId, GTask? g)
    {
        if (g is null || !DateTimeOffset.TryParse(g.Updated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)) return;
        var key = listId + "|" + g.Id;
        c.HeldThisRun.Add(key);
        if (!c.S.HoldSince.TryGetValue(key, out var since)) c.S.HoldSince[key] = since = clock.UtcNow;
        if (clock.UtcNow - since > MaxHold) return;
        if (!c.HoldCursor.TryGetValue(listId, out var existing) || at < existing) c.HoldCursor[listId] = at;
    }

    private void SaveBatch(Ctx c)
    {
        if (++c.Operations % BatchSave == 0) store.Save(c.S);
    }
}
