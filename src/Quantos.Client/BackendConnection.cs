using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace Quantos.Client;

/// <summary>
/// Finds the backend that owns the vault, or starts one (BUILD-PLAN.md 3.E: one backend per vault;
/// extra windows connect to it instead of becoming a second writer).
/// </summary>
internal sealed class BackendConnection : IDisposable
{
    private sealed record RuntimeInfo(int Port, string Token, int Pid, string Vault, string Url);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private BackendConnection(string baseUrl, string token, Process? owned)
    {
        BaseUrl = baseUrl;
        Token = token;
        OwnedProcess = owned;
    }

    public string BaseUrl { get; }
    public string Token { get; }

    /// <summary>The backend this client started. Null when connected to one that was already running.</summary>
    public Process? OwnedProcess { get; }

    public static string ClientDataPath()
    {
        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(appDir);
        return string.Equals(Path.GetFileName(appDir), "application", StringComparison.OrdinalIgnoreCase) && parent is not null
            ? Path.Combine(parent, "client-data")
            : Path.Combine(appDir, "client-data");
    }

    public static async Task<BackendConnection> ConnectOrStartAsync(string[] passThroughArgs)
    {
        var clientData = ClientDataPath();
        var runtimeFile = Path.Combine(clientData, "runtime", "backend.json");

        var existing = ReadRuntime(runtimeFile);
        if (existing is not null && await IsAliveAsync(existing))
            return new BackendConnection(existing.Url, existing.Token, owned: null);

        var exe = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Quantos.Backend.exe" : "Quantos.Backend");
        if (!File.Exists(exe))
            throw new InvalidOperationException($"Quantos.Backend.exe wasn't found next to Quantos.exe ({AppContext.BaseDirectory}). Keep both files in the same folder.");

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        psi.ArgumentList.Add("--no-browser");
        psi.ArgumentList.Add("--client-data");
        psi.ArgumentList.Add(clientData);
        foreach (var a in passThroughArgs) psi.ArgumentList.Add(a);

        var process = Process.Start(psi) ?? throw new InvalidOperationException("The Quantos backend couldn't be started.");
        var stderr = new System.Text.StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) stderr.AppendLine(e.Data); };
        process.OutputDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                string message;
                lock (stderr) message = stderr.ToString().Trim();
                throw new InvalidOperationException(string.IsNullOrEmpty(message) ? $"The Quantos backend stopped while starting (exit code {process.ExitCode})." : message);
            }
            var info = ReadRuntime(runtimeFile);
            if (info is not null && info.Pid == process.Id && await IsAliveAsync(info))
                return new BackendConnection(info.Url, info.Token, process);
            await Task.Delay(250);
        }
        try { process.Kill(); } catch (InvalidOperationException) { }
        throw new TimeoutException("The Quantos backend didn't start within 30 seconds. See client-data\\logs for details.");
    }

    /// <summary>Asks the backend to finish in-flight saves and stop. Only used for a backend this client owns.</summary>
    public async Task ShutdownOwnedAsync()
    {
        if (OwnedProcess is null || OwnedProcess.HasExited) return;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/shutdown");
            request.Headers.Add("X-Quantos-Token", Token);
            await _http.SendAsync(request);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await OwnedProcess.WaitForExitAsync(cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // Last resort only if a safe shutdown didn't happen in time.
            if (!OwnedProcess.HasExited) OwnedProcess.Kill();
        }
    }

    private static RuntimeInfo? ReadRuntime(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<RuntimeInfo>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; }
    }

    private static async Task<bool> IsAliveAsync(RuntimeInfo info)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{info.Url}/api/status");
            request.Headers.Add("X-Quantos-Token", info.Token);
            using var response = await http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }

    public void Dispose() => _http.Dispose();
}
