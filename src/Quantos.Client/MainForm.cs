using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Quantos.Client;

/// <summary>
/// The Windows client: shows the shared frontend in WebView2, starts or connects to the backend,
/// minimizes on close (BUILD-PLAN.md 2.7) and exits only through an explicit Exit.
/// The native bridge accepts one message today ("exit"), and only from the backend's own origin.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly string[] _args;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly Label _loading = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Text = "Starting Quantos…",
        Font = new Font("Segoe UI", 12f),
    };
    private BackendConnection? _backend;
    private bool _exiting;

    public MainForm(string[] args)
    {
        _args = args;
        Text = "Quantos";
        Width = 1400;
        Height = 900;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(0x0c, 0x13, 0x1d);
        _loading.ForeColor = Color.FromArgb(0xed, 0xf2, 0xfa);
        Controls.Add(_web);
        Controls.Add(_loading);
        _loading.BringToFront();
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.Q) _ = ExitAsync(); };
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            _backend = await BackendConnection.ConnectOrStartAsync(_args);
            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: Path.Combine(BackendConnection.ClientDataPath(), "webview2"));
            await _web.EnsureCoreWebView2Async(env);

            var core = _web.CoreWebView2;
            core.Settings.IsWebMessageEnabled = true;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.NavigationStarting += (_, a) =>
            {
                // Only our own pages render inside the app; other links open in the default browser.
                if (!IsOwnOrigin(a.Uri)) { a.Cancel = true; OpenExternal(a.Uri); }
            };
            core.NewWindowRequested += (_, a) => { a.Handled = true; OpenExternal(a.Uri); };
            core.WebMessageReceived += OnWebMessage;

            core.Navigate($"{_backend.BaseUrl}/#token={_backend.Token}");
            _loading.Visible = false;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Fail("The Microsoft Edge WebView2 Runtime isn't installed on this PC. It's normally part of Windows 10 and 11. " +
                 "You can still use Quantos in a browser by running Quantos.Backend.exe.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsOwnOrigin(e.Source)) return; // Never accept native requests from other content.
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type == "exit") _ = ExitAsync();
        }
        catch (JsonException) { /* Ignore malformed messages. */ }
    }

    private bool IsOwnOrigin(string uri) =>
        _backend is not null && Uri.TryCreate(uri, UriKind.Absolute, out var u) &&
        u.Scheme == "http" && u.Host == "127.0.0.1" && u.Port == new Uri(_backend.BaseUrl).Port;

    private static void OpenExternal(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http"))
            Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
    }

    /// <summary>Closing the window minimizes it. Exit (button or Ctrl+Q) shuts down safely.</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing && _backend is not null)
        {
            e.Cancel = true;
            WindowState = FormWindowState.Minimized;
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // Windows shutdown or sign-out: still stop the backend we own.
        if (!_exiting && _backend is not null) _backend.ShutdownOwnedAsync().GetAwaiter().GetResult();
        base.OnFormClosed(e);
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        Text = "Quantos — saving and exiting…";
        if (_backend is not null) await _backend.ShutdownOwnedAsync();
        _backend?.Dispose();
        _backend = null;
        Close();
    }

    private void Fail(string message)
    {
        _loading.Visible = true;
        _loading.BringToFront();
        _loading.Text = "Quantos couldn't start.\n\n" + message;
        _exiting = true; // Let the window close normally.
    }
}
