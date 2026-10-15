using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Quantos.Core;

namespace Quantos.Backend.Hosting;

public sealed record CheckResult(string Name, string Status, string Detail);

/// <summary>
/// The Stage 1 laptop test (BUILD-PLAN section 11, Stage 1 step 1): checks that this machine lets
/// Quantos run without admin rights. Prints a report and saves it to client-data/logs.
/// </summary>
public static class SelfCheck
{
    public static IReadOnlyList<CheckResult> Run(QuantosPaths paths)
    {
        var results = new List<CheckResult>
        {
            Check("Runtime", () => $"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}"),
            Check("Running as administrator", () => IsElevated() ? "warn:yes - Quantos doesn't need admin rights; test without them" : "no (good)"),
            Check("Executable location", () => Environment.ProcessPath ?? AppContext.BaseDirectory),
            Check("Mark of the Web", () =>
            {
                var exe = Environment.ProcessPath;
                if (exe is null) return "unknown";
                return File.Exists(exe + ":Zone.Identifier")
                    ? "warn:present - the file came from the internet or email; SmartScreen may prompt. Unblock it in File Properties if needed."
                    : "not present";
            }),
            Check("Client data folder", () =>
            {
                paths.EnsureCreated();
                var probe = Path.Combine(paths.ClientData, ".probe");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return $"writable: {paths.ClientData}";
            }),
            Check("SQLite native library", () =>
            {
                using var db = new SqliteConnection("Data Source=:memory:");
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = "CREATE VIRTUAL TABLE t USING fts5(x); SELECT sqlite_version();";
                return "loaded, FTS5 available, SQLite " + cmd.ExecuteScalar();
            }),
            Check("Single-file extraction", () =>
            {
                var extract = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR");
                return extract is null ? "default (%TEMP%\\.net); native libraries loaded above" : $"custom: {extract}";
            }),
            Check("Loopback listener", () =>
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                using var client = new TcpClient();
                client.Connect(IPAddress.Loopback, port);
                listener.Stop();
                return $"127.0.0.1 accepts local connections (tested port {port})";
            }),
            Check("Proxy for local addresses", () =>
            {
                var proxy = HttpClient.DefaultProxy;
                var target = new Uri("http://127.0.0.1:47821/");
                return proxy.IsBypassed(target) || proxy.GetProxy(target) is null || proxy.GetProxy(target) == target
                    ? "local requests bypass the proxy"
                    : $"warn:local requests would go through {proxy.GetProxy(target)}";
            }),
            Check("WebView2 runtime", WebView2Version),
            Check("PowerShell language mode", PowerShellLanguageMode),
            Check("Default vault location", () =>
            {
                var check = VaultHost.Check(QuantosPaths.DefaultVault);
                return check.Error is not null ? "fail:" + check.Error
                    : check.Warning is not null ? "warn:" + check.Warning
                    : $"writable: {check.Path}";
            })
        };

        try
        {
            paths.EnsureCreated();
            File.WriteAllText(Path.Combine(paths.Logs, $"selfcheck-{DateTime.Now:yyyyMMdd-HHmmss}.json"),
                JsonSerializer.Serialize(results, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
        catch (IOException) { }
        return results;
    }

    public static void Print(IReadOnlyList<CheckResult> results, TextWriter output)
    {
        output.WriteLine("Quantos self-check");
        output.WriteLine(new string('=', 60));
        foreach (var r in results)
            output.WriteLine($"[{r.Status.ToUpperInvariant(),-4}] {r.Name}: {r.Detail}");
        output.WriteLine(new string('=', 60));
        var failed = results.Count(r => r.Status == "fail");
        var warned = results.Count(r => r.Status == "warn");
        output.WriteLine(failed == 0 ? $"Passed with {warned} warning(s)." : $"{failed} check(s) failed. See BUILD-PLAN.md Stage 1 for the fallback.");
    }

    private static CheckResult Check(string name, Func<string> probe)
    {
        try
        {
            var detail = probe();
            if (detail.StartsWith("warn:")) return new CheckResult(name, "warn", detail[5..]);
            if (detail.StartsWith("fail:")) return new CheckResult(name, "fail", detail[5..]);
            return new CheckResult(name, "ok", detail);
        }
        catch (Exception ex)
        {
            return new CheckResult(name, "fail", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static string WebView2Version()
    {
        if (!OperatingSystem.IsWindows()) return "warn:not Windows";
        const string client = @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        foreach (var (hive, key) in new[]
        {
            (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"),
            (Microsoft.Win32.Registry.LocalMachine, client),
            (Microsoft.Win32.Registry.CurrentUser, client)
        })
        {
            using var k = hive.OpenSubKey(key);
            if (k?.GetValue("pv") is string pv && pv.Length > 0 && pv != "0.0.0.0") return $"installed ({pv})";
        }
        return "fail:not found - the Windows client needs the Evergreen WebView2 runtime. Use the browser instead (run Quantos.Backend.exe --open).";
    }

    private static string PowerShellLanguageMode()
    {
        if (!OperatingSystem.IsWindows()) return "warn:not Windows";
        var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"$ExecutionContext.SessionState.LanguageMode\"")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("powershell.exe didn't start");
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit(15000);
        return output switch
        {
            "FullLanguage" => "FullLanguage - the terminal will work normally",
            "ConstrainedLanguage" => "warn:ConstrainedLanguage - app control (WDAC/AppLocker) is enforced; the terminal works but scripts are restricted",
            "" => "warn:couldn't determine",
            _ => "warn:" + output
        };
    }
}
