using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace Quantos.Client;

/// <summary>
/// The Stage 1 laptop test, from the Windows client: the backend's checks plus WebView2 and the
/// built-in PowerShell engine. Run Quantos.exe --selfcheck, or Self-check from Settings.
/// </summary>
public static class SelfCheck
{
    public static async Task<string> RunAsync(ClientPaths paths)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Quantos Windows client self-check");
        sb.AppendLine(new string('=', 60));

        var backend = Path.Combine(ClientPaths.AppDirectory, "Quantos.Backend.exe");
        if (File.Exists(backend))
        {
            var psi = new ProcessStartInfo(backend) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("--selfcheck");
            psi.ArgumentList.Add("--client-data"); psi.ArgumentList.Add(paths.ClientData);
            try
            {
                using var p = Process.Start(psi)!;
                sb.Append(await p.StandardOutput.ReadToEndAsync());
                await p.WaitForExitAsync();
            }
            catch (Exception ex) { sb.AppendLine($"[FAIL] Backend: couldn't start Quantos.Backend.exe ({ex.Message}). App control may be blocking it."); }
        }
        else sb.AppendLine("[FAIL] Backend: Quantos.Backend.exe isn't next to Quantos.exe.");

        sb.AppendLine();
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            Directory.CreateDirectory(paths.WebView2);
            var env = await CoreWebView2Environment.CreateAsync(null, paths.WebView2);
            sb.AppendLine($"[OK  ] WebView2: runtime {version}, profile folder writable ({paths.WebView2})");
            _ = env;
        }
        catch (Exception ex) { sb.AppendLine($"[FAIL] WebView2: {ex.Message} Use the browser instead (Quantos.Backend.exe --open)."); }

        try
        {
            using var rs = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());
            rs.Open();
            var mode = rs.SessionStateProxy.LanguageMode;
            using var ps = PowerShell.Create();
            ps.Runspace = rs;
            var result = ps.AddScript("$PSVersionTable.PSVersion.ToString()").Invoke();
            sb.AppendLine(mode == PSLanguageMode.FullLanguage
                ? $"[OK  ] Built-in PowerShell {result.FirstOrDefault()}: FullLanguage"
                : $"[WARN] Built-in PowerShell {result.FirstOrDefault()}: {mode} (app control is enforced; the terminal works with restrictions)");
        }
        catch (Exception ex) { sb.AppendLine($"[FAIL] Built-in PowerShell: {ex.Message}"); }

        var report = sb.ToString();
        try
        {
            Directory.CreateDirectory(paths.Logs);
            File.WriteAllText(Path.Combine(paths.Logs, $"selfcheck-client-{DateTime.Now:yyyyMMdd-HHmmss}.txt"), report);
        }
        catch (IOException) { }
        return report;
    }

    public static async Task RunAndShow(ClientPaths paths, IWin32Window? owner)
    {
        var report = await RunAsync(paths);
        var form = new Form
        {
            Text = "Quantos self-check", Width = 900, Height = 560, StartPosition = FormStartPosition.CenterScreen, Icon = MainForm.LoadIcon(),
            BackColor = Color.FromArgb(12, 19, 29)
        };
        form.Controls.Add(new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Text = report.Replace("\n", "\r\n"),
            Font = new Font("Consolas", 10f), BackColor = Color.FromArgb(16, 24, 36), ForeColor = Color.FromArgb(237, 242, 250), WordWrap = false
        });
        if (owner is Control c && c.InvokeRequired) c.Invoke(() => form.ShowDialog(owner));
        else form.ShowDialog(owner);
    }
}
