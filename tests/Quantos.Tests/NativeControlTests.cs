using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Quantos.Backend.Hosting;
using Quantos.Core;

namespace Quantos.Tests;

public sealed class NativeControlTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Pipe_native_authority_depends_on_the_os_peer_executable(bool trusted)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var data = new TestVault();
        var path = new QuantosPaths(Path.Combine(data.Root, "native-control"));
        var address = new ServerAddress { Port = 47899 };
        var tokens = new AccessTokens();
        var executable = Environment.ProcessPath!;
        using var server = new NativeControlServer(path, address, tokens, NullLogger<NativeControlServer>.Instance,
            windowsExecutable: trusted ? executable : Path.Combine(data.Root, "untrusted", "Quantos.exe"));
        await server.StartAsync(CancellationToken.None);
        try
        {
            if (trusted)
            {
                var reply = await NativeControl.RequestAsync(path.ClientData, Environment.ProcessId, NativeControl.WindowsClient, [executable], CancellationToken.None);
                Assert.True(reply.Ok);
                Assert.NotNull(reply.Connection);
                Assert.Equal("windows-client", tokens.NativeActor(reply.Connection.Token)!.Name);
                Assert.Equal(address.Url, reply.Connection.Url);
            }
            else
                await Assert.ThrowsAsync<IOException>(() => NativeControl.RequestAsync(path.ClientData, Environment.ProcessId, NativeControl.WindowsClient, [executable], CancellationToken.None));
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task A_pipe_name_alone_cannot_impersonate_the_expected_backend_process()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var data = new TestVault();
        var paths = new QuantosPaths(Path.Combine(data.Root, "native-control"));
        var executable = Environment.ProcessPath!;
        using var server = new NativeControlServer(paths, new ServerAddress { Port = 47899 }, new AccessTokens(), NullLogger<NativeControlServer>.Instance, windowsExecutable: executable);
        await server.StartAsync(CancellationToken.None);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => NativeControl.RequestAsync(paths.ClientData, Environment.ProcessId + 1, NativeControl.WindowsClient, [executable], CancellationToken.None));
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Reconnected_native_credentials_can_sign_in_without_discovery_authority()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var data = new TestVault();
        var paths = new QuantosPaths(Path.Combine(data.Root, "backend"));
        var tokens = new AccessTokens();
        await using var app = Quantos.Backend.Program.Build([], new BackendOptions { Vault = Path.Combine(data.Root, "backend-vault"), Port = 0 }, paths, tokens);
        await app.StartAsync();
        var address = (ServerAddress)app.Services.GetService(typeof(ServerAddress))!;
        var pipePaths = new QuantosPaths(Path.Combine(data.Root, "verified-pipe"));
        var executable = Environment.ProcessPath!;
        using var server = new NativeControlServer(pipePaths, address, tokens, NullLogger<NativeControlServer>.Instance, windowsExecutable: executable);
        await server.StartAsync(CancellationToken.None);
        try
        {
            var reply = await NativeControl.RequestAsync(pipePaths.ClientData, Environment.ProcessId, NativeControl.WindowsClient, [executable], CancellationToken.None);
            using var native = new HttpClient { BaseAddress = new Uri(reply.Connection!.Url) };
            native.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", reply.Connection.Token);
            var response = await native.PostAsJsonAsync("api/auth/code", new { kind = "app" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var code = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
            Assert.Equal(SessionKind.App, tokens.Redeem(code)!.Kind);
            Assert.DoesNotContain(reply.Connection.Token, File.ReadAllText(paths.DiscoveryFile));
        }
        finally { await server.StopAsync(CancellationToken.None); await app.StopAsync(); }
    }
}
