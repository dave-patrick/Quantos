using System.Text;
using Microsoft.Web.WebView2.WinForms;

namespace Quantos.Probe;

/// <summary>Runs the checks, shows a plain result list, and saves a report you can send back.</summary>
internal sealed class ProbeForm : Form
{
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
    private readonly WebView2 _web = new() { Dock = DockStyle.Top, Height = 120 };
    private readonly Label _summary = new() { Dock = DockStyle.Top, Height = 56, Padding = new Padding(12, 8, 12, 0), Font = new Font("Segoe UI", 11f) };
    private readonly Button _copy = new() { Text = "Copy report", AutoSize = true, Enabled = false };
    private readonly Button _close = new() { Text = "Close", AutoSize = true };
    private readonly List<CheckResult> _results = [];
    private string? _reportPath;

    public ProbeForm()
    {
        Text = "Quantos laptop test";
        Width = 1000;
        Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        _list.Columns.Add("Result", 80);
        _list.Columns.Add("Check", 280);
        _list.Columns.Add("Details", 600);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        buttons.Controls.Add(_close);
        buttons.Controls.Add(_copy);
        _close.Click += (_, _) => Close();
        _copy.Click += (_, _) => { Clipboard.SetText(Report()); _copy.Text = "Copied"; };

        Controls.Add(_list);
        Controls.Add(_web);
        Controls.Add(_summary);
        Controls.Add(buttons);
        _summary.Text = "Running checks…";
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await ShowWebViewAsync();
        foreach (var (name, run) in Checks.All())
        {
            CheckResult result;
            try { result = await run(); }
            catch (Exception ex) { result = new CheckResult(name, Outcome.Fail, ex.Message); }
            _results.Add(result);
            var item = new ListViewItem([result.Outcome.ToString().ToUpperInvariant(), result.Name, result.Detail])
            {
                ForeColor = result.Outcome switch { Outcome.Pass => Color.DarkGreen, Outcome.Warn => Color.DarkGoldenrod, _ => Color.Firebrick },
            };
            _list.Items.Add(item);
        }

        var fails = _results.Count(r => r.Outcome == Outcome.Fail);
        var warns = _results.Count(r => r.Outcome == Outcome.Warn);
        _summary.Text = fails == 0
            ? $"Quantos can run on this computer. {_results.Count - warns} passed, {warns} warnings."
            : $"{fails} check(s) failed. Quantos would be limited on this computer; see the red rows.";
        SaveReport();
        _copy.Enabled = true;
    }

    private async Task ShowWebViewAsync()
    {
        try
        {
            var dataDir = Path.Combine(Path.GetTempPath(), "quantos-probe-webview2");
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, dataDir);
            await _web.EnsureCoreWebView2Async(env);
            _web.NavigateToString("<html><body style='margin:0;font:15px Segoe UI;background:#0c131d;color:#edf2fa;display:flex;align-items:center;justify-content:center;height:100vh'>" +
                                  "<div>If you can read this, <b style='color:#78b6ff'>WebView2 works</b> and Quantos's interface can display here.</div></body></html>");
        }
        catch (Exception ex)
        {
            _web.Visible = false;
            _results.Add(new CheckResult("WebView2 display", Outcome.Fail, $"{ex.GetType().Name}: {ex.Message}"));
        }
    }

    private string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Quantos laptop test report");
        sb.AppendLine($"Date: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Computer: {Environment.MachineName}");
        sb.AppendLine();
        foreach (var r in _results) sb.AppendLine($"[{r.Outcome.ToString().ToUpperInvariant()}] {r.Name}: {r.Detail}");
        if (_reportPath is not null) sb.AppendLine().AppendLine($"Saved to: {_reportPath}");
        return sb.ToString();
    }

    private void SaveReport()
    {
        foreach (var dir in new[] { AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Desktop) })
        {
            try
            {
                var path = Path.Combine(dir, $"quantos-laptop-test-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                _reportPath = path;
                File.WriteAllText(path, Report());
                _summary.Text += $"\nReport saved to {path}";
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _reportPath = null; }
        }
    }
}
