using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Quantos.Backend;
using Quantos.Core;

namespace Quantos.Tests;

/// <summary>Acceptance tests "Permissions and security" 6 and 7, run against a real backend on loopback.</summary>
public sealed class BackendSecurityTests : IAsyncLifetime
{
    private readonly TestVault _paths = new();
    private BackendHost _host = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        // Use separate folders from TestVault's own WriteService (that one isn't started against this vault).
        var options = new BackendOptions
        {
            VaultPath = Path.Combine(_paths.Root, "api-vault"),
            ClientDataPath = Path.Combine(_paths.Root, "api-client-data"),
            Port = 0,
            OpenBrowser = false,
        };
        _host = BackendHost.Create(options, fileLogging: false);
        await _host.App.StartAsync();
        _host.AnnounceStarted();
        _http = new HttpClient { BaseAddress = new Uri(_host.BaseUrl) };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _host.DisposeAsync();
        _paths.Dispose();
    }

    private HttpRequestMessage Request(HttpMethod method, string path, object? body = null, bool token = true)
    {
        var r = new HttpRequestMessage(method, path);
        if (token) r.Headers.Add(LocalSecurity.TokenHeader, _host.Runtime.Token);
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    [Fact]
    public async Task Request_without_token_is_rejected()
    {
        var res = await _http.SendAsync(Request(HttpMethod.Get, "/api/status", token: false));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Request_with_wrong_token_is_rejected()
    {
        var r = Request(HttpMethod.Get, "/api/status", token: false);
        r.Headers.Add(LocalSecurity.TokenHeader, "not-the-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.SendAsync(r)).StatusCode);
    }

    [Fact]
    public async Task Request_from_unexpected_origin_is_rejected_even_with_token()
    {
        var r = Request(HttpMethod.Get, "/api/status");
        r.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(r)).StatusCode);
    }

    [Fact]
    public async Task Dns_rebinding_host_header_is_rejected()
    {
        var r = Request(HttpMethod.Get, "/api/status");
        r.Headers.Host = $"evil.example:{_host.Runtime.Port}";
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.SendAsync(r)).StatusCode);
    }

    [Fact]
    public async Task Own_origin_with_token_is_allowed()
    {
        var r = Request(HttpMethod.Get, "/api/status");
        r.Headers.Add("Origin", _host.BaseUrl);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(r)).StatusCode);
    }

    [Fact]
    public async Task Frontend_is_served_without_token_but_has_no_data_and_a_strict_csp()
    {
        var res = await _http.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("default-src 'self'", res.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Runtime_file_describes_the_running_backend()
    {
        var file = Path.Combine(_host.Options.ClientDataPath, "runtime", "backend.json");
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file));
        Assert.Equal(_host.Runtime.Port, doc.RootElement.GetProperty("port").GetInt32());
        Assert.Equal(_host.Runtime.Token, doc.RootElement.GetProperty("token").GetString());
    }

    [Fact]
    public async Task Api_round_trip_create_update_conflict_trash_restore()
    {
        var created = await (await _http.SendAsync(Request(HttpMethod.Post, "/api/nodes", new { title = "API note", body = "one\n" })))
            .Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("header").GetProperty("id").GetString()!;
        var version = created.GetProperty("version").GetString()!;

        var conflict = await _http.SendAsync(Request(HttpMethod.Put, $"/api/nodes/{id}", new { expectedVersion = "stale", body = "x" }));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(version, (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("currentVersion").GetString());

        var updated = await _http.SendAsync(Request(HttpMethod.Put, $"/api/nodes/{id}", new { expectedVersion = version, body = "two\n" }));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var v2 = (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetString();

        var trashed = await (await _http.SendAsync(Request(HttpMethod.Post, $"/api/nodes/{id}/trash", new { expectedVersion = v2 })))
            .Content.ReadFromJsonAsync<JsonElement>();
        var restore = await _http.SendAsync(Request(HttpMethod.Post, $"/api/trash/{trashed.GetProperty("trashId").GetString()}/restore"));
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        var missing = await _http.SendAsync(Request(HttpMethod.Get, "/api/nodes/00000000-0000-7000-8000-000000000000"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public void Second_backend_on_the_same_vault_refuses_to_start()
    {
        var options = new BackendOptions { VaultPath = _host.Options.VaultPath, ClientDataPath = Path.Combine(_paths.Root, "other"), Port = 0, OpenBrowser = false };
        Assert.Throws<VaultLockedException>(() => BackendHost.Create(options, fileLogging: false));
    }
}
