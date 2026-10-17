using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Quantos.Client;

/// <summary>
/// The Windows client window (BUILD-PLAN section 3C): shows the shared frontend through WebView2,
/// owns or connects to the backend, minimizes on close, and exits only through Exit. Native
/// functions sit behind a narrow bridge that accepts messages from the backend's own pages only.
/// </summary>
public sealed class MainForm : Form
{
    private readonly ClientPaths _paths;
    private readonly ClientPrefs _prefs;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(12, 19, 29) };
    private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(197, 208, 223), BackColor = Color.FromArgb(12, 19, 29), Font = new Font("Segoe UI", 12f) };
    private readonly NotifyIcon _tray = new();
    private readonly HotKeys _hotkeys = new();
    private BackendConnection? _backend;
    private CoreWebView2Environment? _env;
    private TerminalHost? _terminal;
    private PopupForm? _capture, _scratchpad;
    private readonly HashSet<string> _approvedTerminals = [];
    private readonly HashSet<string> _promptedApprovals = [];
    private bool _exiting, _recovering;
    private TaskCompletionSource<int>? _flush;
    private string? _lastNotificationNode;
    private readonly SemaphoreSlim _reconnect = new(1, 1);
    private readonly System.Windows.Forms.Timer _watch = new() { Interval = 5000 };
    private readonly List<DateTime> _restarts = [];

    public MainForm(ClientPaths paths, ClientPrefs prefs)
    {
        _paths = paths;
        _prefs = prefs;
        Text = "Quantos";
        Icon = LoadIcon();
        BackColor = Color.FromArgb(12, 19, 29);
        MinimumSize = LogicalToDeviceUnits(new Size(720, 480));
        if (prefs.X is null)
        {
            // First run: size relative to the screen, so high-DPI displays don't get a cramped window.
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            StartPosition = FormStartPosition.Manual;
            var size = new Size((int)(area.Width * 0.82), (int)(area.Height * 0.86));
            Bounds = new Rectangle(area.Left + (area.Width - size.Width) / 2, area.Top + (area.Height - size.Height) / 2, size.Width, size.Height);
        }
        else
        {
            StartPosition = FormStartPosition.Manual;
            var saved = new Rectangle(prefs.X.Value, prefs.Y ?? 100, prefs.Width, prefs.Height);
            // A saved position on a monitor that's no longer attached falls back to the primary screen.
            Bounds = Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(saved)) ? saved : new Rectangle(100, 100, prefs.Width, prefs.Height);
        }
        if (prefs.Maximized) WindowState = FormWindowState.Maximized;
        Controls.Add(_web);
        Controls.Add(_status);
        _status.BringToFront();
        _status.Text = "Starting Quantos…";
        BuildTray();
    }

    public static Icon LoadIcon()
    {
        using var s = typeof(MainForm).Assembly.GetManifestResourceStream("quantos.ico");
        return s is null ? SystemIcons.Application : new Icon(s);
    }

    private void BuildTray()
    {
        _tray.Icon = Icon;
        _tray.Text = "Quantos";
        _tray.Visible = true;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Quantos", null, (_, _) => ShowMain());
        menu.Items.Add("Quick capture", null, (_, _) => ShowCapture());
        menu.Items.Add("Scratchpad", null, (_, _) => ToggleScratchpad());
        menu.Items.Add("Open in browser", null, async (_, _) => await OpenInBrowser());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit Quantos", null, async (_, _) => await ExitAsync());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowMain();
        _tray.BalloonTipClicked += (_, _) =>
        {
            ShowMain();
            if (_lastNotificationNode is not null) Post(new { type = "open-node", id = _lastNotificationNode });
        };
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            _backend = await BackendConnection.ConnectAsync(_paths, s => _status.Text = s);
            _status.Text = "Opening…";
            Directory.CreateDirectory(_paths.WebView2);
            _env = await CoreWebView2Environment.CreateAsync(null, _paths.WebView2);
            await _web.EnsureCoreWebView2Async(_env);
            ConfigureWebView(_web.CoreWebView2, main: true);
            _web.CoreWebView2.Navigate(await _backend.SignInUrlAsync("app"));
            _web.NavigationCompleted += async (_, args) =>
            {
                _status.Visible = false;
                _paths.Log($"Page loaded: {_web.Source?.GetLeftPart(UriPartial.Path)} (success: {args.IsSuccess}, status: {args.HttpStatusCode})");
                if (Environment.GetEnvironmentVariable("QUANTOS_CLIENT_DIAG") == "1")
                {
                    await Task.Delay(2500);
                    var text = await _web.CoreWebView2.ExecuteScriptAsync("document.body.innerText.slice(0, 400)");
                    _paths.Log("Page text: " + text);
                }
            };
            _terminal = new TerminalHost(Post);
            _backend.Listen(OnBackendEvent);
            RegisterHotkeys();
            _watch.Tick += async (_, _) => await WatchBackendAsync();
            _watch.Start();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _status.Text = "This computer doesn't have the Microsoft Edge WebView2 runtime.\nYou can still use Quantos in your browser: choose Open in browser from the tray icon.";
            _paths.Log("WebView2 runtime not found");
        }
        catch (Exception ex)
        {
            _status.Text = "Quantos couldn't start.\n\n" + ex.Message;
            _paths.Log("Startup failed: " + ex);
        }
    }

    public void ConfigureWebView(CoreWebView2 core, bool main)
    {
        core.Settings.AreDevToolsEnabled = Debugger.IsAttached || Environment.GetEnvironmentVariable("QUANTOS_DEVTOOLS") == "1";
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;  // no unrestricted native access from page script
        core.Settings.IsWebMessageEnabled = true;
        // Only the backend's own pages load here; every other link opens in the default browser.
        core.NavigationStarting += (_, args) =>
        {
            if (!args.Uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase) && !args.Uri.StartsWith("about:", StringComparison.Ordinal))
            {
                args.Cancel = true;
                OpenExternal(args.Uri);
            }
        };
        core.NewWindowRequested += (_, args) => { args.Handled = true; OpenExternal(args.Uri); };
        core.WebMessageReceived += (_, args) =>
        {
            // The bridge accepts messages only from the backend's origin.
            if (!args.Source.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) return;
            try { HandleMessage(JsonDocument.Parse(args.WebMessageAsJson).RootElement, core, main); }
            catch (Exception ex) { _paths.Log("Bridge message failed: " + ex.Message); }
        };
    }

    // Read each time: a restarted backend may be on a different port.
    private string Origin => new Uri(_backend!.Url).GetLeftPart(UriPartial.Authority);

    private static void OpenExternal(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https" || u.Scheme == "mailto"))
            Process.Start(new ProcessStartInfo(u.ToString()) { UseShellExecute = true });
    }

    // ---- bridge ---------------------------------------------------------------------------------

    private void HandleMessage(JsonElement msg, CoreWebView2 source, bool main)
    {
        var type = msg.TryGetProperty("type", out var t) ? t.GetString() : null;
        switch (type)
        {
            case "ready":
                source.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "client", version = Application.ProductVersion, captureHotkey = _prefs.CaptureHotkey, scratchpadHotkey = _prefs.ScratchpadHotkey }));
                break;
            case "notify":
                ShowBalloon(msg.GetProperty("title").GetString() ?? "Quantos", msg.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "", null);
                break;
            case "open-external":
                OpenExternal(msg.GetProperty("url").GetString() ?? "");
                break;
            case "reauth":
                _ = SignInAgainAsync(source, msg.TryGetProperty("path", out var p) ? p.GetString() : null);
                break;
            case "open-scratchpad":
                ToggleScratchpad();
                break;
            case "open-capture":
                ShowCapture();
                break;
            case "close-popup":
                if (!main) BeginInvoke(() => { _capture?.Hide(); });
                break;
            case "exit":
                _ = ExitAsync();
                break;
            case "pending-saves":
                _flush?.TrySetResult(msg.GetProperty("count").GetInt32());
                break;
            case "hotkeys":
                _prefs.CaptureHotkey = msg.GetProperty("capture").GetString() ?? _prefs.CaptureHotkey;
                _prefs.ScratchpadHotkey = msg.GetProperty("scratchpad").GetString() ?? _prefs.ScratchpadHotkey;
                _prefs.Save(_paths);
                RegisterHotkeys(reportTo: source);
                break;
            case "terminal":
                HandleTerminal(msg);
                break;
            case "run-code":
                RunCodeBlock(msg.GetProperty("code").GetString() ?? "", msg.TryGetProperty("cwd", out var cwd) ? cwd.GetString() : null);
                break;
            case "selfcheck":
                _ = Task.Run(() => SelfCheck.RunAndShow(_paths, this));
                break;
        }
    }

    private void HandleTerminal(JsonElement msg)
    {
        if (_terminal is null) return;
        var action = msg.GetProperty("action").GetString();
        var id = msg.TryGetProperty("id", out var i) ? i.GetString() : null;
        switch (action)
        {
            case "open":
                var cwd = BackendConnection.Read(_paths)?.Vault ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                // Shell access is kept separate from editing: each session is approved in a native prompt.
                var ok = Confirm("Open a PowerShell terminal?",
                    $"Commands you type will run on this computer with your permissions.\n\nStarting folder: {cwd}", "Open terminal");
                if (!ok) { Post(new { type = "terminal", @event = "denied" }); return; }
                var newId = _terminal.Open(cwd);
                _approvedTerminals.Add(newId);
                break;
            case "input" when id is not null && _approvedTerminals.Contains(id):
                _terminal.Input(id, msg.GetProperty("data").GetString() ?? "");
                break;
            case "stop" when id is not null:
                _terminal.Stop(id);
                break;
            case "close" when id is not null:
                _terminal.Close(id);
                _approvedTerminals.Remove(id);
                break;
        }
    }

    /// <summary>Code in notes never runs automatically: the exact code and folder are shown before it runs.</summary>
    private void RunCodeBlock(string code, string? cwd)
    {
        cwd ??= BackendConnection.Read(_paths)?.Vault ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Confirm("Run this PowerShell code?", $"Folder: {cwd}\n\n{Truncate(code, 1500)}", "Run")) return;
        _ = Task.Run(() =>
        {
            var (output, failed) = TerminalHost.RunOnce(code, cwd, TimeSpan.FromMinutes(5));
            Post(new { type = "run-result", output, failed });
        });
    }

    private bool Confirm(string heading, string text, string verb)
    {
        var page = new TaskDialogPage
        {
            Caption = "Quantos", Heading = heading, Text = text, Icon = TaskDialogIcon.ShieldWarningYellowBar,
            Buttons = { new TaskDialogButton(verb), TaskDialogButton.Cancel }, DefaultButton = TaskDialogButton.Cancel, AllowCancel = true
        };
        var result = (TaskDialogButton?)Invoke(() => TaskDialog.ShowDialog(this, page));
        return result is not null && result.Text == verb;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "\n…";

    private void Post(object message)
    {
        var json = JsonSerializer.Serialize(message);
        if (IsHandleCreated) BeginInvoke(() =>
        {
            try { _web.CoreWebView2?.PostWebMessageAsJson(json); } catch (InvalidOperationException) { }
        });
    }

    // ---- backend events: reminders and approvals -------------------------------------------------

    private void OnBackendEvent(JsonElement e)
    {
        var kind = e.TryGetProperty("kind", out var k) ? k.GetString() : null;
        if (kind == "Notification" && e.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("notification", out var n))
        {
            // One backend, one client: each reminder shows once, however many browser tabs are open.
            ShowBalloon(n.GetProperty("title").GetString() ?? "Reminder", n.GetProperty("body").GetString() ?? "", n.TryGetProperty("nodeId", out var id) ? id.GetString() : null);
        }
        else if (kind == "Approval" && e.TryGetProperty("data", out var a) && a.ValueKind == JsonValueKind.Object)
        {
            var approvalId = a.GetProperty("id").GetString()!;
            if (!_promptedApprovals.Add(approvalId)) return;
            var actor = a.GetProperty("actor");
            var who = $"{actor.GetProperty("kind").GetString()} “{actor.GetProperty("name").GetString()}”";
            BeginInvoke(async () =>
            {
                ShowMain();
                var approve = Confirm($"Approve: {Describe(a.GetProperty("permission").GetString())}?",
                    $"Requested by {who}.\n\n{a.GetProperty("description").GetString()}\n\nOnly approve if you started this.", "Approve");
                try { await _backend!.Http.PostAsJsonAsync($"api/approvals/{approvalId}", new { approve }); }
                catch (HttpRequestException ex) { _paths.Log("Approval failed: " + ex.Message); }
            });
        }
        else if (kind == "ShellJob" && e.TryGetProperty("data", out var job))
        {
            _ = Task.Run(() => RunApprovedJob(job));
        }
    }

    private static string Describe(string? permission) => permission switch
    {
        "PermanentDelete" => "permanent deletion",
        "RunCommands" => "running a command",
        _ => permission ?? "action"
    };

    /// <summary>Runs a shell job the user already approved in this client, and reports the result.</summary>
    private async Task RunApprovedJob(JsonElement job)
    {
        var id = job.GetProperty("id").GetString()!;
        var (output, failed) = TerminalHost.RunOnce(job.GetProperty("command").GetString()!, job.GetProperty("cwd").GetString()!, TimeSpan.FromMinutes(5));
        try { await _backend!.Http.PostAsJsonAsync($"api/shell/jobs/{id}/result", new { output, failed }); }
        catch (HttpRequestException ex) { _paths.Log("Job result failed: " + ex.Message); }
    }

    private void ShowBalloon(string title, string body, string? nodeId)
    {
        _lastNotificationNode = nodeId;
        _paths.Log($"Notification: {title}");
        BeginInvoke(() => _tray.ShowBalloonTip(8000, title, string.IsNullOrWhiteSpace(body) ? " " : body, ToolTipIcon.Info));
    }

    // ---- windows ---------------------------------------------------------------------------------

    private void RegisterHotkeys(CoreWebView2? reportTo = null)
    {
        _hotkeys.Clear();
        var problems = new[] { _hotkeys.Register(_prefs.CaptureHotkey, () => BeginInvoke(ShowCapture)), _hotkeys.Register(_prefs.ScratchpadHotkey, () => BeginInvoke(ToggleScratchpad)) }
            .Where(p => p is not null).ToList();
        foreach (var p in problems) _paths.Log("Shortcut: " + p);
        var json = JsonSerializer.Serialize(new { type = "hotkey-status", problems });
        (reportTo ?? _web.CoreWebView2)?.PostWebMessageAsJson(json);
    }

    public void ShowMain()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = _prefs.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
        Show();
        Activate();
    }

    private void ShowCapture()
    {
        if (_env is null || _backend is null) return;
        _capture ??= new PopupForm(this, _env, _backend.Url + "capture", "Quick capture", new Size(560, 300), topMost: true, hideOnDeactivate: true);
        _capture.ShowNear();
    }

    private void ToggleScratchpad()
    {
        if (_env is null || _backend is null) return;
        _scratchpad ??= new PopupForm(this, _env, _backend.Url + "scratchpad", "Scratchpad", new Size(420, 520), topMost: _prefs.ScratchpadOnTop, hideOnDeactivate: false);
        if (_scratchpad.Visible) _scratchpad.Hide(); else _scratchpad.ShowNear();
    }

    private async Task OpenInBrowser()
    {
        if (_backend is null) return;
        OpenExternal(await _backend.SignInUrlAsync("browser"));
    }

    // ---- backend recovery ------------------------------------------------------------------------

    /// <summary>A page lost its sign-in (usually because the backend restarted): give it a fresh one-time link.</summary>
    private async Task SignInAgainAsync(CoreWebView2 page, string? path)
    {
        try
        {
            if (!await EnsureBackendAsync()) return;
            var url = await _backend!.SignInUrlAsync("app", path is { Length: > 0 } && path.StartsWith('/') ? path : "/");
            page.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "signin", url }));
        }
        catch (Exception ex) { _paths.Log("Signing a page in again failed: " + ex.Message); }
    }

    /// <summary>Every few seconds: if the backend's process has gone, start it again and sign the windows back in.</summary>
    private async Task WatchBackendAsync()
    {
        if (_recovering || _exiting || _backend is null || !_backend.ProcessGone()) return;
        _recovering = true;
        try
        {
            _paths.Log($"The backend (pid {_backend.Info.Pid}) stopped unexpectedly.");
            if (!await EnsureBackendAsync()) return;
            await SignInAgainAsync(_web.CoreWebView2, "/");
            foreach (var popup in new[] { _capture, _scratchpad })
                if (popup?.Core is { } core) await SignInAgainAsync(core, popup.PagePath);
        }
        finally { _recovering = false; }
    }

    /// <summary>Reconnects, starting a new backend if none is running. False when it keeps failing.</summary>
    private async Task<bool> EnsureBackendAsync()
    {
        await _reconnect.WaitAsync();
        try
        {
            if (_backend is not null && !_backend.ProcessGone()) return true;
            _restarts.RemoveAll(t => DateTime.UtcNow - t > TimeSpan.FromMinutes(10));
            if (_restarts.Count >= 3)
            {
                _watch.Stop();
                ShowBalloon("Quantos stopped working", $"The backend stopped several times. Unsaved text is kept in the window. Details are in {_paths.Logs}.", null);
                return false;
            }
            _restarts.Add(DateTime.UtcNow);
            _backend?.Dispose();
            _backend = await BackendConnection.ConnectAsync(_paths, s => _paths.Log(s));
            _backend.Listen(OnBackendEvent);
            _paths.Log($"Reconnected to backend {_backend.Url} (pid {_backend.Info.Pid}).");
            return true;
        }
        catch (Exception ex)
        {
            _paths.Log("Restarting the backend failed: " + ex.Message);
            return false;
        }
        finally { _reconnect.Release(); }
    }

    // ---- close and exit ------------------------------------------------------------------------

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing the window minimizes it (confirmed); Exit is the explicit way out.
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            SaveWindow();
            WindowState = FormWindowState.Minimized;
            return;
        }
        base.OnFormClosing(e);
    }

    /// <summary>Exit: saves open work, warns about anything unsaved, and stops an owned backend only after a safe shutdown.</summary>
    public async Task ExitAsync()
    {
        if (_exiting) return;
        ShowMain();
        var pending = 0;
        if (_web.CoreWebView2 is not null)
        {
            _flush = new TaskCompletionSource<int>();
            _web.CoreWebView2.PostWebMessageAsJson("{\"type\":\"flush\"}");
            var done = await Task.WhenAny(_flush.Task, Task.Delay(4000));
            pending = done == _flush.Task ? _flush.Task.Result : -1;
        }
        if (pending != 0)
        {
            var text = pending > 0 ? $"{pending} note(s) still have changes that couldn't be saved." : "Quantos couldn't confirm that all changes were saved.";
            if (!Confirm("Exit anyway?", text + " If you exit now, those changes may be lost.", "Exit anyway")) return;
        }
        _exiting = true;
        _watch.Stop();
        SaveWindow();
        _status.Text = "Closing Quantos safely…";
        _status.Visible = true;
        _status.BringToFront();
        if (_backend?.Owned is not null)
        {
            // Another client (e.g. a browser) may still be using it; the backend stops only after finishing writes.
            var stopped = await _backend.ShutdownAsync();
            if (!stopped) _paths.Log("Backend didn't stop within 10 seconds.");
        }
        _tray.Visible = false;
        _terminal?.Dispose();
        _hotkeys.Dispose();
        Application.Exit();
    }

    private void SaveWindow()
    {
        _prefs.Maximized = WindowState == FormWindowState.Maximized;
        if (WindowState == FormWindowState.Normal)
        {
            _prefs.X = Left; _prefs.Y = Top; _prefs.Width = Width; _prefs.Height = Height;
        }
        _prefs.Save(_paths);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _tray.Dispose(); _backend?.Dispose(); }
        base.Dispose(disposing);
    }
}
