using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Quantos.Backend.Hosting;
using Quantos.Core;

namespace Quantos.Tests;

public sealed class SyncCoordinatorTests
{
    [Fact]
    public async Task Disconnect_cancels_a_stalled_connection_without_stopping_the_local_app()
    {
        // Desktop credentials use Windows DPAPI. Android supplies its own Keystore protector.
        if (!OperatingSystem.IsWindows()) return;
        using var data = new TestVault();
        var folder = Path.Combine(data.Root, "backend-client");
        var tokens = new AccessTokens();
        await using var app = Quantos.Backend.Program.Build([], new BackendOptions { Vault = Path.Combine(data.Root, "backend-vault"), ClientData = folder, Port = 0 }, new QuantosPaths(folder), tokens);
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<ServerAddress>().Url) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.LaunchToken);
        http.DefaultRequestHeaders.Add("X-Quantos-Client", "windows");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            (await http.PutAsJsonAsync("api/sync/", new { url = $"https://127.0.0.1:{port}", token = new string('a', 64) })).EnsureSuccessStatusCode();
            var attempt = http.PostAsJsonAsync("api/sync/run", new { });
            using var stalled = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync("api/sync/").WaitAsync(TimeSpan.FromSeconds(5))).StatusCode);
            await attempt.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("api/ping")).StatusCode);
            Assert.False(File.Exists(Path.Combine(folder, "settings/sync.json")));
        }
        finally { listener.Stop(); await app.StopAsync(); }
    }
}
