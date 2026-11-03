using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Quantos.Backend;
using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Security;

namespace Quantos.Tests;

/// <summary>The app's Streamable HTTP MCP endpoint (POST /mcp), against a real localhost backend.</summary>
public sealed class McpHttpTests : IAsyncLifetime
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
        _url = ((ServerAddress)_app.Services.GetService(typeof(ServerAddress))!).Url;
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private ClientCredentials Credentials => (ClientCredentials)_app.Services.GetService(typeof(ClientCredentials))!;

    private HttpClient Client(string? token)
    {
        var http = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }) { BaseAddress = new Uri(_url) };
        if (token is not null) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private string AgentToken(string name = "http-mcp") => Credentials.Issue(ActorKind.Agent, name).Token;

    private static StringContent Rpc(object message) => new(JsonSerializer.Serialize(message), Encoding.UTF8, "application/json");

    private static async Task<JsonNode> Json(HttpResponseMessage response) => JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    [Fact]
    public async Task Requires_a_paired_agent_connection()
    {
        var message = new { jsonrpc = "2.0", id = 1, method = "ping" };
        using (var anonymous = Client(null))
        {
            var response = await anonymous.PostAsync("mcp", Rpc(message));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
        }
        using (var wrong = Client("0".PadLeft(64, '0')))
            Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.PostAsync("mcp", Rpc(message))).StatusCode);
        // The personal CLI connection and the app's own credential are not agents.
        using (var cli = Client(Credentials.Issue(ActorKind.Cli, "cli").Token))
            Assert.Equal(HttpStatusCode.Forbidden, (await cli.PostAsync("mcp", Rpc(message))).StatusCode);
        using (var native = Client(_tokens.IssueNativeToken("windows")))
            Assert.Equal(HttpStatusCode.Unauthorized, (await native.PostAsync("mcp", Rpc(message))).StatusCode);
        using (var agent = Client(AgentToken()))
            Assert.Equal(HttpStatusCode.OK, (await agent.PostAsync("mcp", Rpc(message))).StatusCode);
    }

    [Fact]
    public async Task Web_pages_and_other_methods_are_refused()
    {
        using var http = Client(AgentToken());
        using var withOrigin = new HttpRequestMessage(HttpMethod.Post, "mcp") { Content = Rpc(new { jsonrpc = "2.0", id = 1, method = "ping" }) };
        withOrigin.Headers.Add("Origin", "http://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(withOrigin)).StatusCode);

        var get = await http.GetAsync("mcp");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        Assert.Contains("POST", get.Content.Headers.Allow.Concat(get.Headers.TryGetValues("Allow", out var v) ? v : []));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await http.DeleteAsync("mcp")).StatusCode);
    }

    [Fact]
    public async Task Handshake_lists_the_nine_tools_and_needs_no_session()
    {
        using var http = Client(AgentToken());
        var init = await Json(await http.PostAsync("mcp", Rpc(new
        {
            jsonrpc = "2.0", id = 1, method = "initialize",
            @params = new { protocolVersion = "2025-03-26", capabilities = new { }, clientInfo = new { name = "test", version = "1" } }
        })));
        Assert.Equal("2025-03-26", init["result"]!["protocolVersion"]!.ToString());
        Assert.Equal("quantos", init["result"]!["serverInfo"]!["name"]!.ToString());

        var future = await Json(await http.PostAsync("mcp", Rpc(new { jsonrpc = "2.0", id = 2, method = "initialize", @params = new { protocolVersion = "2099-01-01" } })));
        Assert.Equal("2025-06-18", future["result"]!["protocolVersion"]!.ToString());

        var notification = await http.PostAsync("mcp", Rpc(new { jsonrpc = "2.0", method = "notifications/initialized" }));
        Assert.Equal(HttpStatusCode.Accepted, notification.StatusCode);
        Assert.Equal("", await notification.Content.ReadAsStringAsync());

        var list = await Json(await http.PostAsync("mcp", Rpc(new { jsonrpc = "2.0", id = 3, method = "tools/list" })));
        var names = list["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.ToString()).ToList();
        Assert.Equal(9, names.Count);
        Assert.Contains("search_notes", names);
        Assert.Contains("create_note", names);

        var unknown = await Json(await http.PostAsync("mcp", Rpc(new { jsonrpc = "2.0", id = 4, method = "resources/list" })));
        Assert.Equal(-32601, unknown["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task An_agent_reads_but_writes_only_with_a_grant()
    {
        using var http = Client(AgentToken("research-bot"));
        var read = await Json(await http.PostAsync("mcp", Rpc(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "search_notes", arguments = new { query = "anything" } } })));
        Assert.Null(read["result"]!["isError"]);

        var create = new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = "create_note", arguments = new { title = "From HTTP", body = "hello", tags = new[] { "demo" } } } };
        var refused = await Json(await http.PostAsync("mcp", Rpc(create)));
        Assert.True(refused["result"]!["isError"]!.GetValue<bool>());
        Assert.Contains("Plugins & permissions", refused["result"]!["content"]![0]!["text"]!.ToString());

        var session = ((VaultHost)_app.Services.GetService(typeof(VaultHost))!).Require();
        session.Permissions.Set(ActorKind.Agent, "research-bot", [Permission.Read, Permission.CreateEdit]);
        var created = await Json(await http.PostAsync("mcp", Rpc(create)));
        Assert.Null(created["result"]!["isError"]);
        Assert.Contains("From HTTP", created["result"]!["content"]![0]!["text"]!.ToString());

        var found = await Json(await http.PostAsync("mcp", Rpc(new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "read_note", arguments = new { note = "From HTTP" } } })));
        Assert.Contains("hello", found["result"]!["content"]![0]!["text"]!.ToString());
    }

    [Fact]
    public async Task Batches_and_event_stream_clients_are_answered()
    {
        using var http = Client(AgentToken());
        var batch = new object[]
        {
            new { jsonrpc = "2.0", id = 1, method = "ping" },
            new { jsonrpc = "2.0", method = "notifications/initialized" },
            new { jsonrpc = "2.0", id = 2, method = "tools/list" }
        };
        var reply = (JsonArray)await Json(await http.PostAsync("mcp", Rpc(batch)));
        Assert.Equal(2, reply.Count);

        using var sse = new HttpRequestMessage(HttpMethod.Post, "mcp") { Content = Rpc(new { jsonrpc = "2.0", id = 5, method = "ping" }) };
        sse.Headers.Accept.ParseAdd("text/event-stream");
        var streamed = await http.SendAsync(sse);
        Assert.Equal("text/event-stream", streamed.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("event: message\ndata: {", await streamed.Content.ReadAsStringAsync());

        var bad = await http.PostAsync("mcp", new StringContent("{not json", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(-32700, (await Json(bad))["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task The_settings_endpoint_creates_a_revocable_connection_for_the_user_only()
    {
        using var app = Client(_tokens.IssueNativeToken("windows"));
        var info = await app.GetFromJsonAsync<JsonElement>("api/mcp-http");
        Assert.EndsWith("/mcp", info.GetProperty("url").GetString());

        // A paired agent cannot mint itself more access.
        using (var agent = Client(AgentToken("sneaky")))
            Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsJsonAsync("api/mcp-http/connection", new { name = "other" })).StatusCode);

        // Creating a connection asks for the same native confirmation as revoking one; approve it, then retry with the approval.
        var response = await app.PostAsJsonAsync("api/mcp-http/connection", new { name = "claude-remote" });
        Assert.Equal((HttpStatusCode)428, response.StatusCode);
        var approvalId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("approval").GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await app.PostAsJsonAsync($"api/approvals/{approvalId}", new { approve = true })).StatusCode);
        response = await app.PostAsJsonAsync("api/mcp-http/connection", new { name = "claude-remote", approval = approvalId });
        response.EnsureSuccessStatusCode();
        var made = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = made.GetProperty("token").GetString()!;
        using var remote = Client(token);
        Assert.Equal(HttpStatusCode.OK, (await remote.PostAsync("mcp", Rpc(new { jsonrpc = "2.0", id = 1, method = "ping" }))).StatusCode);
        Assert.True(Credentials.Revoke(made.GetProperty("id").GetString()!));
        Assert.Equal(HttpStatusCode.Unauthorized, (await remote.PostAsync("mcp", Rpc(new { jsonrpc = "2.0", id = 1, method = "ping" }))).StatusCode);
    }
}
