using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Security.Cryptography;
using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Security;

namespace Quantos.Tests;

public sealed class CredentialSecurityTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "quantos-tests", Guid.NewGuid().ToString("N"));
    private readonly AccessTokens tokens = new();
    private WebApplication app = null!;
    private string url = "";
    private VaultSession Session => app.Services.GetRequiredService<VaultHost>().Require();

    public async Task InitializeAsync()
    {
        app = Quantos.Backend.Program.Build([], new BackendOptions { Vault = Path.Combine(root, "Vault"), Port = 0 }, new QuantosPaths(Path.Combine(root, "client-data")), tokens);
        await app.StartAsync();
        url = app.Services.GetRequiredService<ServerAddress>().Url;
    }

    public async Task DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(root, true);
    }

    private HttpClient DiscoveryClient(string? client = null, string? agent = null)
    {
        var http = new HttpClient { BaseAddress = new Uri(url) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.LaunchToken);
        if (client is not null) http.DefaultRequestHeaders.Add(RequestSecurity.ClientHeader, client);
        if (agent is not null) http.DefaultRequestHeaders.Add(RequestSecurity.AgentHeader, agent);
        return http;
    }

    private HttpClient CredentialClient(string credential)
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(url) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        return http;
    }

    private ClientCredentials Credentials => app.Services.GetRequiredService<ClientCredentials>();

    [Fact]
    public async Task Revocation_closes_an_already_authenticated_event_stream()
    {
        var credential = Credentials.Issue(ActorKind.Agent, "event-reader");
        using var agent = CredentialClient(credential.Token);
        using var stream = await agent.GetStreamAsync("api/events");
        using var reader = new StreamReader(stream);
        Assert.Equal("event: hello", await reader.ReadLineAsync());
        await reader.ReadLineAsync();
        await reader.ReadLineAsync();
        Assert.True(Credentials.Revoke(credential.Id));
        Assert.Null(await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync("api/nodes")).StatusCode);
    }

    [Fact]
    public void A_pairing_retry_does_not_return_a_revoked_credential()
    {
        var proof = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var request = Credentials.Request(ActorKind.Agent, "retry", proof, Session.Approvals);
        Session.Approvals.Decide(request.Id, true);
        var issued = Credentials.Poll(request.Id, proof, Session.Approvals);
        Assert.Equal("Approved", issued.State);
        Credentials.Revoke(Credentials.Authenticate(issued.Token!)!.CredentialId!);
        var retry = Credentials.Poll(request.Id, proof, Session.Approvals);
        Assert.Equal("Revoked", retry.State);
        Assert.Null(retry.Token);
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("android")]
    public async Task Native_credentials_work_without_identity_headers_and_never_enter_discovery(string platform)
    {
        var token = tokens.IssueNativeToken(platform);
        using var native = CredentialClient(token);
        var identity = await native.GetFromJsonAsync<JsonElement>("api/auth/whoami");
        Assert.True(identity.GetProperty("native").GetBoolean());
        Assert.Equal(platform + "-client", identity.GetProperty("name").GetString());
        Assert.DoesNotContain(token, File.ReadAllText(Path.Combine(root, "client-data", "settings", "backend.json")));
        var approval = Session.Approvals.Request(Actor.Cli, Permission.RunCommands, "marker, no execution");
        Assert.Equal(HttpStatusCode.OK, (await native.PostAsJsonAsync("api/approvals/" + approval.Id, new { approve = true })).StatusCode);
    }

    [Theory]
    [InlineData("ordinary-agent")]
    [InlineData("windows-client")]
    [InlineData("android-client")]
    public async Task Agent_identity_stays_bound_when_headers_are_omitted_or_forged(string name)
    {
        var credential = Credentials.Issue(ActorKind.Agent, name);
        Session.Permissions.Set(ActorKind.Agent, "writer", [Permission.Read, Permission.CreateEdit]);
        using var agent = CredentialClient(credential.Token);
        agent.DefaultRequestHeaders.Add(RequestSecurity.ClientHeader, "windows");
        agent.DefaultRequestHeaders.Add(RequestSecurity.AgentHeader, "writer");
        var identity = await agent.GetFromJsonAsync<JsonElement>("api/auth/whoami");
        Assert.Equal(name, identity.GetProperty("name").GetString());
        Assert.Equal("Agent", identity.GetProperty("kind").GetString());
        Assert.False(identity.GetProperty("native").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsJsonAsync("api/nodes", new { title = "denied" })).StatusCode);
        agent.DefaultRequestHeaders.Remove(RequestSecurity.AgentHeader);
        agent.DefaultRequestHeaders.Remove(RequestSecurity.ClientHeader);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsJsonAsync("api/nodes", new { title = "still denied" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsJsonAsync("api/shell/jobs/marker/result", new { output = "forged", failed = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsJsonAsync("api/auth/code", new { kind = "browser" })).StatusCode);
    }

    [Fact]
    public async Task Personal_cli_has_its_own_authority_but_cannot_become_a_native_app()
    {
        using var cli = CredentialClient(Credentials.Issue(ActorKind.Cli, "cli").Token);
        cli.DefaultRequestHeaders.Add(RequestSecurity.ClientHeader, "windows");
        Assert.Equal(HttpStatusCode.OK, (await cli.PostAsJsonAsync("api/nodes", new { title = "paired CLI" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cli.PostAsJsonAsync("api/auth/code", new { kind = "browser" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cli.PostAsJsonAsync("api/auth/code", new { kind = "app" })).StatusCode);
        var pending = Session.Approvals.Request(Actor.Cli, Permission.RunCommands, "marker, no execution");
        Assert.Equal(HttpStatusCode.Forbidden, (await cli.PostAsJsonAsync("api/approvals/" + pending.Id, new { approve = true })).StatusCode);
    }

    [Fact]
    public async Task Pairing_needs_native_consent_and_the_requesters_private_proof()
    {
        using var bootstrap = DiscoveryClient("windows");
        var proof = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var response = await bootstrap.PostAsJsonAsync("api/auth/credentials/request", new { kind = "Agent", name = "paired-agent", proof });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var before = await (await bootstrap.PostAsJsonAsync("api/auth/credentials/poll", new { id, proof })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", before.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("token").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await bootstrap.PostAsJsonAsync("api/approvals/" + id, new { approve = true })).StatusCode);
        using var native = CredentialClient(tokens.IssueNativeToken("windows"));
        Assert.Equal(HttpStatusCode.OK, (await native.PostAsJsonAsync("api/approvals/" + id, new { approve = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bootstrap.PostAsJsonAsync("api/auth/credentials/poll", new { id, proof = new string('f', 64) })).StatusCode);
        var after = await (await bootstrap.PostAsJsonAsync("api/auth/credentials/poll", new { id, proof })).Content.ReadFromJsonAsync<JsonElement>();
        var credential = after.GetProperty("token").GetString()!;
        Assert.Equal("Approved", after.GetProperty("state").GetString());
        using var agent = CredentialClient(credential);
        var identity = await agent.GetFromJsonAsync<JsonElement>("api/auth/whoami");
        Assert.Equal("paired-agent", identity.GetProperty("name").GetString());
        Assert.DoesNotContain(credential, File.ReadAllText(Path.Combine(root, "client-data", "settings", "connection-identities.json")));
        var metadata = await native.GetStringAsync("api/auth/credentials");
        Assert.DoesNotContain(credential, metadata);
        Assert.DoesNotContain("hash", metadata, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Revocation_requires_a_bound_native_approval_and_stops_existing_credentials()
    {
        var credential = Credentials.Issue(ActorKind.Agent, "revoked-agent");
        using var agent = CredentialClient(credential.Token);
        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync("api/nodes")).StatusCode);
        var session = tokens.Redeem(tokens.IssueCode(SessionKind.App))!;
        using var person = new HttpClient { BaseAddress = new Uri(url) };
        person.DefaultRequestHeaders.Add("Cookie", AccessTokens.CookieName + "=" + session.Id);
        person.DefaultRequestHeaders.Add(RequestSecurity.CsrfHeader, "1");
        var path = "api/auth/credentials/" + credential.Id + "/revoke";
        var attempt = await person.PostAsJsonAsync(path, new { });
        Assert.Equal((HttpStatusCode)428, attempt.StatusCode);
        var approval = (await attempt.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("approval").GetProperty("id").GetString()!;
        using var native = CredentialClient(tokens.IssueNativeToken("windows"));
        await native.PostAsJsonAsync("api/approvals/" + approval, new { approve = true });
        Assert.Equal(HttpStatusCode.NoContent, (await person.PostAsJsonAsync(path, new { approval })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync("api/nodes")).StatusCode);
    }

    [Fact]
    public void Credentials_survive_backend_restart_but_native_credentials_do_not()
    {
        var credential = Credentials.Issue(ActorKind.Agent, "persistent");
        var loaded = new ClientCredentials(new QuantosPaths(Path.Combine(root, "client-data")));
        Assert.Equal("persistent", loaded.Authenticate(credential.Token)!.Name);
        var native = tokens.IssueNativeToken("windows");
        Assert.Null(new AccessTokens().NativeActor(native));
        loaded.Revoke(credential.Id);
        Assert.Null(new ClientCredentials(new QuantosPaths(Path.Combine(root, "client-data"))).Authenticate(credential.Token));
    }

    [Fact]
    public async Task A_bad_bearer_credential_cannot_fall_back_to_a_persons_cookie()
    {
        using var http = CredentialClient("invalid");
        var session = tokens.Redeem(tokens.IssueCode(SessionKind.App))!;
        http.DefaultRequestHeaders.Add("Cookie", AccessTokens.CookieName + "=" + session.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("api/nodes")).StatusCode);
    }

    [Fact]
    public async Task Create_edit_alone_cannot_register_outside_paths_or_write_link_configuration()
    {
        var directory = Path.Combine(root, "outside-marker");
        Directory.CreateDirectory(directory);
        Session.Permissions.Set(ActorKind.Agent, "editor", [Permission.Read, Permission.CreateEdit]);
        using var agent = CredentialClient(Credentials.Issue(ActorKind.Agent, "editor").Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsJsonAsync("api/linked-folders", new { name = "outside", path = directory })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PutAsJsonAsync("api/config/linked-folders.json", new { folders = new[] { new { name = "outside", path = directory } } })).StatusCode);
        Assert.Empty(Session.Vault.LinkedRoots);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
    }

    [Fact]
    public async Task An_explicit_outside_files_grant_still_allows_pairing_an_outside_folder()
    {
        var directory = Path.Combine(root, "outside-marker");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "marker.txt"), "marker");
        Session.Permissions.Set(ActorKind.Agent, "outside-editor", [Permission.Read, Permission.CreateEdit, Permission.OutsideFiles]);
        using var agent = CredentialClient(Credentials.Issue(ActorKind.Agent, "outside-editor").Token);
        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsJsonAsync("api/linked-folders", new { name = "outside", path = directory })).StatusCode);
        Assert.Equal("marker", await agent.GetStringAsync("api/files/linked/outside/marker.txt"));
    }

    [Fact]
    public async Task The_file_endpoint_never_serves_device_credentials_through_a_registered_link()
    {
        Session.Vault.RegisterLinkedFolder("private-device-data", Path.Combine(root, "client-data"));
        using var native = CredentialClient(tokens.IssueNativeToken("windows"));
        Assert.Equal(HttpStatusCode.NotFound, (await native.GetAsync("api/files/linked/private-device-data/settings/backend.json")).StatusCode);
    }

    [Fact]
    public void Cached_credentials_are_private_and_windows_secrets_are_device_protected()
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var path = ClientCredentialFile.PathFor(Path.Combine(root, "client-data"), "Agent", "cached-agent");
        ClientCredentialFile.Write(path, new("Agent", "cached-agent", token));
        Assert.Equal(token, ClientCredentialFile.Read(path)!.Token);
        if (OperatingSystem.IsWindows()) Assert.DoesNotContain(token, File.ReadAllText(path));
        else Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("windows")]
    [InlineData("android")]
    [InlineData("cli")]
    public async Task Discovery_credentials_never_grant_content_writes_by_changing_headers(string? client)
    {
        using var http = DiscoveryClient(client);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync("api/nodes", new { title = "must not be written" })).StatusCode);
        Assert.Equal(0, Session.Cache.Count());
    }

    [Theory]
    [InlineData("browser")]
    [InlineData("app")]
    public async Task Discovery_credentials_cannot_mint_a_privileged_browser_session(string kind)
    {
        using var http = DiscoveryClient("windows");
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync("api/auth/code", new { kind })).StatusCode);
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("android")]
    public async Task Discovery_credentials_cannot_approve_a_request(string platform)
    {
        var pending = Session.Approvals.Request(new Actor(ActorKind.Agent, "probe"), Permission.RunCommands, "audit marker; never execute");
        using var http = DiscoveryClient(platform);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync("api/approvals/" + pending.Id, new { approve = true })).StatusCode);
        Assert.Equal(ApprovalState.Pending, pending.State);
    }

    [Fact]
    public void An_agent_named_windows_client_is_not_a_native_windows_client()
    {
        var ctx = new DefaultHttpContext();
        ctx.Items[RequestSecurity.ActorKey] = new Actor(ActorKind.Agent, "windows-client");
        ctx.Items[RequestSecurity.NativeKey] = true;
        Assert.False(ctx.IsWindowsClient());
        Assert.False(ctx.IsAppClient());
    }

    [Fact]
    public void Approvals_are_bound_to_the_authenticated_actor_and_requesting_instance()
    {
        var first = new Actor(ActorKind.Agent, "first", Instance: "one", CredentialId: "credential-one");
        var second = new Actor(ActorKind.Agent, "second", Instance: "one");
        Session.Permissions.Set(ActorKind.Agent, "first", [Permission.RunCommands]);
        Session.Permissions.Set(ActorKind.Agent, "second", [Permission.RunCommands]);
        var required = Assert.Throws<ApprovalRequiredException>(() => Session.Permissions.Demand(first, Permission.RunCommands, "marker command"));
        Session.Approvals.Decide(required.Approval.Id, true);
        Assert.Throws<ApprovalRequiredException>(() => Session.Permissions.Demand(second, Permission.RunCommands, "marker command", required.Approval.Id));
        Assert.Throws<ApprovalRequiredException>(() => Session.Permissions.Demand(first with { Instance = "two" }, Permission.RunCommands, "marker command", required.Approval.Id));
        Assert.Throws<ApprovalRequiredException>(() => Session.Permissions.Demand(first with { CredentialId = "credential-two" }, Permission.RunCommands, "marker command", required.Approval.Id));
        Session.Permissions.Demand(first, Permission.RunCommands, "marker command", required.Approval.Id);
        Assert.Equal(ApprovalState.Used, required.Approval.State);
    }
}
