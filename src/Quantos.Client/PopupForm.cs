using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Quantos.Client;

/// <summary>The capture window and the always-on-top scratchpad (BUILD-PLAN 8E.1-3).</summary>
public sealed class PopupForm : Form
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(16, 24, 36) };
    private readonly MainForm _owner;
    private readonly string _url;
    private readonly bool _hideOnDeactivate;
    private bool _ready;

    public PopupForm(MainForm owner, CoreWebView2Environment env, string url, string title, Size size, bool topMost, bool hideOnDeactivate)
    {
        _owner = owner;
        _url = url;
        _hideOnDeactivate = hideOnDeactivate;
        Text = title;
        Icon = MainForm.LoadIcon();
        Size = LogicalToDeviceUnits(size);
        TopMost = topMost;
        ShowInTaskbar = !hideOnDeactivate;
        FormBorderStyle = hideOnDeactivate ? FormBorderStyle.FixedToolWindow : FormBorderStyle.SizableToolWindow;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(16, 24, 36);
        Controls.Add(_web);
        _ = Init(env);
    }

    private async Task Init(CoreWebView2Environment env)
    {
        await _web.EnsureCoreWebView2Async(env);
        _owner.ConfigureWebView(_web.CoreWebView2, main: false);
        _web.CoreWebView2.Navigate(_url);
        _ready = true;
    }

    public void ShowNear()
    {
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top + screen.Height / 4);
        Show();
        Activate();
        if (_ready) _web.CoreWebView2.PostWebMessageAsJson("{\"type\":\"popup-shown\"}");
        _web.Focus();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (_hideOnDeactivate) Hide();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Hide(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }
}
