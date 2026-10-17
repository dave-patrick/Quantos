using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

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

    private BackendConnection(Discovery info, Process? owned)
    {
        Info = info;
        Owned = owned;
        Http = new HttpClient { BaseAddress = new Uri(info.Url), Timeout = TimeSpan.FromSeconds(30) };
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", info.Token);
        Http.DefaultRequestHeaders.Add("X-Quantos-Client", "windows");
    }

    public Discovery Info { get; }
    public Process? Owned { get; }
    public HttpClient Http { get; }
    public string Url => Info.Url;

    public static async Task<BackendConnection> ConnectAsync(ClientPaths paths, Action<string> status)
    {
        var existing = Read(paths);
        if (existing is not null && await IsAliveAsync(existing))
        {
            paths.Log($"Connected to running backend {existing.Url} (pid {existing.Pid})");
            return new BackendConnection(existing, null);
        }

        var exe = FindBackend() ?? throw new InvalidOperationException("Quantos.Backend.exe wasn't found next to Quantos.exe.");
        status("Starting the Quantos backend…");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(exe)!
        };
        psi.ArgumentList.Add("--client-data"); psi.ArgumentList.Add(paths.ClientData);
        psi.ArgumentList.Add("--parent"); psi.ArgumentList.Add(Environment.ProcessId.ToString());
        var process = Process.Start(psi) ?? throw new InvalidOperationException("The backend didn't start.");
        paths.Log($"Started backend {exe} as pid {process.Id}");

        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
                throw new InvalidOperationException($"The backend stopped while starting (exit code {process.ExitCode}). See {paths.Logs}.");
            var info = Read(paths);
            if (info is not null && info.Pid == process.Id && await IsAliveAsync(info)) return new BackendConnection(info, process);
            await Task.Delay(150);
        }
        throw new TimeoutException("The backend didn't answer within 45 seconds.");
    }

    private static string? FindBackend()
    {
        var overridePath = Environment.GetEnvironmentVariable("QUANTOS_BACKEND_EXE");
        if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath)) return overridePath;
        var dir = ClientPaths.AppDirectory;
        var sibling = Path.Combine(dir, "Quantos.Backend.exe");
        if (File.Exists(sibling)) return sibling;
        // Development layout: src/Quantos.Client/bin/... next to src/Quantos.Backend/bin/...
        var dev = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "..", "Quantos.Backend", "bin", "Debug", "net10.0", "Quantos.Backend.exe"));
        return File.Exists(dev) ? dev : null;
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
        if (Owned is null || Owned.HasExited) return true;
        try { await Http.PostAsync("api/system/shutdown", null); } catch (HttpRequestException) { }
        return await Task.Run(() => Owned.WaitForExit(10_000));
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
                    http.DefaultRequestHeaders.Add("X-Quantos-Client", "windows");
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
        Http.Dispose();
    }
}
