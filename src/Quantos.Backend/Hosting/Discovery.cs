using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Quantos.Core;
using Quantos.Core.Storage;

namespace Quantos.Backend.Hosting;

public sealed record DiscoveryInfo(int Port, int Pid, string Token, string Url, string? Vault, DateTimeOffset Started, string Version);

/// <summary>
/// client-data/settings/backend.json: how clients, the CLI and capture windows find the running
/// backend (BUILD-PLAN section 3E). Readable by the current user only.
/// </summary>
public static class Discovery
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static void Write(QuantosPaths paths, DiscoveryInfo info)
    {
        paths.EnsureCreated();
        AtomicFile.WriteAllText(paths.DiscoveryFile, JsonSerializer.Serialize(info, Json));
        if (OperatingSystem.IsWindows()) RestrictToCurrentUser(paths.DiscoveryFile);
    }

    public static DiscoveryInfo? Read(QuantosPaths paths)
    {
        try
        {
            return File.Exists(paths.DiscoveryFile)
                ? JsonSerializer.Deserialize<DiscoveryInfo>(File.ReadAllText(paths.DiscoveryFile), Json)
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public static void Remove(QuantosPaths paths, int pid)
    {
        var current = Read(paths);
        if (current is not null && current.Pid == pid) File.Delete(paths.DiscoveryFile);
    }

    /// <summary>A discovery file is live when its process exists and the backend answers with the token.</summary>
    public static async Task<bool> IsAliveAsync(DiscoveryInfo info, CancellationToken ct = default)
    {
        try { using var _ = Process.GetProcessById(info.Pid); }
        catch (ArgumentException) { return false; }
        try
        {
            // Generous: a slow answer from a busy machine must not look like a dead backend and start a second one.
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(HttpMethod.Get, info.Url + "api/ping");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", info.Token);
            using var response = await http.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }

    [SupportedOSPlatform("windows")]
    private static void RestrictToCurrentUser(string path)
    {
        try
        {
            var user = WindowsIdentity.GetCurrent().User;
            if (user is null) return;
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { }
    }
}
