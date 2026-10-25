using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Quantos.Core.Sync;

namespace Quantos.Tests;

public sealed class ServerTests
{
    [Fact]
    public async Task Server_requires_auth_and_conditional_writes_and_keeps_identity_after_restart()
    {
        var root = Path.Combine(Path.GetTempPath(), "quantos-server-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var tokenFile = Path.Combine(root, "server-token");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await File.WriteAllTextAsync(tokenFile, token);
        var mcpToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await File.WriteAllTextAsync(Path.Combine(root, "mcp-token"), mcpToken);
        string? serverId = null;
        try
        {
            for (var iteration = 0; iteration < 2; iteration++)
            {
                await using var app = Quantos.Server.Program.Build(["--urls", "http://127.0.0.1:0"], root, tokenFile);
                await app.StartAsync();
                var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                using var http = new HttpClient { BaseAddress = new Uri(address) };
                Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("health")).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("sync/manifest")).StatusCode);
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var manifest = await http.GetFromJsonAsync<JsonElement>("sync/manifest");
                var identity = manifest.GetProperty("serverId").GetString();
                if (iteration == 0) serverId = identity; else Assert.Equal(serverId, identity);
                using (var mcp = new HttpClient { BaseAddress = new Uri(address) })
                {
                    mcp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mcpToken);
                    mcp.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                    mcp.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
                    mcp.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-11-25");
                    Assert.Equal(HttpStatusCode.Unauthorized, (await mcp.GetAsync("sync/manifest")).StatusCode);
                    Assert.Equal(HttpStatusCode.Unauthorized, (await mcp.PutAsJsonAsync("sync/mcp", new { writeAccess = true })).StatusCode);
                    var init = await Rpc(mcp, "initialize", new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "test", version = "1" } });
                    Assert.True(init.TryGetProperty("result", out _), init.ToString());
                    var tools = await Rpc(mcp, "tools/list", new { });
                    Assert.Equal(9, tools.GetProperty("result").GetProperty("tools").GetArrayLength());
                    if (iteration == 0)
                    {
                        var denied = await Rpc(mcp, "tools/call", new { name = "create_note", arguments = new { title = "Denied" } });
                        Assert.True(denied.GetProperty("result").GetProperty("isError").GetBoolean());
                        (await http.PutAsJsonAsync("sync/mcp", new { writeAccess = true })).EnsureSuccessStatusCode();
                        var created = await Rpc(mcp, "tools/call", new { name = "create_note", arguments = new { title = "Written through MCP", body = "Shared server content" } });
                        Assert.False(created.GetProperty("result").TryGetProperty("isError", out var failed) && failed.GetBoolean(), created.ToString());
                        var read = await Rpc(mcp, "tools/call", new { name = "read_note", arguments = new { note = "Written through MCP" } });
                        Assert.Contains("Shared server content", read.ToString());
                    }
                    else
                    {
                        var config = await http.GetFromJsonAsync<JsonElement>("sync/mcp");
                        Assert.True(config.GetProperty("writeAccess").GetBoolean());
                        (await http.PutAsJsonAsync("sync/mcp", new { writeAccess = false })).EnsureSuccessStatusCode();
                        var denied = await Rpc(mcp, "tools/call", new { name = "append_to_note", arguments = new { note = "Written through MCP", text = "Denied after revocation" } });
                        Assert.True(denied.GetProperty("result").GetProperty("isError").GetBoolean());
                    }
                }
                using (var origin = new HttpRequestMessage(HttpMethod.Get, "sync/manifest"))
                {
                    origin.Headers.Add("Origin", address);
                    Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(origin)).StatusCode);
                }
                Assert.Equal(HttpStatusCode.BadRequest, (await http.PutAsync("sync/file?path=settings.json", new StringContent("{}"))).StatusCode);
                using (var outside = new HttpRequestMessage(HttpMethod.Put, "sync/file?path=..%2Foutside.txt"))
                {
                    outside.Headers.TryAddWithoutValidation("If-Match", "\"missing\""); outside.Content = new StringContent("no");
                    Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(outside)).StatusCode);
                }
                var path = "attachments/ab/test.txt";
                if (iteration == 0)
                {
                    using var write = new HttpRequestMessage(HttpMethod.Put, "sync/file?path=" + Uri.EscapeDataString(path));
                    write.Headers.TryAddWithoutValidation("If-Match", "\"missing\""); write.Content = new StringContent("kept");
                    Assert.Equal(HttpStatusCode.NoContent, (await http.SendAsync(write)).StatusCode);
                }
                using (var stale = new HttpRequestMessage(HttpMethod.Delete, "sync/file?path=" + Uri.EscapeDataString(path)))
                {
                    stale.Headers.TryAddWithoutValidation("If-Match", "\"missing\"");
                    Assert.Equal(HttpStatusCode.Conflict, (await http.SendAsync(stale)).StatusCode);
                }
                using (var read = new HttpRequestMessage(HttpMethod.Get, "sync/file?path=" + Uri.EscapeDataString(path)))
                {
                    read.Headers.TryAddWithoutValidation("If-Match", '"' + SyncFiles.Hash(Encoding.UTF8.GetBytes("kept")) + '"');
                    Assert.Equal("kept", await (await http.SendAsync(read)).Content.ReadAsStringAsync());
                }
                await app.StopAsync();
            }
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    private static async Task<JsonElement> Rpc(HttpClient client, string method, object parameters)
    {
        using var response = await client.PostAsJsonAsync("mcp", new { jsonrpc = "2.0", id = 1, method, @params = parameters });
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            text = text.Split('\n').Last(l => l.StartsWith("data: "))[6..];
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
