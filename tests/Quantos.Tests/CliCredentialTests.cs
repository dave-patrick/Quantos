using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Security;

namespace Quantos.Tests;

public sealed class CliCredentialTests
{
    [Fact]
    public async Task Supplying_an_agent_credential_stays_restricted_without_agent_arguments_even_when_a_cli_cache_exists()
    {
        using var data = new TestVault();
        var paths = new QuantosPaths(Path.Combine(data.Root, "backend"));
        await using var app = Quantos.Backend.Program.Build([], new BackendOptions { Vault = Path.Combine(data.Root, "backend-vault"), Port = 0 }, paths);
        await app.StartAsync();
        try
        {
            var credentials = app.Services.GetRequiredService<ClientCredentials>();
            ClientCredentialFile.Write(ClientCredentialFile.PathFor(paths.ClientData, "Cli", "cli"), new("Cli", "cli", credentials.Issue(ActorKind.Cli, "cli").Token));
            var agent = credentials.Issue(ActorKind.Agent, "scoped-agent");
            using var process = StartCli(paths.ClientData, agent.Token, "new", "must not be created", "--json");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(4, process.ExitCode);
            Assert.Contains("isn't allowed", await stderr);
            Assert.DoesNotContain(agent.Token, await stdout);
            Assert.Empty(app.Services.GetRequiredService<VaultHost>().Require().Cache.List());
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task First_cli_pairing_waits_for_a_native_decision_and_reuses_the_cached_identity()
    {
        using var data = new TestVault();
        var paths = new QuantosPaths(Path.Combine(data.Root, "backend"));
        var tokens = new AccessTokens();
        await using var app = Quantos.Backend.Program.Build([], new BackendOptions { Vault = Path.Combine(data.Root, "backend-vault"), Port = 0 }, paths, tokens);
        await app.StartAsync();
        try
        {
            var session = app.Services.GetRequiredService<VaultHost>().Require();
            using var process = StartCli(paths.ClientData, null, "status", "--json", "--agent", "paired-from-cli");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            Approval? pending = null;
            for (var i = 0; i < 100 && pending is null && !process.HasExited; i++)
            {
                pending = session.Approvals.Pending().SingleOrDefault();
                if (pending is null) await Task.Delay(100);
            }
            Assert.NotNull(pending);
            Assert.False(process.HasExited);
            using var native = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<ServerAddress>().Url) };
            native.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.IssueNativeToken("windows"));
            (await native.PostAsJsonAsync("api/approvals/" + pending.Id, new { approve = true })).EnsureSuccessStatusCode();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(0, process.ExitCode);
            Assert.Contains("Confirm code", await stderr);
            Assert.Equal("paired-from-cli", JsonDocument.Parse(await stdout).RootElement.GetProperty("actor").GetProperty("name").GetString());
            using var second = StartCli(paths.ClientData, null, "status", "--json", "--agent", "paired-from-cli");
            var again = second.StandardOutput.ReadToEndAsync();
            var againError = second.StandardError.ReadToEndAsync();
            await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(0, second.ExitCode);
            Assert.Equal("", await againError);
            Assert.Equal("paired-from-cli", JsonDocument.Parse(await again).RootElement.GetProperty("actor").GetProperty("name").GetString());
        }
        finally { await app.StopAsync(); }
    }

    private static Process StartCli(string clientData, string? token, params string[] args)
    {
        var config = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var program = Path.Combine(root, "src", "Quantos.Cli", "bin", config, "net10.0", OperatingSystem.IsWindows() ? "quantos-cli.exe" : "quantos-cli");
        var start = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("QUANTOS_ACCESS_TOKEN");
        if (token is not null) start.Environment["QUANTOS_ACCESS_TOKEN"] = token;
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.ArgumentList.Add("--client-data"); start.ArgumentList.Add(clientData);
        return Process.Start(start)!;
    }
}
