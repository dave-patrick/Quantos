using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.GoogleTasks;
using Quantos.Core.Security;

namespace Quantos.Tests;

internal sealed class FakeApiFactory(FakeGoogle google) : IGoogleTasksApiFactory
{
    public int Forgets;
    public IGoogleTasksApi Create() => google;
    public void Forget() => Forgets++;
}

internal sealed class TokenEndpoint : HttpMessageHandler
{
    public List<string> Bodies { get; } = [];
    public bool FailRevoke;
    /// <summary>When set, the next token exchange waits for it, so a test can change settings while a sign-in is in flight.</summary>
    public TaskCompletionSource? Hold;
    /// <summary>When set, token revocation waits for it, so a test can act while a disconnect is in progress.</summary>
    public TaskCompletionSource? HoldRevoke;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
        if (Hold is { } hold && request.RequestUri!.AbsolutePath.Contains("token")) await hold.Task;
        if (HoldRevoke is { } holdRevoke && request.RequestUri!.AbsolutePath.Contains("revoke")) await holdRevoke.Task;
        if (FailRevoke && request.RequestUri!.AbsolutePath.Contains("revoke")) throw new HttpRequestException("offline");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"access_token\":\"a\",\"refresh_token\":\"REFRESH-SECRET\",\"expires_in\":3600,\"scope\":\"https://www.googleapis.com/auth/tasks\"}", Encoding.UTF8, "application/json")
        };
    }
}

/// <summary>The real backend pipeline with a fake Google: the sign-in callback, access control and the settings/preview/confirm/sync rules.</summary>
public sealed class GoogleTasksBackendTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "quantos-tests", Guid.NewGuid().ToString("N"));
    private readonly AccessTokens tokens = new();
    private readonly FakeGoogle google = new();
    private readonly TokenEndpoint tokenEndpoint = new();
    private FakeApiFactory factory = null!;
    private WebApplication app = null!;
    private string url = "";

    public async Task InitializeAsync()
    {
        app = Quantos.Backend.Program.Build([], new BackendOptions { Vault = Path.Combine(root, "Vault"), Port = 0 }, new QuantosPaths(Path.Combine(root, "client-data")), tokens,
            services =>
            {
                services.AddSingleton(BuiltInGoogleClient.None); // these tests start with no client, whatever this machine builds in
                services.AddSingleton(new GoogleOAuth(new HttpClient(tokenEndpoint)));
                services.AddSingleton<IGoogleTasksApiFactory>(factory = new FakeApiFactory(google));
            });
        await app.StartAsync();
        url = app.Services.GetRequiredService<ServerAddress>().Url;
    }

    public async Task DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    private HttpClient User()
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(url) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.IssueNativeToken("windows"));
        return http;
    }

    private static StringContent Body(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    private static string State(string authUrl) => System.Web.HttpUtility.ParseQueryString(new Uri(authUrl).Query)["state"]!;

    private async Task<string> StartFlow(HttpClient user)
    {
        Assert.Equal(HttpStatusCode.OK, (await user.PutAsync("api/google-tasks/settings", Body(new { clientId = "cid", clientSecret = "CLIENT-SECRET" }))).StatusCode);
        var connect = await user.PostAsync("api/google-tasks/connect", null);
        Assert.Equal(HttpStatusCode.OK, connect.StatusCode);
        return (await Json(connect)).GetProperty("authUrl").GetString()!;
    }

    private async Task<HttpResponseMessage> Callback(string query, HttpMethod? method = null, string path = "oauth/google/callback")
    {
        // Google's redirect: a cross-site top-level navigation with no Origin and no credentials.
        using var http = new HttpClient { BaseAddress = new Uri(url) };
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, path + query);
        request.Headers.Add("Sec-Fetch-Site", "cross-site");
        return await http.SendAsync(request);
    }

    private async Task Connect(HttpClient user)
    {
        var authUrl = await StartFlow(user);
        var response = await Callback("?code=abc&state=" + State(authUrl));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_cross_site_callback_completes_the_sign_in_with_hardened_headers_and_no_secrets_in_the_status()
    {
        using var user = User();
        var authUrl = await StartFlow(user);
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", authUrl);
        var response = await Callback("?code=abc&state=" + State(authUrl));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        var statusText = await (await user.GetAsync("api/google-tasks")).Content.ReadAsStringAsync();
        Assert.Contains("\"state\":\"Connected\"", statusText);
        Assert.DoesNotContain("REFRESH-SECRET", statusText);
        Assert.DoesNotContain("CLIENT-SECRET", statusText);
        Assert.DoesNotContain("REFRESH-SECRET", File.ReadAllText(Path.Combine(root, "client-data", "settings", "google-tasks.json")));
    }

    [Fact]
    public async Task A_wrong_state_is_refused_without_cancelling_the_real_sign_in_and_a_post_is_not_allowed()
    {
        using var user = User();
        var authUrl = await StartFlow(user);
        Assert.Equal(HttpStatusCode.BadRequest, (await Callback("?code=abc&state=forged")).StatusCode);
        Assert.Equal("waiting", (await Json(await user.GetAsync("api/google-tasks"))).GetProperty("flow").GetString());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Callback("?code=abc&state=" + State(authUrl), HttpMethod.Post)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Callback("?code=abc&state=" + State(authUrl))).StatusCode);
        // single use
        Assert.Equal(HttpStatusCode.BadRequest, (await Callback("?code=abc&state=" + State(authUrl))).StatusCode);
    }

    [Fact]
    public async Task The_exemption_covers_only_the_exact_callback_path()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Callback("", path: "oauth/google/callback/extra")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Callback("", path: "api/google-tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Callback("", path: "api/nodes")).StatusCode);
    }

    [Fact]
    public async Task Only_the_user_can_reach_the_endpoints_even_to_read()
    {
        var credential = app.Services.GetRequiredService<ClientCredentials>().Issue(ActorKind.Agent, "reader");
        using var agent = new HttpClient { BaseAddress = new Uri(url) };
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.GetAsync("api/google-tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsync("api/google-tasks/connect", null)).StatusCode);
    }

    [Fact]
    public async Task Settings_are_validated_and_an_empty_secret_keeps_the_saved_one()
    {
        using var user = User();
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PutAsync("api/google-tasks/settings", Body(new { intervalSeconds = 10 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PutAsync("api/google-tasks/settings", Body(new { mappings = new[] { new { workspace = "nope", listId = "L1", listTitle = "x" } } }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PutAsync("api/google-tasks/settings", Body(new
        {
            mappings = new[] { new { workspace = "default", listId = "L1", listTitle = "x" }, new { workspace = "default", listId = "L2", listTitle = "y" } }
        }))).StatusCode);
        await Connect(user);
        // an empty secret keeps the saved one, so the sign-in survives
        await user.PutAsync("api/google-tasks/settings", Body(new { clientSecret = "", intervalSeconds = 60 }));
        Assert.Contains("\"state\":\"Connected\"", await (await user.GetAsync("api/google-tasks")).Content.ReadAsStringAsync());
        // changing the client ID forgets the sign-in
        await user.PutAsync("api/google-tasks/settings", Body(new { clientId = "other", clientSecret = "OTHER-SECRET" }));
        Assert.Contains("\"state\":\"Off\"", await (await user.GetAsync("api/google-tasks")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Lists_need_a_connection_and_confirm_needs_a_fresh_preview()
    {
        using var user = User();
        Assert.Equal(HttpStatusCode.Conflict, (await user.GetAsync("api/google-tasks/lists")).StatusCode);
        await Connect(user);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("api/google-tasks/lists")).StatusCode);
        var put = await user.PutAsync("api/google-tasks/settings", Body(new { mappings = new[] { new { workspace = "default", listId = "L1", listTitle = "Mine" } } }));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        // nothing syncs until a preview is confirmed
        Assert.Equal(HttpStatusCode.Conflict, (await user.PostAsync("api/google-tasks/sync", null)).StatusCode);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        Assert.Equal(HttpStatusCode.Conflict, (await user.PostAsync("api/google-tasks/confirm", Body(new { workspace = "default", cutoff }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsync("api/google-tasks/preview", Body(new { workspace = "unmapped" }))).StatusCode);

        var session = app.Services.GetRequiredService<VaultHost>().Require();
        session.Tasks.Create(new Quantos.Core.Tasks.CreateTaskRequest { Title = "From Quantos" }, Actor.User);
        google.Add("From Google");
        var preview = await Json(await user.PostAsync("api/google-tasks/preview", Body(new { workspace = "default" })));
        Assert.Equal(1, preview.GetProperty("createInGoogle").GetInt32());
        Assert.Equal(1, preview.GetProperty("importToQuantos").GetInt32());
        Assert.Equal(0, google.Mutations);
        Assert.Equal(HttpStatusCode.Conflict, (await user.PostAsync("api/google-tasks/confirm", Body(new { workspace = "default", cutoff = cutoff.AddDays(-5) }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.PostAsync("api/google-tasks/confirm", Body(new { workspace = "default", cutoff = preview.GetProperty("cutoff").GetDateTimeOffset() }))).StatusCode);

        var sync = await Json(await user.PostAsync("api/google-tasks/sync", null));
        Assert.Equal(1, sync.GetProperty("pushed").GetInt32());
        Assert.Equal(1, sync.GetProperty("pulled").GetInt32());
        Assert.Contains(session.Tasks.List(), t => t.Title == "From Google");
        Assert.Contains(google.Lists["L1"], t => t.Title == "From Quantos");

        // changing the list for the workspace needs a new preview
        await user.PutAsync("api/google-tasks/settings", Body(new { mappings = new[] { new { workspace = "default", listId = "L2", listTitle = "Other" } } }));
        google.Lists["L2"] = [];
        Assert.Equal(HttpStatusCode.Conflict, (await user.PostAsync("api/google-tasks/confirm", Body(new { workspace = "default", cutoff }))).StatusCode);
    }

    [Fact]
    public async Task A_new_sign_in_new_credentials_and_disconnect_all_discard_the_cached_access_token()
    {
        using var user = User();
        await Connect(user);
        var afterSignIn = factory.Forgets;
        Assert.True(afterSignIn >= 1);
        await user.PutAsync("api/google-tasks/settings", Body(new { clientSecret = "NEW-SECRET" }));
        Assert.True(factory.Forgets > afterSignIn);
        await Connect(user);
        var beforeDisconnect = factory.Forgets;
        await user.DeleteAsync("api/google-tasks");
        Assert.True(factory.Forgets > beforeDisconnect);
    }

    [Fact]
    public async Task A_google_error_while_listing_is_mapped_to_a_clear_status_instead_of_a_500()
    {
        using var user = User();
        await Connect(user);
        google.FailLists = new GoogleApiException(403, "accessNotConfigured", "Tasks API has not been used");
        var response = await user.GetAsync("api/google-tasks/lists");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Google Tasks API", await response.Content.ReadAsStringAsync());
        Assert.Contains("Google Tasks API", (await Json(await user.GetAsync("api/google-tasks"))).GetProperty("reason").GetString());
        google.FailLists = new GoogleAuthException("revoked");
        Assert.Equal(HttpStatusCode.Conflict, (await user.GetAsync("api/google-tasks/lists")).StatusCode);
        Assert.Equal("NeedsReconnect", (await Json(await user.GetAsync("api/google-tasks"))).GetProperty("state").GetString());
    }

    private async Task ConfirmedMapping(HttpClient user)
    {
        await Connect(user);
        await user.PutAsync("api/google-tasks/settings", Body(new { mappings = new[] { new { workspace = "default", listId = "L1", listTitle = "Mine" } } }));
        var preview = await Json(await user.PostAsync("api/google-tasks/preview", Body(new { workspace = "default" })));
        await user.PostAsync("api/google-tasks/confirm", Body(new { workspace = "default", cutoff = preview.GetProperty("cutoff").GetDateTimeOffset() }));
    }

    private async Task<Task<HttpResponseMessage>> StartBlockedSync(HttpClient user)
    {
        google.BlockListTasks = true;
        var running = user.PostAsync("api/google-tasks/sync", null);
        for (var i = 0; i < 100 && !google.Calls.Contains("ListTasks"); i++) await Task.Delay(50);
        Assert.Contains("ListTasks", google.Calls); // the sync is now waiting inside Google
        return running;
    }

    [Fact]
    public async Task Removing_a_mapping_stops_a_running_sync_with_a_409_but_leaves_a_pending_sign_in_alone()
    {
        using var user = User();
        await ConfirmedMapping(user);
        google.Calls.Clear();
        await user.PostAsync("api/google-tasks/connect", null); // a sign-in is waiting
        Assert.Equal("waiting", (await Json(await user.GetAsync("api/google-tasks"))).GetProperty("flow").GetString());
        var running = await StartBlockedSync(user);
        Assert.Equal(HttpStatusCode.OK, (await user.PutAsync("api/google-tasks/settings", Body(new { mappings = Array.Empty<object>() }))).StatusCode);
        var stopped = await running.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.Conflict, stopped.StatusCode);
        Assert.Contains("settings changed", await stopped.Content.ReadAsStringAsync());
        Assert.Equal("waiting", (await Json(await user.GetAsync("api/google-tasks"))).GetProperty("flow").GetString()); // not cancelled by a row removal
        google.BlockListTasks = false;
    }

    [Fact]
    public async Task Changing_the_credentials_or_disconnecting_stops_a_running_sync_and_cancels_a_pending_sign_in()
    {
        using var user = User();
        await ConfirmedMapping(user);
        google.Calls.Clear();
        await user.PostAsync("api/google-tasks/connect", null);
        var running = await StartBlockedSync(user);
        await user.PutAsync("api/google-tasks/settings", Body(new { clientSecret = "ANOTHER" }));
        Assert.Equal(HttpStatusCode.Conflict, (await running.WaitAsync(TimeSpan.FromSeconds(10))).StatusCode);
        Assert.Equal("idle", (await Json(await user.GetAsync("api/google-tasks"))).GetProperty("flow").GetString());
        google.BlockListTasks = false;

        await Connect(user);
        await user.PutAsync("api/google-tasks/settings", Body(new { mappings = new[] { new { workspace = "default", listId = "L1", listTitle = "Mine" } } }));
        google.Calls.Clear();
        var preview = await Json(await user.PostAsync("api/google-tasks/preview", Body(new { workspace = "default" })));
        await user.PostAsync("api/google-tasks/confirm", Body(new { workspace = "default", cutoff = preview.GetProperty("cutoff").GetDateTimeOffset() }));
        google.Calls.Clear();
        var again = await StartBlockedSync(user);
        Assert.Equal(HttpStatusCode.NoContent, (await user.DeleteAsync("api/google-tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await again.WaitAsync(TimeSpan.FromSeconds(10))).StatusCode);
        google.BlockListTasks = false;
    }

    [Fact]
    public async Task A_blank_client_id_means_unchanged_and_disconnect_removes_the_sync_state_folder()
    {
        using var user = User();
        await ConfirmedMapping(user);
        Assert.Equal(HttpStatusCode.OK, (await user.PostAsync("api/google-tasks/sync", null)).StatusCode);
        var stateFolder = Path.Combine(root, "client-data", "google-tasks");
        Assert.True(Directory.Exists(stateFolder) && Directory.GetFiles(stateFolder, "state.json", SearchOption.AllDirectories).Length == 1);
        await user.PutAsync("api/google-tasks/settings", Body(new { clientId = "  " }));
        Assert.Contains("\"state\":\"Connected\"", await (await user.GetAsync("api/google-tasks")).Content.ReadAsStringAsync()); // not signed out
        await user.DeleteAsync("api/google-tasks");
        Assert.Empty(Directory.Exists(stateFolder) ? Directory.GetFiles(stateFolder, "state.json", SearchOption.AllDirectories) : []);
    }

    [Fact]
    public async Task Disconnect_removes_the_state_of_the_vault_the_settings_were_pinned_to_even_when_another_vault_is_open()
    {
        using var user = User();
        await Connect(user);
        var store = app.Services.GetRequiredService<GoogleTasksSettingsStore>();
        var otherVault = Path.Combine(root, "SomeOtherVault");
        store.Save(store.Load() with { Vault = otherVault }); // set up for another vault than the one open now
        var folder = Path.Combine(root, "client-data", "google-tasks", FileGoogleTasksStateStore.VaultKey(otherVault));
        new FileGoogleTasksStateStore(folder).Save(new GoogleTasksState { PendingDeletes = [new PendingDelete { TaskId = "x", Gid = "g", ListId = "L1", Held = true }] });
        Assert.True(File.Exists(Path.Combine(folder, "state.json")));
        Assert.Contains("\"state\":\"Paused\"", await (await user.GetAsync("api/google-tasks")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await user.DeleteAsync("api/google-tasks")).StatusCode);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task A_sign_in_finishing_after_the_credentials_changed_is_discarded_and_revoked_not_stored()
    {
        using var user = User();
        var authUrl = await StartFlow(user);
        tokenEndpoint.Hold = new TaskCompletionSource();
        var callback = Callback("?code=abc&state=" + State(authUrl)); // the token exchange is now waiting inside Google
        for (var i = 0; i < 100 && tokenEndpoint.Bodies.Count == 0; i++) await Task.Delay(50);
        Assert.NotEmpty(tokenEndpoint.Bodies);
        await user.PutAsync("api/google-tasks/settings", Body(new { clientSecret = "REPLACED-SECRET" }));
        tokenEndpoint.Hold.SetResult();
        var response = await callback.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Off", await (await user.GetAsync("api/google-tasks")).Content.ReadAsStringAsync()); // nothing was stored
        Assert.Contains(tokenEndpoint.Bodies, b => b.Contains("token=REFRESH-SECRET")); // and the unused token was revoked
        tokenEndpoint.Hold = null;
    }

    [Fact]
    public async Task A_sign_in_finishing_after_disconnect_is_discarded_and_revoked_and_leaves_no_settings_behind()
    {
        using var user = User();
        var authUrl = await StartFlow(user);
        tokenEndpoint.Hold = new TaskCompletionSource();
        var callback = Callback("?code=abc&state=" + State(authUrl));
        for (var i = 0; i < 100 && tokenEndpoint.Bodies.Count == 0; i++) await Task.Delay(50);
        Assert.Equal(HttpStatusCode.NoContent, (await user.DeleteAsync("api/google-tasks")).StatusCode);
        tokenEndpoint.Hold.SetResult();
        Assert.Equal(HttpStatusCode.BadRequest, (await callback.WaitAsync(TimeSpan.FromSeconds(10))).StatusCode);
        Assert.Contains(tokenEndpoint.Bodies, b => b.Contains("token=REFRESH-SECRET"));
        Assert.False(File.Exists(Path.Combine(root, "client-data", "settings", "google-tasks.json")));
        Assert.Contains("\"state\":\"Off\"", await (await user.GetAsync("api/google-tasks")).Content.ReadAsStringAsync());
        tokenEndpoint.Hold = null;
    }

    [Fact]
    public async Task A_sign_in_cannot_start_while_a_disconnect_is_in_progress()
    {
        using var user = User();
        await Connect(user);
        tokenEndpoint.HoldRevoke = new TaskCompletionSource();
        var disconnect = user.DeleteAsync("api/google-tasks"); // waits inside the revoke call
        for (var i = 0; i < 100 && !tokenEndpoint.Bodies.Any(b => b.StartsWith("token=")); i++) await Task.Delay(50);
        var refused = await user.PostAsync("api/google-tasks/connect", null);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("disconnected", await refused.Content.ReadAsStringAsync());
        tokenEndpoint.HoldRevoke.SetResult();
        Assert.Equal(HttpStatusCode.NoContent, (await disconnect.WaitAsync(TimeSpan.FromSeconds(10))).StatusCode);
        tokenEndpoint.HoldRevoke = null;
        // and afterwards the person can connect again as normal
        await StartFlow(user);
    }

    [Fact]
    public async Task Held_deletion_decisions_and_syncs_are_refused_for_the_wrong_vault_and_apply_needs_a_connection()
    {
        using var user = User();
        await ConfirmedMapping(user);
        var store = app.Services.GetRequiredService<GoogleTasksSettingsStore>();
        var pinned = store.Load();
        store.Save(pinned with { Vault = Path.Combine(root, "SomeOtherVault") });
        google.Calls.Clear();
        Assert.Equal(HttpStatusCode.Conflict, (await user.PostAsync("api/google-tasks/sync", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await user.PostAsync("api/google-tasks/deletes", Body(new { action = "apply" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await user.PostAsync("api/google-tasks/deletes", Body(new { action = "cancel" }))).StatusCode);
        Assert.DoesNotContain("ListTasks", google.Calls); // nothing reached Google or the engine
        store.Save(pinned); // back to the right vault
        Assert.Equal(HttpStatusCode.OK, (await user.PostAsync("api/google-tasks/deletes", Body(new { action = "cancel" }))).StatusCode);
        await user.PutAsync("api/google-tasks/settings", Body(new { clientSecret = "NEW-SECRET" })); // signs out
        Assert.Equal(HttpStatusCode.Conflict, (await user.PostAsync("api/google-tasks/deletes", Body(new { action = "apply" }))).StatusCode);
    }

    [Fact]
    public async Task A_change_made_by_the_person_starts_a_sync_after_the_pause_but_one_made_by_quantos_itself_does_not()
    {
        using var user = User();
        await ConfirmedMapping(user);
        var session = app.Services.GetRequiredService<VaultHost>().Require();
        await Task.Delay(500);
        google.Calls.Clear();
        session.Tasks.Create(new Quantos.Core.Tasks.CreateTaskRequest { Title = "By the system" }, Actor.System);
        await Task.Delay(8000); // longer than the 5 second pause: nothing may have started
        Assert.DoesNotContain("ListTasks", google.Calls);
        session.Tasks.Create(new Quantos.Core.Tasks.CreateTaskRequest { Title = "By the person" }, Actor.User);
        // the sync starts after the pause and then pushes the new task; wait for the push itself, not just for the first call
        for (var i = 0; i < 200 && !google.Lists["L1"].Any(t => t.Title == "By the person"); i++) await Task.Delay(100);
        Assert.Contains("ListTasks", google.Calls);
        Assert.Contains(google.Lists["L1"], t => t.Title == "By the person");
    }

    [Theory]
    [InlineData(30 * 24 * 3600)] // a month: must not park sync or break the timer
    [InlineData(-5)]             // a date in the past
    public async Task A_rate_limit_reply_never_schedules_the_next_attempt_outside_one_second_to_one_hour(int retryAfterSeconds)
    {
        using var user = User();
        await ConfirmedMapping(user);
        google.FailListTasks = new GoogleApiException(429, "rateLimitExceeded", "slow down", TimeSpan.FromSeconds(retryAfterSeconds));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await user.PostAsync("api/google-tasks/sync", null)).StatusCode);
        var next = (await Json(await user.GetAsync("api/google-tasks"))).GetProperty("nextAttempt").GetDateTimeOffset();
        var wait = next - DateTimeOffset.Now;
        Assert.InRange(wait.TotalSeconds, 100, 3600 + 5); // never sooner than the backoff, never later than an hour
        google.FailListTasks = null;
    }

    [Fact]
    public async Task A_sync_or_deletion_decision_queued_behind_a_disconnect_does_not_start_and_leaves_no_stale_error()
    {
        using var user = User();
        await ConfirmedMapping(user);
        google.Calls.Clear();
        tokenEndpoint.HoldRevoke = new TaskCompletionSource();
        var disconnect = user.DeleteAsync("api/google-tasks"); // holds the gate inside the revoke call
        for (var i = 0; i < 100 && !tokenEndpoint.Bodies.Any(b => b.StartsWith("token=")); i++) await Task.Delay(50);
        var sync = user.PostAsync("api/google-tasks/sync", null);       // queued behind the disconnect
        var deletes = user.PostAsync("api/google-tasks/deletes", Body(new { action = "cancel" }));
        tokenEndpoint.HoldRevoke.SetResult();
        Assert.Equal(HttpStatusCode.NoContent, (await disconnect.WaitAsync(TimeSpan.FromSeconds(10))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await sync.WaitAsync(TimeSpan.FromSeconds(10))).StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, (await deletes.WaitAsync(TimeSpan.FromSeconds(10))).StatusCode); // nothing left to cancel once disconnected: a no-op, never an error
        tokenEndpoint.HoldRevoke = null;
        Assert.DoesNotContain("ListTasks", google.Calls); // nothing reached Google
        var status = await Json(await user.GetAsync("api/google-tasks"));
        Assert.Equal("Off", status.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("error").ValueKind);
    }

    [Fact]
    public async Task Preview_and_confirm_are_refused_while_another_vault_is_open()
    {
        using var user = User();
        await Connect(user);
        await user.PutAsync("api/google-tasks/settings", Body(new { mappings = new[] { new { workspace = "default", listId = "L1", listTitle = "Mine" } } }));
        var store = app.Services.GetRequiredService<GoogleTasksSettingsStore>();
        store.Save(store.Load() with { Vault = Path.Combine(root, "SomeOtherVault") });
        Assert.Equal(HttpStatusCode.Conflict, (await user.PostAsync("api/google-tasks/preview", Body(new { workspace = "default" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await user.PostAsync("api/google-tasks/confirm", Body(new { workspace = "default", cutoff = DateTimeOffset.UtcNow }))).StatusCode);
    }

    [Fact]
    public async Task While_a_disconnect_runs_settings_changes_are_refused_and_do_not_recreate_the_settings_file()
    {
        using var user = User();
        await Connect(user);
        tokenEndpoint.HoldRevoke = new TaskCompletionSource();
        var first = user.DeleteAsync("api/google-tasks"); // waits inside the revoke call
        for (var i = 0; i < 100 && !tokenEndpoint.Bodies.Any(b => b.StartsWith("token=")); i++) await Task.Delay(50);
        var second = user.DeleteAsync("api/google-tasks"); // an overlapping disconnect
        var interval = await user.PutAsync("api/google-tasks/settings", Body(new { intervalSeconds = 60 }));
        Assert.Equal(HttpStatusCode.Conflict, interval.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await user.PostAsync("api/google-tasks/connect", null)).StatusCode);
        tokenEndpoint.HoldRevoke.SetResult();
        Assert.Equal(HttpStatusCode.NoContent, (await first.WaitAsync(TimeSpan.FromSeconds(10))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await second.WaitAsync(TimeSpan.FromSeconds(10))).StatusCode);
        tokenEndpoint.HoldRevoke = null;
        Assert.False(File.Exists(Path.Combine(root, "client-data", "settings", "google-tasks.json")));
        await StartFlow(user); // once every disconnect has finished the person can set up again
    }

    [Fact]
    public async Task Changing_only_the_client_id_is_refused_until_its_secret_is_given()
    {
        using var user = User();
        await Connect(user);
        var refused = await user.PutAsync("api/google-tasks/settings", Body(new { clientId = "another-client" }));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("client secret", await refused.Content.ReadAsStringAsync());
        Assert.Contains("\"state\":\"Connected\"", await (await user.GetAsync("api/google-tasks")).Content.ReadAsStringAsync()); // untouched
    }

    [Fact]
    public async Task A_state_folder_that_cannot_be_deleted_on_disconnect_is_reported_and_the_settings_still_go()
    {
        using var user = User();
        await ConfirmedMapping(user);
        Assert.Equal(HttpStatusCode.OK, (await user.PostAsync("api/google-tasks/sync", null)).StatusCode);
        var stateFile = Directory.GetFiles(Path.Combine(root, "client-data", "google-tasks"), "state.json", SearchOption.AllDirectories).Single();
        using (new FileStream(stateFile, FileMode.Open, FileAccess.Read, FileShare.None)) // another process holds it open
        {
            Assert.Equal(HttpStatusCode.NoContent, (await user.DeleteAsync("api/google-tasks")).StatusCode);
            var status = await Json(await user.GetAsync("api/google-tasks"));
            Assert.Contains("sync records", status.GetProperty("notice").GetString());
            Assert.Equal("Off", status.GetProperty("state").GetString());
        }
        Assert.False(File.Exists(Path.Combine(root, "client-data", "settings", "google-tasks.json")));
    }

    [Fact]
    public async Task A_disconnect_that_could_not_revoke_access_says_so_and_a_new_sign_in_clears_it()
    {
        using var user = User();
        await Connect(user);
        tokenEndpoint.FailRevoke = true;
        Assert.Equal(HttpStatusCode.NoContent, (await user.DeleteAsync("api/google-tasks")).StatusCode);
        var status = await Json(await user.GetAsync("api/google-tasks"));
        Assert.Equal("Off", status.GetProperty("state").GetString());
        Assert.Contains("myaccount.google.com/permissions", status.GetProperty("notice").GetString());
        tokenEndpoint.FailRevoke = false;
        await Connect(user);
        Assert.Equal(JsonValueKind.Null, (await Json(await user.GetAsync("api/google-tasks"))).GetProperty("notice").ValueKind);
    }

    [Fact]
    public async Task Disconnect_forgets_the_sign_in_and_the_state()
    {
        using var user = User();
        await Connect(user);
        Assert.Equal(HttpStatusCode.NoContent, (await user.DeleteAsync("api/google-tasks")).StatusCode);
        Assert.Contains("\"state\":\"Off\"", await (await user.GetAsync("api/google-tasks")).Content.ReadAsStringAsync());
        Assert.False(File.Exists(Path.Combine(root, "client-data", "settings", "google-tasks.json")));
    }
}

/// <summary>A copy of Quantos with a Google client built in: there is nothing to paste, only Connect.</summary>
public sealed class GoogleTasksBuiltInClientTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "quantos-tests", Guid.NewGuid().ToString("N"));
    private readonly AccessTokens tokens = new();
    private WebApplication app = null!;
    private string url = "";

    public async Task InitializeAsync()
    {
        app = Quantos.Backend.Program.Build([], new BackendOptions { Vault = Path.Combine(root, "Vault"), Port = 0 }, new QuantosPaths(Path.Combine(root, "client-data")), tokens,
            services =>
            {
                services.AddSingleton(new BuiltInGoogleClient("built-in-id.apps.googleusercontent.com", "BUILT-IN-SECRET"));
                services.AddSingleton(new GoogleOAuth(new HttpClient(new TokenEndpoint())));
                services.AddSingleton<IGoogleTasksApiFactory>(new FakeApiFactory(new FakeGoogle()));
            });
        await app.StartAsync();
        url = app.Services.GetRequiredService<ServerAddress>().Url;
    }

    public async Task DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    private HttpClient User()
    {
        var http = new HttpClient { BaseAddress = new Uri(url) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.IssueNativeToken("windows"));
        return http;
    }

    [Fact]
    public async Task Connect_works_with_no_setup_uses_the_built_in_client_and_never_exposes_its_secret()
    {
        using var user = User();
        var status = await user.GetStringAsync("api/google-tasks");
        Assert.Contains("\"builtInClient\":true", status);
        Assert.Contains("\"hasCredentials\":true", status);
        Assert.DoesNotContain("BUILT-IN-SECRET", status);
        var connect = await user.PostAsync("api/google-tasks/connect", null);
        Assert.Equal(HttpStatusCode.OK, connect.StatusCode);
        Assert.Contains("client_id=built-in-id.apps.googleusercontent.com", await connect.Content.ReadAsStringAsync());
        // after a disconnect it is ready to connect again, with nothing to enter
        Assert.Equal(HttpStatusCode.NoContent, (await user.DeleteAsync("api/google-tasks")).StatusCode);
        Assert.Contains("\"hasCredentials\":true", await user.GetStringAsync("api/google-tasks"));
    }

    [Fact]
    public async Task A_client_the_person_saved_themselves_is_not_replaced_by_the_built_in_one()
    {
        using var user = User();
        await user.GetStringAsync("api/google-tasks"); // seeds the built-in client
        var body = new StringContent(JsonSerializer.Serialize(new { clientId = "my-own-id", clientSecret = "MY-OWN-SECRET" }), Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.OK, (await user.PutAsync("api/google-tasks/settings", body)).StatusCode);
        var connect = await user.PostAsync("api/google-tasks/connect", null);
        Assert.Contains("client_id=my-own-id", await connect.Content.ReadAsStringAsync());
    }
}
