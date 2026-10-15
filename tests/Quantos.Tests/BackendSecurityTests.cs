using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Quantos.Backend;
using Quantos.Backend.Hosting;
using Quantos.Core;

namespace Quantos.Tests;

/// <summary>BUILD-PLAN section 13, "Permissions and security", against a real localhost server.</summary>
public sealed class BackendSecurityTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quantos-tests", Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;
    private AccessTokens _tokens = null!;
    private string _url = "";

    public async Task InitializeAsync()
    {
        var paths = new QuantosPaths(Path.Combine(_root, "client-data"));
        _tokens = new AccessTokens();
        _app = Program.Build([], new BackendOptions { Vault = Path.Combine(_root, "Vault"), Port = 0 }, paths, _tokens);
        await _app.StartAsync();
        _url = _app.Services.GetService(typeof(ServerAddress)) is ServerAddress a ? a.Url : throw new InvalidOperationException();
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private HttpClient Client(bool token = false, string? client = null, CookieContainer? cookies = null)
    {
        var handler = new HttpClientHandler { CookieContainer = cookies ?? new CookieContainer(), UseCookies = true, AllowAutoRedirect = false };
        var http = new HttpClient(handler) { BaseAddress = new Uri(_url) };
        if (token) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _tokens.LaunchToken);
        if (client is not null) http.DefaultRequestHeaders.Add(RequestSecurity.ClientHeader, client);
        return http;
    }

    [Fact] // 13 security.6
    public async Task A_request_without_the_access_token_is_rejected()
    {
        using var http = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("api/status")).StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("api/status")).StatusCode);
        using var ok = Client(token: true);
        Assert.Equal(HttpStatusCode.OK, (await ok.GetAsync("api/status")).StatusCode);
    }

    [Fact] // 13 security.7
    public async Task A_request_from_an_unexpected_origin_is_rejected()
    {
        using var http = Client(token: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/status");
        request.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(request)).StatusCode);
        using var same = new HttpRequestMessage(HttpMethod.Get, "api/status");
        same.Headers.Add("Origin", _url.TrimEnd('/'));
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(same)).StatusCode);
    }

    [Fact]
    public async Task A_request_with_an_unexpected_host_header_is_rejected()
    {
        using var http = Client(token: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/status");
        request.Headers.Host = "rebind.example:" + new Uri(_url).Port;
        Assert.Equal(HttpStatusCode.MisdirectedRequest, (await http.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Browser_sessions_use_a_one_time_code_and_need_the_request_header_to_write()
    {
        using var native = Client(token: true);
        var code = await (await native.PostAsJsonAsync("api/auth/code", new { kind = "browser" })).Content.ReadFromJsonAsync<JsonElement>();
        var openUrl = new Uri(code.GetProperty("url").GetString()!);

        var cookies = new CookieContainer();
        using var browser = Client(cookies: cookies);
        var open = await browser.GetAsync(openUrl.PathAndQuery);
        Assert.Equal(HttpStatusCode.Redirect, open.StatusCode);
        Assert.Contains("httponly", open.Headers.GetValues("Set-Cookie").First(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("api/status")).StatusCode);

        // Codes are single use.
        using var other = Client();
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(openUrl.PathAndQuery)).StatusCode);

        // Writes from a cookie session need the custom header.
        var bare = await browser.PostAsJsonAsync("api/nodes", new { title = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, bare.StatusCode);
        browser.DefaultRequestHeaders.Add(RequestSecurity.CsrfHeader, "1");
        var created = await browser.PostAsJsonAsync("api/nodes", new { title = "From browser" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        // A browser session can't mint codes or stop the backend.
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsJsonAsync("api/auth/code", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsync("api/system/shutdown", null)).StatusCode);
    }

    [Fact] // 13 security.5
    public async Task Permanent_deletion_requires_approval_in_the_windows_client()
    {
        var cookies = new CookieContainer();
        using var native = Client(token: true);
        var code = await (await native.PostAsJsonAsync("api/auth/code", new { kind = "browser" })).Content.ReadFromJsonAsync<JsonElement>();
        using var browser = Client(cookies: cookies);
        await browser.GetAsync(new Uri(code.GetProperty("url").GetString()!).PathAndQuery);
        browser.DefaultRequestHeaders.Add(RequestSecurity.CsrfHeader, "1");

        var node = await (await browser.PostAsJsonAsync("api/nodes", new { title = "Doomed" })).Content.ReadFromJsonAsync<JsonElement>();
        var entry = await (await browser.PostAsync($"api/nodes/{node.GetProperty("id").GetString()}/trash", null)).Content.ReadFromJsonAsync<JsonElement>();
        var entryId = entry.GetProperty("entryId").GetString();

        var attempt = await browser.DeleteAsync($"api/trash/{entryId}");
        Assert.Equal((HttpStatusCode)428, attempt.StatusCode);
        var approvalId = (await attempt.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("approval").GetProperty("id").GetString();

        // The browser can't approve its own request, and neither can the CLI.
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsJsonAsync($"api/approvals/{approvalId}", new { approve = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await native.PostAsJsonAsync($"api/approvals/{approvalId}", new { approve = true })).StatusCode);

        using var windows = Client(token: true, client: "windows");
        Assert.Equal(HttpStatusCode.OK, (await windows.PostAsJsonAsync($"api/approvals/{approvalId}", new { approve = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync($"api/trash/{entryId}?approval={approvalId}")).StatusCode);
    }

    [Fact] // 13 security.1
    public async Task An_agent_write_is_denied_by_default()
    {
        using var agent = Client(token: true);
        agent.DefaultRequestHeaders.Add(RequestSecurity.AgentHeader, "helper");
        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync("api/nodes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsJsonAsync("api/nodes", new { title = "nope" })).StatusCode);
    }

    [Fact]
    public async Task The_discovery_file_names_the_running_backend()
    {
        var info = Discovery.Read(new QuantosPaths(Path.Combine(_root, "client-data")));
        Assert.NotNull(info);
        Assert.Equal(_url, info.Url);
        Assert.True(await Discovery.IsAliveAsync(info));
    }

    [Fact]
    public async Task Events_stream_announces_changes()
    {
        using var native = Client(token: true);
        native.Timeout = TimeSpan.FromSeconds(10);
        using var stream = await native.GetStreamAsync("api/events");
        using var reader = new StreamReader(stream);
        Assert.Equal("event: hello", await reader.ReadLineAsync());
        await reader.ReadLineAsync(); await reader.ReadLineAsync();
        using var writer = Client(token: true);
        await writer.PostAsJsonAsync("api/nodes", new { title = "Announced" });
        Assert.Equal("event: change", await reader.ReadLineAsync());
        Assert.Contains("\"kind\":\"Created\"", await reader.ReadLineAsync());
    }
}
