using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Quantos.Backend;
using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Security;

namespace Quantos.Tests;

/// <summary>BUILD-PLAN section 13 "Permissions and security" (shell, plugins, safe mode) and Stage 6 automations.</summary>
public sealed class ExtensibilityTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quantos-tests", Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;
    private AccessTokens _tokens = null!;
    private string _url = "";

    public async Task InitializeAsync()
    {
        _tokens = new AccessTokens();
        _app = Program.Build([], new BackendOptions { Vault = Path.Combine(_root, "Vault"), Port = 0 }, new QuantosPaths(Path.Combine(_root, "client-data")), _tokens);
        await _app.StartAsync();
        _url = ((ServerAddress)_app.Services.GetService(typeof(ServerAddress))!).Url;
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private VaultSession Session => ((VaultHost)_app.Services.GetService(typeof(VaultHost))!).Session!;

    private HttpClient Native(string? client = null, string? agent = null)
    {
        var http = new HttpClient { BaseAddress = new Uri(_url) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _tokens.LaunchToken);
        if (client is not null) http.DefaultRequestHeaders.Add(RequestSecurity.ClientHeader, client);
        if (agent is not null) http.DefaultRequestHeaders.Add(RequestSecurity.AgentHeader, agent);
        return http;
    }

    private async Task<HttpClient> Browser()
    {
        using var native = Native();
        var code = await (await native.PostAsJsonAsync("api/auth/code", new { kind = "browser" })).Content.ReadFromJsonAsync<JsonElement>();
        var browser = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = new Uri(_url) };
        await browser.GetAsync(new Uri(code.GetProperty("url").GetString()!).PathAndQuery);
        browser.DefaultRequestHeaders.Add(RequestSecurity.CsrfHeader, "1");
        return browser;
    }

    private async Task Approve(string approvalId)
    {
        using var windows = Native("windows");
        Assert.Equal(HttpStatusCode.OK, (await windows.PostAsJsonAsync($"api/approvals/{approvalId}", new { approve = true })).StatusCode);
    }

    [Fact] // 13 security.2, security.8
    public async Task Shell_execution_from_the_browser_needs_approval_in_the_windows_client()
    {
        using var browser = await Browser();
        var first = await browser.PostAsJsonAsync("api/shell/run", new { command = "Get-Date" });
        Assert.Equal((HttpStatusCode)428, first.StatusCode);
        var approval = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("approval").GetProperty("id").GetString()!;
        // Without approval, retrying with the id still fails.
        Assert.Equal((HttpStatusCode)428, (await browser.PostAsJsonAsync("api/shell/run", new { command = "Get-Date", approval })).StatusCode);
        await Approve(approval);
        // The approval covers exactly this command, once.
        Assert.Equal((HttpStatusCode)428, (await browser.PostAsJsonAsync("api/shell/run", new { command = "Get-Process", approval })).StatusCode);
        var run = await browser.PostAsJsonAsync("api/shell/run", new { command = "Get-Date", approval });
        Assert.Equal(HttpStatusCode.Accepted, run.StatusCode);
        var job = await run.Content.ReadFromJsonAsync<JsonElement>();
        // Only the Windows client reports results.
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsJsonAsync($"api/shell/jobs/{job.GetProperty("id").GetString()}/result", new { output = "x", failed = false })).StatusCode);
    }

    [Fact] // 13 security.2 (agent)
    public async Task An_agent_cannot_run_commands_without_a_grant()
    {
        using var agent = Native(agent: "helper");
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsJsonAsync("api/shell/run", new { command = "Get-Date" })).StatusCode);
    }

    [Fact]
    public async Task Granting_permissions_needs_approval_in_the_windows_client()
    {
        using var browser = await Browser();
        var body = new { kind = "Agent", name = "helper", permissions = new[] { "Read", "CreateEdit" } };
        var attempt = await browser.PutAsJsonAsync("api/permissions", body);
        Assert.Equal((HttpStatusCode)428, attempt.StatusCode);
        var approval = (await attempt.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("approval").GetProperty("id").GetString()!;
        await Approve(approval);
        Assert.Equal(HttpStatusCode.OK, (await browser.PutAsJsonAsync("api/permissions", new { body.kind, body.name, body.permissions, approval })).StatusCode);
        using var agent = Native(agent: "helper");
        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsJsonAsync("api/nodes", new { title = "Agent note" })).StatusCode);
    }

    [Fact] // 13 security.3-4
    public async Task Plugins_get_only_their_grants_and_safe_mode_turns_them_off()
    {
        Directory.CreateDirectory(Path.Combine(Session.Vault.Plugins, "hello"));
        File.WriteAllText(Path.Combine(Session.Vault.Plugins, "hello", "plugin.json"), """{ "name": "hello", "version": "1.0.0", "commands": [{ "id": "greet", "title": "Say hello" }] }""");
        File.WriteAllText(Path.Combine(Session.Vault.Plugins, "hello", "main.js"), "quantos.registerCommand('greet', 'Say hello', () => quantos.toast('Hi'));");

        using var browser = await Browser();
        var page = await browser.GetAsync("plugin-host/hello");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var csp = page.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("sandbox allow-scripts", csp);
        Assert.Contains("script-src 'nonce-", csp);

        // Requests made on the plugin's behalf carry its name and are denied without a grant.
        using var asPlugin = new HttpRequestMessage(HttpMethod.Post, "api/nodes") { Content = JsonContent.Create(new { title = "From plugin" }) };
        asPlugin.Headers.Add("X-Quantos-Plugin", "hello");
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.SendAsync(asPlugin)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsJsonAsync("api/system/safe-mode", new { on = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("plugin-host/hello")).StatusCode);
        Session.Permissions.Set(ActorKind.Plugin, "hello", [Permission.Read]);
        using var read = new HttpRequestMessage(HttpMethod.Get, "api/nodes");
        read.Headers.Add("X-Quantos-Plugin", "hello");
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.SendAsync(read)).StatusCode);
    }

    [Fact] // Stage 6: automations triggered by property changes respect permissions
    public void Automations_run_on_property_changes_within_their_permissions()
    {
        File.WriteAllText(Path.Combine(Session.Vault.Root, "automations.json"), """
            { "automations": [
              { "name": "Stamp done", "when": { "type": "task", "property": "status", "to": "done" },
                "then": [ { "set": { "done_on": "{{today}}" } }, { "notify": "{{title}} is done" }, { "archive": true } ] }
            ] }
            """);
        var task = Session.Tasks.Create(new Core.Tasks.CreateTaskRequest { Title = "File taxes" }, Actor.User);
        Session.Tasks.SetStatus(task.Id, "done", Actor.User);
        var updated = Session.Writes.Require(task.Id);
        Assert.Equal(Session.Clock.Today.ToString("yyyy-MM-dd"), updated.Properties["done_on"]);
        Assert.Contains(Session.Notifications.List(), n => n.Body == "File taxes is done");
        // Archiving needs a grant the automation doesn't have, so it stops there and says why.
        Assert.False(updated.Archived);
        Assert.Contains(Session.Automations.Recent, r => r.Outcome.StartsWith("stopped"));
    }
}
