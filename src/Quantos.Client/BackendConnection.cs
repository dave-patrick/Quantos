using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Quantos.Backend;
using Quantos.Backend.Hosting;
using Quantos.Core;

namespace Quantos.Client;

public sealed record Discovery(int Port, int Pid, string Token, string Url, string? Vault);

/// <summary>
/// Finds the running backend through client-data/settings/backend.json, or starts one (BUILD-PLAN
/// section 3E). The client talks to it with the launch token, which the page never sees.
/// </summary>
public sealed class BackendConnection : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly CancellationTokenSource _stop = new();

    private BackendConnection(Discovery info, Process? owned, string nativeToken)
    {
        Info = info;
        Owned = owned;
        Http = new HttpClient { BaseAddress = new Uri(info.Url), Timeout = TimeSpan.FromSeconds(30) };
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", nativeToken);
    }

    public Discovery Info { get; }
    public Process? Owned { get; }
    public HttpClient Http { get; }
    public string Url => Info.Url;

    /// <summary>In-process Kestrel host when this client started the backend itself.</summary>
    private WebApplication? _ownedApp;

    public static async Task<BackendConnection> ConnectAsync(ClientPaths paths, Action<string> status)
    {
        var existing = Read(paths);
        if (existing is not null && await IsAliveAsync(existing))
        {
            paths.Log($"Connected to running backend {existing.Url} (pid {existing.Pid})");
            var grant = await NativeControl.ConnectWindowsAsync(paths.ClientData, existing.Pid, ClientPaths.AppDirectory);
            return new BackendConnection(existing with { Url = grant.Url, Port = grant.Port }, null, grant.Token);
        }

        // No live backend: host Kestrel in this process so there's no separate service to orphan.
        status("Starting the Quantos backend…");
        var corePaths = new QuantosPaths(paths.ClientData);
        corePaths.EnsureCreated();
        var options = new BackendOptions { ClientData = paths.ClientData, OpenBrowser = false };
        var app = Quantos.Backend.Program.Build(Array.Empty<string>(), options, corePaths);
        await app.StartAsync();

        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            var info = Read(paths);
            if (info is not null && await IsAliveAsync(info))
            {
                paths.Log($"Started in-process backend {info.Url} (process {Environment.ProcessId})");
                return new BackendConnection(info, null, app.Services.GetRequiredService<AccessTokens>().IssueNativeToken("windows")) { _ownedApp = app };
            }
            await Task.Delay(150);
        }
        throw new TimeoutException("The backend didn't answer within 45 seconds.");
    }

    public static Discovery? Read(ClientPaths paths)
    {
        try { return File.Exists(paths.DiscoveryFile) ? JsonSerializer.Deserialize<Discovery>(File.ReadAllText(paths.DiscoveryFile), Json) : null; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>True once the backend's process has gone (crashed or was ended). A busy backend still counts as running.</summary>
    public bool ProcessGone()
    {
        if (Owned is not null) return Owned.HasExited;
        try { using var p = Process.GetProcessById(Info.Pid); return p.HasExited; }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
    }

    private static async Task<bool> IsAliveAsync(Discovery info)
    {
        try { using var _ = Process.GetProcessById(info.Pid); }
        catch (ArgumentException) { return false; }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var req = new HttpRequestMessage(HttpMethod.Get, info.Url + "api/ping");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", info.Token);
            return (await http.SendAsync(req)).IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }

    /// <summary>A one-time sign-in link: "app" for this window, "browser" for the default browser.</summary>
    public async Task<string> SignInUrlAsync(string kind, string? to = null)
    {
        var response = await Http.PostAsJsonAsync("api/auth/code", new { kind, to });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("url").GetString()!;
    }

    public async Task<bool> ShutdownAsync()
    {
        // Ask Kestrel to stop (works whether the backend is in this process or a separate one).
        try { await Http.PostAsync("api/system/shutdown", null); } catch (HttpRequestException) { }
        if (_ownedApp is not null)
        {
            try { await Task.WhenAny(_ownedApp.StopAsync(), Task.Delay(TimeSpan.FromSeconds(10))); } catch { }
            try { await _ownedApp.DisposeAsync(); } catch { }
            _ownedApp = null;
            return true;
        }
        if (Owned is not null && !Owned.HasExited)
            return await Task.Run(() => Owned.WaitForExit(10_000));
        return true;
    }

    /// <summary>Listens to the push channel for reminders and approval requests; reconnects on failure.</summary>
    public void Listen(Action<JsonElement> onChange)
    {
        _ = Task.Run(async () =>
        {
            var delay = 1000;
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    using var http = new HttpClient { BaseAddress = Http.BaseAddress, Timeout = Timeout.InfiniteTimeSpan };
                    http.DefaultRequestHeaders.Authorization = Http.DefaultRequestHeaders.Authorization;
                    using var stream = await http.GetStreamAsync("api/events", _stop.Token);
                    using var reader = new StreamReader(stream);
                    delay = 1000;
                    string? evt = null;
                    while (await reader.ReadLineAsync(_stop.Token) is { } line)
                    {
                        if (line.StartsWith("event: ")) evt = line[7..];
                        else if (line.StartsWith("data: ") && evt == "change")
                        {
                            try { onChange(JsonSerializer.Deserialize<JsonElement>(line[6..])); } catch (JsonException) { }
                        }
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception) { }
                await Task.Delay(delay);
                delay = Math.Min(delay * 2, 15000);
            }
        });
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (_ownedApp is not null)
        {
            try { Task.WhenAny(_ownedApp.StopAsync(), Task.Delay(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult(); } catch { }
            try { _ownedApp.DisposeAsync().GetAwaiter().GetResult(); } catch { }
            _ownedApp = null;
        }
        Http.Dispose();
    }
}
