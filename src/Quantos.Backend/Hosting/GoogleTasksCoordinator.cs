using Quantos.Core;
using Quantos.Core.GoogleTasks;
using Quantos.Core.Storage;

namespace Quantos.Backend.Hosting;

public sealed record GoogleMappingBody(string Workspace, string ListId, string ListTitle);
public sealed record GoogleSettingsBody(string? ClientId, string? ClientSecret, int? IntervalSeconds, List<GoogleMappingBody>? Mappings);
public sealed record GoogleWorkspaceBody(string Workspace);
public sealed record GoogleConfirmBody(string Workspace, DateTimeOffset Cutoff);
public sealed record GoogleListBody(string Title);
public sealed record GoogleDeletesBody(string Action);

/// <summary>Builds the real Google Tasks client; tests replace this to supply a fake.</summary>
public sealed class GoogleTasksApiFactory(HttpClient http, GoogleOAuth oauth, GoogleTasksSettingsStore settings) : IGoogleTasksApiFactory
{
    private readonly GoogleTokenSource _tokens = new(oauth, settings.Load);
    public IGoogleTasksApi Create() => new GoogleTasksApi(http, _tokens);
    public void Forget() => _tokens.Forget();
}

/// <summary>
/// Runs the Google Tasks sync for the open vault: the sign-in flow, the settings, the timer and the debounce on local changes.
/// The person's own edits always win; a failure only pauses this worker.
/// </summary>
public sealed class GoogleTasksCoordinator : BackgroundService
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(5);
    private const string StateKept = "Quantos couldn't delete its sync records on this device. If you connect again they will be used again; delete the google-tasks folder in Quantos's client data to start clean.";
    private const string RevokeFailed = "Quantos couldn't reach Google to revoke its access. You can remove it at myaccount.google.com/permissions.";
    private readonly VaultHost host;
    private readonly GoogleOAuth oauth;
    private readonly IGoogleTasksApiFactory apis;
    private readonly GoogleTasksSettingsStore settings;
    private readonly ServerAddress address;
    private readonly BuiltInGoogleClient builtIn;
    private readonly SemaphoreSlim gate = new(1);
    private readonly SemaphoreSlim kick = new(0, 1);
    private readonly object sync = new(); // guards config read-modify-write, previews and the debounce timer
    private readonly object subLock = new(); // guards only the change subscription, so Kick() (called on the Android UI thread) never waits behind file work
    private readonly Dictionary<(string Workspace, string ListId), DateTimeOffset> previews = [];
    private CancellationTokenSource? active;
    private Timer? debounce;
    private VaultSession? subscribed;
    private Action<ChangeEvent>? handler;
    private DateTimeOffset? lastSynced, nextAttempt;
    private string? error, notice;
    private volatile bool needsReconnect, apiDisabled;
    private int connectionEpoch; // bumped when the sign-in is being replaced or removed, so a sign-in already in flight is not stored
    private volatile bool configured; // a refresh token and a confirmed list exist: only then is a vault change worth a sync, so others cost nothing
    private int disconnecting;   // number of Disconnects running: while above zero no new sign-in may start or be stored (a count, so overlapping ones do not clear it early)
    private int failures;
    private GoogleSyncReport? last;
    private (int Linked, List<PendingDelete> Pending)? snapshot;

    public GoogleTasksCoordinator(VaultHost host, GoogleOAuth oauth, IGoogleTasksApiFactory apis, GoogleTasksSettingsStore settings, ServerAddress address, BuiltInGoogleClient builtIn)
    {
        this.host = host; this.oauth = oauth; this.apis = apis; this.settings = settings; this.address = address; this.builtIn = builtIn;
    }

    // ---- status ---------------------------------------------------------------------------------

    private string RedirectUri => address.Url.TrimEnd('/') + "/oauth/google/callback";
    private static bool HasRefreshToken(GoogleTasksConfig c) => GoogleTasksSettingsStore.Unseal(c.ProtectedRefreshToken) is not null;
    private static bool HasSecret(GoogleTasksConfig c) => GoogleTasksSettingsStore.Unseal(c.ProtectedClientSecret) is not null;

    private bool VaultMatches(GoogleTasksConfig c) =>
        host.Session is { } s && (c.Vault is null || Vault.PathEquals(s.Vault.Root, c.Vault));

    /// <summary>
    /// When this copy has a Google client built in and none is saved (first use, or after a Disconnect), saves it, so everything else works
    /// from the saved settings exactly as if the person had pasted it. A client the person saved themselves is never replaced.
    /// </summary>
    private void SeedBuiltInClient()
    {
        if (!builtIn.Exists) return;
        lock (sync)
        {
            if (disconnecting > 0) return;
            var c = settings.Load();
            if (!string.IsNullOrEmpty(c.ClientId) && !string.IsNullOrEmpty(c.ProtectedClientSecret)) return;
            try { settings.Save(c with { ClientId = builtIn.Id, ProtectedClientSecret = GoogleTasksSettingsStore.Seal(builtIn.Secret!) }); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or QuantosException) { }
        }
    }

    /// <summary>Applies a change to the saved settings without losing a concurrent one.</summary>
    private void UpdateConfig(Func<GoogleTasksConfig, GoogleTasksConfig> change)
    {
        lock (sync) { var next = change(settings.Load()); settings.Save(next); configured = !string.IsNullOrEmpty(next.ProtectedRefreshToken) && next.Mappings.Any(m => m.Confirmed); }
    }

    private int snapshotVersion;
    private void Invalidate() { lock (sync) { snapshot = null; snapshotVersion++; } }

    /// <summary>Link count and held deletions, read from state.json once and kept until something changes it.</summary>
    private (int Linked, List<PendingDelete> Pending) Snapshot()
    {
        int version;
        lock (sync)
        {
            if (snapshot is { } cached) return cached;
            version = snapshotVersion;
        }
        // The file is read outside the lock: vault changes and Connect take it, and a slow or locked state file must not stall them.
        var state = StateStore().Load();
        var value = (state.Links.Count, state.PendingDeletes.Where(p => p.Held).ToList());
        lock (sync) { if (version == snapshotVersion) snapshot = value; } // not stored if something changed the state meanwhile
        return value;
    }

    public object Status()
    {
        SeedBuiltInClient();
        var c = settings.Load();
        string state;
        string? reason = null;
        if (string.IsNullOrEmpty(c.ProtectedRefreshToken)) state = "Off";
        else if (needsReconnect || !HasRefreshToken(c) || !HasSecret(c)) state = "NeedsReconnect";
        else if (!VaultMatches(c)) { state = "Paused"; reason = "Open the vault this was set up for"; }
        else state = "Connected";
        if (state == "Connected" && apiDisabled) reason = "Turn on the Google Tasks API for your Google Cloud project";
        var mappings = c.Mappings.Select(m => new
        {
            workspace = m.Workspace, listId = m.ListId, listTitle = m.ListTitle, confirmed = m.Confirmed,
            state = m.Confirmed ? "Ok" : "Paused", reason = m.Confirmed ? (string?)null : "Review the preview first"
        }).ToList();
        var linked = 0;
        var pending = new List<object>();
        try
        {
            // Nothing to read while disconnected; and an unreadable state file (held open, damaged) only hides the counts, it never fails the status.
            if (state != "Off" && host.Session is not null && VaultMatches(c))
            {
                var s = Snapshot();
                linked = s.Linked;
                pending.AddRange(s.Pending.Select(d => new { taskId = d.TaskId, title = d.Title, direction = d.Direction == DeleteDirection.ToGoogle ? "toGoogle" : "toQuantos" }));
            }
        }
        catch (Exception ex) when (ex is QuantosException or IOException or UnauthorizedAccessException) { }
        return new
        {
            state, reason, builtInClient = builtIn.Exists, hasCredentials = !string.IsNullOrEmpty(c.ClientId) && !string.IsNullOrEmpty(c.ProtectedClientSecret),
            flow = oauth.FlowPending ? "waiting" : "idle", mappings,
            intervalSeconds = c.IntervalSeconds, lastSynced, nextAttempt, error, notice,
            counts = new { linked, pushed = last?.Pushed ?? 0, pulled = last?.Pulled ?? 0, skipped = last?.Skipped ?? 0, pendingDeletes = pending.Count },
            pendingDeletes = pending,
            problems = last?.Problems.Select(p => new { workspace = p.Workspace, reason = p.Reason }).ToList()
        };
    }

    // ---- settings -------------------------------------------------------------------------------

    public async Task Configure(GoogleSettingsBody body, CancellationToken ct)
    {
        var session = host.Require();
        var old = settings.Load();
        if (old.Vault is not null && !Vault.PathEquals(old.Vault, session.Vault.Root)) throw new QuantosException("Open the vault this was set up for, or disconnect first.", 409);
        if (body.IntervalSeconds is { } secs && secs is < 30 or > 900) throw new QuantosException("The sync interval must be between 30 and 900 seconds.");

        // A blank client ID means "unchanged", like an omitted one.
        var newClientId = string.IsNullOrWhiteSpace(body.ClientId) ? null : body.ClientId.Trim();
        var credentialsChanged = newClientId is not null && newClientId != (old.ClientId ?? "") || !string.IsNullOrWhiteSpace(body.ClientSecret);
        // A different client ID with the old secret can never sign in: ask for the secret now instead of failing later.
        if (newClientId is not null && !string.IsNullOrEmpty(old.ClientId) && newClientId != old.ClientId && string.IsNullOrWhiteSpace(body.ClientSecret))
            throw new QuantosException("Enter the client secret that belongs to this new client ID.");
        (List<GoogleListMapping>? Kept, List<GoogleListMapping> Removed) Plan(GoogleTasksConfig from)
        {
            if (body.Mappings is not { } rows) return (null, []);
            var keptRows = rows.Select(m => from.Mappings.FirstOrDefault(o => o.Workspace == m.Workspace && o.ListId == m.ListId) is { } existing
                ? existing with { ListTitle = m.ListTitle } : new GoogleListMapping(m.Workspace, m.ListId, m.ListTitle)).ToList();
            return (keptRows, from.Mappings.Where(o => !keptRows.Any(k => k.Workspace == o.Workspace && k.ListId == o.ListId)).ToList());
        }
        if (body.Mappings is { } incoming)
        {
            var names = session.Vault.WorkspaceNames().ToHashSet();
            var seenW = new HashSet<string>(); var seenL = new HashSet<string>();
            foreach (var m in incoming)
            {
                if (string.IsNullOrWhiteSpace(m.Workspace) || string.IsNullOrWhiteSpace(m.ListId)) throw new QuantosException("Choose a workspace and a Google list for each row.");
                if (!names.Contains(m.Workspace)) throw new QuantosException($"There is no workspace called “{m.Workspace}”.");
                if (!seenW.Add(m.Workspace)) throw new QuantosException($"“{m.Workspace}” is used twice.");
                if (!seenL.Add(m.ListId)) throw new QuantosException($"“{m.ListTitle}” is used twice.");
            }
        }

        // Anything that rewrites the sync state waits for a running sync to stop first, so a late save cannot undo it.
        var exclusive = credentialsChanged || Plan(old).Removed.Count > 0;
        if (credentialsChanged) lock (sync) connectionEpoch++; // a sign-in already past its token exchange must not be stored
        if (exclusive)
        {
            CancelSync();
            if (credentialsChanged) oauth.CancelFlow();
            await gate.WaitAsync(ct);
            if (credentialsChanged) oauth.CancelFlow(); // a Connect that arrived during the wait started a flow with the old client ID
        }
        try
        {
            // Settings may have changed while waiting for the gate (a Confirm, a finished sign-in): work from what is saved now.
            var fresh = settings.Load();
            var (_, removed) = Plan(fresh);
            if (!exclusive && removed.Count > 0)
            {
                // A concurrent change made a removal necessary after all: take the gate now (and stop any running sync) before touching state.
                CancelSync();
                await gate.WaitAsync(ct);
                exclusive = true;
                fresh = settings.Load();
                (_, removed) = Plan(fresh);
            }
            string? oldRefresh = null; // read under the settings lock below, so a sign-in that finished meanwhile is revoked too
            if (removed.Count > 0)
            {
                var engine = NewEngine(session);
                foreach (var r in removed) engine.ResetMapping(r.Workspace, r.ListId);
                lock (sync) foreach (var r in removed) previews.Remove((r.Workspace, r.ListId));
                Invalidate();
            }
            UpdateConfig(c =>
            {
                if (disconnecting > 0) throw new QuantosException("Google Tasks is being disconnected. Try again in a moment.", 409); // do not recreate settings behind a Disconnect
                // Under the same lock Connect uses: a Connect before this saw the old credentials and its flow is cancelled here;
                // one after it sees the new credentials and keeps its flow.
                if (credentialsChanged) oauth.CancelFlow();
                var next = c with { Vault = session.Vault.Root };
                if (body.IntervalSeconds is { } interval) next = next with { IntervalSeconds = interval };
                if (newClientId is not null && newClientId != (c.ClientId ?? "")) next = next with { ClientId = newClientId };
                if (!string.IsNullOrWhiteSpace(body.ClientSecret)) next = next with { ProtectedClientSecret = GoogleTasksSettingsStore.Seal(body.ClientSecret.Trim()) };
                if (credentialsChanged) { oldRefresh = GoogleTasksSettingsStore.Unseal(c.ProtectedRefreshToken); next = next with { ProtectedRefreshToken = null }; }
                // Built from the settings as they are under the lock now, so a Confirm that landed meanwhile keeps its Confirmed and cutoff.
                if (Plan(c).Kept is { } keptNow) next = next with { Mappings = keptNow };
                return next;
            });
            if (credentialsChanged)
            {
                notice = null;
                apis.Forget(); // after the new settings are saved, so a refresh in flight cannot cache the old account's token
                needsReconnect = false;
                if (oldRefresh is not null && !await oauth.Revoke(oldRefresh, ct)) notice = RevokeFailed;
            }
            error = null;
        }
        finally
        {
            if (exclusive) { gate.Release(); Kick(); } // a sync stopped by this change should start again now, not after a whole interval
        }
    }

    // ---- sign-in --------------------------------------------------------------------------------

    public object Connect()
    {
        // Checked, read and started under one lock, so a Disconnect or a credential change cannot slip in between and leave a flow with no settings.
        lock (sync)
        {
            if (disconnecting > 0) throw new QuantosException("Google Tasks is being disconnected. Try again in a moment.", 409);
            SeedBuiltInClient();
            var c = settings.Load();
            var secret = GoogleTasksSettingsStore.Unseal(c.ProtectedClientSecret);
            if (string.IsNullOrEmpty(c.ClientId) || secret is null) throw new QuantosException("Save the client ID and client secret first.", 409);
            needsReconnect = false;
            return new { authUrl = oauth.StartFlow(c.ClientId, RedirectUri) };
        }
    }

    /// <summary>Called by the anonymous callback route. Returns the HTTP status and message for the static page.</summary>
    public async Task<(int Status, string Message)> CompleteSignIn(string? code, string? state, CancellationToken ct)
    {
        int epoch;
        lock (sync) epoch = connectionEpoch;
        var c = settings.Load();
        var secret = GoogleTasksSettingsStore.Unseal(c.ProtectedClientSecret);
        if (string.IsNullOrEmpty(c.ClientId) || secret is null) return (400, "Quantos has no saved client ID and secret.");
        // Not tied to the browser request: the flow and code are already spent once the state matches, so a closed tab must not abandon the exchange.
        var (result, tokens, message) = await oauth.Complete(code, state, c.ClientId, secret, CancellationToken.None);
        return await Finish(result, tokens, message, c, epoch);
    }

    /// <summary>
    /// Finishes a sign-in whose code was kept because the app was in the background (Android blocks its network there).
    /// Called when the app comes back to the front; does nothing when there is nothing waiting.
    /// </summary>
    public async Task ResumeSignIn()
    {
        if (!oauth.HasDeferred) return;
        int epoch;
        lock (sync) epoch = connectionEpoch;
        var c = settings.Load();
        var secret = GoogleTasksSettingsStore.Unseal(c.ProtectedClientSecret);
        if (string.IsNullOrEmpty(c.ClientId) || secret is null) return;
        // Right after the app is brought to the front the network may not be allowed yet, so try a few times.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var (result, tokens, message) = await oauth.CompleteDeferred(c.ClientId, secret, CancellationToken.None);
            if (result == GoogleOAuth.CallbackResult.NoFlow) return;
            await Finish(result, tokens, message, c, epoch);
            if (result != GoogleOAuth.CallbackResult.Retry) return;
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    private async Task<(int Status, string Message)> Finish(GoogleOAuth.CallbackResult result, GoogleTokens? tokens, string? message, GoogleTasksConfig c, int epoch)
    {
        switch (result)
        {
            case GoogleOAuth.CallbackResult.Retry:
                return (200, "Almost done. Switch back to Quantos and it will finish connecting to Google Tasks.");
            case GoogleOAuth.CallbackResult.Completed:
                // The exchange can take a while. If the credentials were changed, or Quantos was disconnected, meanwhile, the token belongs
                // to a client that is no longer configured: discard it (and best-effort revoke it) instead of storing a mismatched pair.
                bool stored;
                lock (sync)
                {
                    var now = settings.Load();
                    stored = epoch == connectionEpoch && disconnecting == 0 && !string.IsNullOrEmpty(now.ClientId) && now.ClientId == c.ClientId && now.ProtectedClientSecret == c.ProtectedClientSecret;
                    if (stored) { settings.Save(now with { ProtectedRefreshToken = GoogleTasksSettingsStore.Seal(tokens!.RefreshToken!) }); configured = now.Mappings.Any(m => m.Confirmed); }
                }
                if (!stored)
                {
                    // Not tied to the browser request: a closed tab must not leave the unused grant live. If Google cannot be reached, say so.
                    if (!await oauth.Revoke(tokens!.RefreshToken!, CancellationToken.None)) notice = RevokeFailed;
                    return (400, "The settings changed while you were signing in. Go back to Quantos and press Connect again.");
                }
                apis.Forget(); // a new sign-in may be a different Google account; forgotten after the new token is saved
                needsReconnect = false; apiDisabled = false; error = null; notice = null; failures = 0; nextAttempt = null;
                Kick();
                return (200, "Google Tasks is connected. You can close this tab and go back to Quantos.");
            case GoogleOAuth.CallbackResult.Failed:
                error = message;
                return (400, message ?? "Google sign-in failed.");
            default:
                return (400, "This sign-in link is not valid any more. Go back to Quantos and press Connect again.");
        }
    }

    public async Task<IReadOnlyList<GTaskList>> Lists(CancellationToken ct)
    {
        RequireConnected();
        return await Guarded(() => apis.Create().ListTaskLists(ct));
    }

    public async Task<GTaskList> CreateList(string title, CancellationToken ct)
    {
        RequireConnected();
        if (string.IsNullOrWhiteSpace(title)) throw new QuantosException("Give the list a name.");
        return await Guarded(() => apis.Create().CreateTaskList(title.Trim(), ct));
    }

    private void RequireConnected()
    {
        var c = settings.Load();
        if (string.IsNullOrEmpty(c.ProtectedRefreshToken) || needsReconnect) throw new QuantosException("Connect Google first.", 409);
    }

    // ---- preview, confirm, sync -----------------------------------------------------------------

    private static GoogleListMapping Mapping(GoogleTasksConfig c, string workspace) =>
        c.Mappings.FirstOrDefault(m => m.Workspace == workspace) ?? throw new QuantosException("That workspace is not in the saved lists.");

    public async Task<GoogleSyncPreview> Preview(string workspace, CancellationToken ct)
    {
        RequireConnected();
        var current = settings.Load();
        if (!VaultMatches(current)) throw new QuantosException("Open the vault this was set up for.", 409);
        var m = Mapping(current, workspace);
        var session = host.Require();
        var preview = await Guarded(() => NewEngine(session).Preview(m, ct));
        lock (sync) previews[(m.Workspace, m.ListId)] = preview.Cutoff;
        return preview;
    }

    public void Confirm(string workspace, DateTimeOffset cutoff)
    {
        var current = settings.Load();
        if (!VaultMatches(current)) throw new QuantosException("Open the vault this was set up for.", 409);
        var m = Mapping(current, workspace);
        lock (sync)
        {
            if (!previews.TryGetValue((m.Workspace, m.ListId), out var remembered)) throw new QuantosException("Run Preview first.", 409);
            if ((remembered - cutoff).Duration() > TimeSpan.FromHours(1)) throw new QuantosException("Preview again: that preview is out of date.", 409);
            previews.Remove((m.Workspace, m.ListId));
        }
        UpdateConfig(c => c with { Mappings = c.Mappings.Select(x => x.Workspace == m.Workspace && x.ListId == m.ListId ? x with { Confirmed = true, ImportCutoff = cutoff } : x).ToList() });
    }

    private GoogleTasksSyncEngine NewEngine(VaultSession session) =>
        new(session.Writes, session.Tasks, session.Clock, StateStore(session), apis.Create());

    private IGoogleTasksStateStore StateStore() => StateStore(host.Require());
    private IGoogleTasksStateStore StateStore(VaultSession session) =>
        new FileGoogleTasksStateStore(Path.Combine(host.Paths.ClientData, "google-tasks", FileGoogleTasksStateStore.VaultKey(session.Vault.Root)));

    /// <summary>Runs one sync and waits its turn behind any run already in flight.</summary>
    public async Task<GoogleSyncReport?> SyncNow(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        CancellationTokenSource? cancellation = null;
        try
        {
            lock (sync) if (disconnecting > 0) throw new QuantosException("Google Tasks is being disconnected.", 409); // a run queued behind a Disconnect must not start
            var c = settings.Load();
            if (string.IsNullOrEmpty(c.ProtectedRefreshToken) || needsReconnect) throw new QuantosException("Connect Google first.", 409);
            // The session is read once and the same one is checked and used, so a vault switch in between cannot run the sync on the wrong vault.
            var session = host.Session;
            if (session is null || !(c.Vault is null || Vault.PathEquals(session.Vault.Root, c.Vault))) throw new QuantosException("Open the vault this was set up for.", 409);
            var confirmed = c.Mappings.Where(m => m.Confirmed).ToList();
            if (confirmed.Count == 0) throw new QuantosException("Review the preview first.", 409);
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            active = cancellation;
            var report = await Guarded(() => NewEngine(session).Run(confirmed, cancellation.Token));
            last = report; lastSynced = DateTimeOffset.Now; error = null; failures = 0; nextAttempt = null; apiDisabled = false;
            return report;
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true && !ct.IsCancellationRequested) { return null; }
        finally { active = null; cancellation?.Dispose(); Invalidate(); gate.Release(); }
    }

    /// <summary>Maps Google failures to the status the person sees. Auth problems stop sync; limits back off; the rest are reported.</summary>
    private async Task<T> Guarded<T>(Func<Task<T>> work)
    {
        try { return await work(); }
        catch (GoogleAuthException ex) { needsReconnect = true; error = ex.Message; throw new QuantosException(ex.Message, 409); }
        catch (GoogleApiException ex) when (ex.Reason is "insufficientPermissions" or "PERMISSION_DENIED" || ex.Status == 403 && ex.Reason is "forbidden")
        { needsReconnect = true; error = "Google Tasks access was not granted. Connect again."; throw new QuantosException(error, 409); }
        catch (GoogleApiException ex) when (ex.Reason is "accessNotConfigured" or "SERVICE_DISABLED")
        { apiDisabled = true; error = "Turn on the Google Tasks API for your Google Cloud project."; throw new QuantosException(error, 409); }
        catch (GoogleApiException ex) when (ex.Status is 429 or >= 500 || ex.Reason is "rateLimitExceeded" or "userRateLimitExceeded" or "quotaExceeded" or "timeout" or "network")
        {
            failures++;
            var interval = TimeSpan.FromSeconds(settings.Load().IntervalSeconds);
            var backoff = interval * Math.Min(Math.Pow(2, failures), 8);
            // Google's Retry-After is not trusted: it is clamped, and never allowed to shorten the backoff (a zero or past date must not mean a retry every second).
            var delay = ex.RetryAfter is { } retryAfter ? TimeSpan.FromSeconds(Math.Max(Math.Clamp(retryAfter.TotalSeconds, 1, 3600), backoff.TotalSeconds)) : backoff;
            nextAttempt = DateTimeOffset.Now + delay;
            error = ex.Reason is "network" or "timeout" ? ex.Message : $"Google is limiting requests or not responding. Next attempt {nextAttempt:HH:mm}.";
            throw new QuantosException(error, 503);
        }
        catch (GoogleApiException ex) { error = ex.Message; throw new QuantosException(ex.Message, 502); }
    }

    public async Task ResolveDeletes(string action, CancellationToken ct)
    {
        if (action is not ("apply" or "cancel")) throw new QuantosException("Choose apply or cancel.");
        await gate.WaitAsync(ct);
        try
        {
            lock (sync) if (disconnecting > 0) throw new QuantosException("Google Tasks is being disconnected.", 409);
            var c = settings.Load();
            var session = host.Session;
            if (session is null || !(c.Vault is null || Vault.PathEquals(session.Vault.Root, c.Vault))) throw new QuantosException("Open the vault this was set up for.", 409);
            if (action == "apply" && (string.IsNullOrEmpty(c.ProtectedRefreshToken) || needsReconnect)) throw new QuantosException("Connect Google first.", 409);
            var engine = NewEngine(session);
            if (action == "apply") await Guarded(() => engine.ApplyPendingDeletes(ct));
            else await engine.CancelPendingDeletes(ct);
        }
        finally { Invalidate(); gate.Release(); }
        Kick();
    }

    public async Task Disconnect(CancellationToken ct)
    {
        // From here until the end no new sign-in may start or be stored: one finishing meanwhile would be wiped below without being revoked.
        lock (sync) { connectionEpoch++; disconnecting++; }
        var counted = true; // the count is released before the gate, so a call queued behind this Disconnect sees it finished
        try
        {
            CancelSync(); oauth.CancelFlow();
            await gate.WaitAsync(ct);
            try
            {
                var c = settings.Load();
                notice = null;
                if (GoogleTasksSettingsStore.Unseal(c.ProtectedRefreshToken) is { } token && !await oauth.Revoke(token, ct)) notice = RevokeFailed;
                apis.Forget();
                // The settings go first. If they cannot be removed (or even overwritten), the connection stays exactly as it was, with its
                // sync records intact and sync stopped, instead of a configured connection whose records are gone (which would re-push or
                // re-import every task if it were reconnected).
                var removed = true;
                // Under the settings lock, so a Confirm or interval change in flight cannot write the old sign-in back after the delete.
                lock (sync)
                {
                    try { settings.Delete(); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        notice ??= "Quantos couldn't delete its Google Tasks settings file. Delete google-tasks.json in Quantos's client data settings folder.";
                        try { settings.Save(new GoogleTasksConfig()); }
                        catch (Exception inner) when (inner is IOException or UnauthorizedAccessException) { removed = false; }
                    }
                    previews.Clear(); snapshot = null; snapshotVersion++; configured = false;
                }
                // The state belongs to the vault the settings were set up for, which may not be the one open now.
                if (removed)
                {
                    if ((c.Vault ?? host.Session?.Vault.Root) is { } vaultRoot)
                    {
                        try { new FileGoogleTasksStateStore(Path.Combine(host.Paths.ClientData, "google-tasks", FileGoogleTasksStateStore.VaultKey(vaultRoot))).Delete(); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { notice ??= StateKept; }
                    }
                    else notice ??= StateKept; // no vault to locate the records by
                }
                error = null; lastSynced = null; nextAttempt = null; last = null; needsReconnect = !removed; apiDisabled = false; failures = 0;
            }
            finally { lock (sync) { if (counted) { disconnecting--; counted = false; } } gate.Release(); }
        }
        finally { lock (sync) { if (counted) disconnecting--; } }
    }

    private void CancelSync()
    {
        try { active?.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>Asks for a sync as soon as possible. Safe to call from any thread (and from Android's OnResume).</summary>
    public void Kick()
    {
        if (oauth.HasDeferred) _ = Task.Run(async () => { try { await ResumeSignIn(); } catch (Exception ex) { error = "Google sign-in could not finish: " + ex.Message; } });
        Resubscribe(); // a vault opened or switched since the last loop pass starts being watched now, not after a whole interval
        try { kick.Release(); } catch (SemaphoreFullException) { }
    }

    // ---- the loop ---------------------------------------------------------------------------------

    private bool ShouldRun()
    {
        var c = settings.Load();
        return !string.IsNullOrEmpty(c.ProtectedRefreshToken) && !needsReconnect && !apiDisabled && host.Session is not null && VaultMatches(c)
            && c.Mappings.Any(m => m.Confirmed) && (nextAttempt is null || DateTimeOffset.Now >= nextAttempt);
    }

    /// <summary>Recomputes `configured` from the saved settings, under the same lock that every settings write uses so a stale value cannot overwrite a newer one.</summary>
    private void RefreshConfigured()
    {
        lock (sync)
        {
            var c = settings.Load();
            configured = !string.IsNullOrEmpty(c.ProtectedRefreshToken) && c.Mappings.Any(m => m.Confirmed);
        }
    }

    /// <summary>Restarts the 5 second timer for a change that did not come from Quantos itself.</summary>
    internal void OnChange(ChangeEvent e)
    {
        if (!configured) return; // Google Tasks is not set up: a vault change costs nothing here
        if (e.Origin is { } origin && origin.StartsWith("System:", StringComparison.Ordinal)) return;
        if (e.Kind is not (ChangeKind.Created or ChangeKind.Updated or ChangeKind.Moved or ChangeKind.Trashed or ChangeKind.Restored or ChangeKind.Deleted
            or ChangeKind.Archived or ChangeKind.Unarchived or ChangeKind.Reindexed)) return;
        lock (sync)
        {
            debounce?.Dispose();
            debounce = new Timer(_ => Kick(), null, Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Resubscribe()
    {
        lock (subLock)
        {
            var session = host.Session;
            if (ReferenceEquals(session, subscribed)) return;
            if (subscribed is not null && handler is not null) subscribed.Changes.Changed -= handler;
            subscribed = session;
            handler = OnChange;
            if (session is not null) session.Changes.Changed += handler;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Resubscribe();
                SeedBuiltInClient();
                RefreshConfigured();
                if (ShouldRun()) await SyncNow(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { } // a timeout or a cancelled run: try again next time, never stop the host
            catch (QuantosException ex) when (ex.Status >= 500) { error ??= ex.Message; }
            catch (QuantosException) { } // a precondition (not connected, disconnecting, wrong vault) is not an error to show
            catch (Exception ex) { error = ex.Message; }
            // How long to wait. Reading the settings can fail (a file locked by antivirus or a sync folder): that must never stop the host.
            var wait = TimeSpan.FromSeconds(120);
            try
            {
                wait = TimeSpan.FromSeconds(Math.Clamp(settings.Load().IntervalSeconds, 30, 900));
                if (nextAttempt is { } at && at > DateTimeOffset.Now) wait = at - DateTimeOffset.Now;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or QuantosException) { }
            try { await kick.WaitAsync(TimeSpan.FromSeconds(Math.Clamp(wait.TotalSeconds, 1, 3600)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public override void Dispose()
    {
        lock (sync) debounce?.Dispose();
        base.Dispose();
    }
}
