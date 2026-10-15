using System.Management.Automation;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Data.Sqlite;
using Microsoft.Web.WebView2.Core;

namespace Quantos.Probe;

public enum Outcome { Pass, Warn, Fail }

public sealed record CheckResult(string Name, Outcome Outcome, string Detail);

/// <summary>
/// The Stage 1 laptop test (BUILD-PLAN.md 11, Stage 1 step 1). Each check exercises one thing the
/// real app depends on. Nothing here needs admin rights, and every file it creates is removed again.
/// </summary>
internal static class Checks
{
    public static IEnumerable<(string Name, Func<Task<CheckResult>> Run)> All() =>
    [
        ("Windows version", () => Task.FromResult(WindowsVersion())),
        ("Running without admin rights", () => Task.FromResult(NotAdmin())),
        ("This program was allowed to start", () => Task.FromResult(new CheckResult("This program was allowed to start", Outcome.Pass,
            $"Started from {AppContext.BaseDirectory}. Application control didn't block an unsigned .exe from this folder."))),
        ("Create files next to the app (portable folder)", () => Task.FromResult(WriteTest("Create files next to the app (portable folder)", AppContext.BaseDirectory))),
        ("Create the default vault folder", () => Task.FromResult(WriteTest("Create the default vault folder",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Quantos")))),
        ("Default vault isn't in a synced folder", () => Task.FromResult(SyncedFolder())),
        ("Local web server on 127.0.0.1", LoopbackAsync),
        ("SQLite (cache database)", () => Task.FromResult(Sqlite())),
        ("PowerShell engine", () => Task.FromResult(PowerShellEngine())),
        ("WebView2 runtime", () => Task.FromResult(WebView2Runtime())),
    ];

    private static CheckResult WindowsVersion() =>
        new("Windows version", Outcome.Pass, $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}");

    private static CheckResult NotAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        return admin
            ? new("Running without admin rights", Outcome.Warn, "This test is running as administrator. Run it again normally (not 'Run as administrator') for a true result.")
            : new("Running without admin rights", Outcome.Pass, $"Running as {identity.Name} without elevation.");
    }

    private static CheckResult WriteTest(string name, string folder)
    {
        var dir = Path.Combine(folder, "quantos-probe-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "test.md");
            File.WriteAllText(file, "---\nid: probe\n---\nhello\n");
            var tmp = file + ".qtmp";
            File.WriteAllText(tmp, "replaced");
            File.Move(tmp, file, overwrite: true); // The same atomic replace the write service uses.
            var ok = File.ReadAllText(file) == "replaced";
            return new(name, ok ? Outcome.Pass : Outcome.Fail, ok ? $"Created, replaced and removed files in {folder}." : "File replace didn't work.");
        }
        catch (Exception ex)
        {
            return new(name, Outcome.Fail, $"{folder}: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    private static CheckResult SyncedFolder()
    {
        var vault = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Quantos", "Vault");
        var roots = new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" }
            .Select(Environment.GetEnvironmentVariable).Where(r => !string.IsNullOrEmpty(r)).Cast<string>().ToList();
        var synced = roots.FirstOrDefault(r => vault.StartsWith(r.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var docsSynced = roots.Any(r => docs.StartsWith(r, StringComparison.OrdinalIgnoreCase));
        if (synced is not null)
            return new("Default vault isn't in a synced folder", Outcome.Warn, $"{vault} is inside {synced}. Quantos will warn about this; choose a non-synced vault folder.");
        return new("Default vault isn't in a synced folder", Outcome.Pass,
            $"{vault} is not synced." + (docsSynced ? " Note: your Documents folder IS synced by OneDrive, so don't put the vault there." : ""));
    }

    private static async Task<CheckResult> LoopbackAsync()
    {
        const string name = "Local web server on 127.0.0.1";
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var serve = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var buffer = new byte[1024];
                _ = await stream.ReadAsync(buffer);
                var body = "quantos-ok"u8.ToArray();
                var head = System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head);
                await stream.WriteAsync(body);
            });
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var text = await http.GetStringAsync($"http://127.0.0.1:{port}/");
            await serve;
            var defaultPortFree = PortFree(47821);
            return text == "quantos-ok"
                ? new(name, Outcome.Pass, $"Served and fetched a page on port {port}. Default Quantos port 47821 is {(defaultPortFree ? "free" : "in use (Quantos will pick another)")}.")
                : new(name, Outcome.Fail, "Unexpected response.");
        }
        catch (Exception ex)
        {
            return new(name, Outcome.Fail, $"{ex.GetType().Name}: {ex.Message}. A firewall or security tool may block local servers.");
        }
        finally
        {
            listener.Stop();
        }
    }

    private static bool PortFree(int port)
    {
        try { var l = new TcpListener(IPAddress.Loopback, port); l.Start(); l.Stop(); return true; }
        catch (SocketException) { return false; }
    }

    private static CheckResult Sqlite()
    {
        const string name = "SQLite (cache database)";
        var path = Path.Combine(Path.GetTempPath(), $"quantos-probe-{Guid.NewGuid():N}.sqlite");
        try
        {
            using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "CREATE TABLE t (x TEXT); INSERT INTO t VALUES ('ok'); SELECT x FROM t; ";
                var value = cmd.ExecuteScalar() as string;
                using var v = c.CreateCommand();
                v.CommandText = "SELECT sqlite_version()";
                var version = v.ExecuteScalar();
                return value == "ok" ? new(name, Outcome.Pass, $"SQLite {version} works (its native library loaded).") : new(name, Outcome.Fail, "Query failed.");
            }
        }
        catch (Exception ex)
        {
            return new(name, Outcome.Fail, $"{ex.GetType().Name}: {ex.Message}. Application control may be blocking DLLs extracted to the temp folder.");
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    private static CheckResult PowerShellEngine()
    {
        const string name = "PowerShell engine";
        try
        {
            using var ps = PowerShell.Create();
            ps.AddScript("\"$($ExecutionContext.SessionState.LanguageMode)|$($PSVersionTable.PSVersion)|$((Get-Date).ToString('s'))\"");
            var result = ps.Invoke();
            if (ps.HadErrors) return new(name, Outcome.Fail, string.Join("; ", ps.Streams.Error.Select(e => e.ToString())));
            var parts = (result.FirstOrDefault()?.ToString() ?? "").Split('|');
            var mode = parts.ElementAtOrDefault(0) ?? "?";
            var version = parts.ElementAtOrDefault(1) ?? "?";
            return mode == "FullLanguage"
                ? new(name, Outcome.Pass, $"PowerShell {version} runs inside the app in FullLanguage mode.")
                : new(name, Outcome.Warn, $"PowerShell {version} runs, but in {mode} mode. Company policy limits scripts, so the planned terminal and automations will be restricted.");
        }
        catch (Exception ex)
        {
            return new(name, Outcome.Fail, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static CheckResult WebView2Runtime()
    {
        const string name = "WebView2 runtime";
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return new(name, Outcome.Pass, $"Installed (version {version}). The test page above is rendered with it.");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return new(name, Outcome.Fail, "Not installed. The Windows client needs it; the browser version of Quantos would still work.");
        }
        catch (Exception ex)
        {
            return new(name, Outcome.Fail, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
