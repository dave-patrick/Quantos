using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Quantos.Client;

namespace Quantos.Client.Tests;

public sealed class BackendConnectionTests
{
    [Fact]
    public async Task In_process_native_startup_keeps_discovery_separate_and_signs_in_the_page()
    {
        var root = Path.Combine(Path.GetTempPath(), "quantos-native-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using (var connection = await BackendConnection.ConnectAsync(new ClientPaths(root), _ => { }))
            {
                var identity = await connection.Http.GetFromJsonAsync<JsonElement>("api/auth/whoami");
                Assert.True(identity.GetProperty("native").GetBoolean());
                Assert.Equal("windows-client", identity.GetProperty("name").GetString());
                using var discovery = new HttpClient { BaseAddress = new Uri(connection.Url) };
                discovery.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.Info.Token);
                Assert.Equal(HttpStatusCode.Forbidden, (await discovery.PostAsJsonAsync("api/auth/code", new { kind = "app" })).StatusCode);
                var link = await connection.SignInUrlAsync("app");
                using var browser = new HttpClient();
                await browser.GetAsync(link);
                var pageIdentity = await browser.GetFromJsonAsync<JsonElement>(connection.Url + "api/auth/whoami");
                Assert.False(pageIdentity.GetProperty("native").GetBoolean());
                Assert.Equal("User", pageIdentity.GetProperty("kind").GetString());
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
