namespace Quantos.Client;

/// <summary>The complete script stays selectable and scrollable; approval defaults to Cancel.</summary>
internal static class ScriptConfirmation
{
    public static bool Show(IWin32Window owner, string code, string cwd)
    {
        if (code.Contains('\0'))
        {
            MessageBox.Show(owner, "This script contains a NUL character and cannot be reviewed safely.", "Script refused", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        using var dialog = new Form
        {
            Text = "Run this PowerShell code?", StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(760, 560), MinimumSize = new Size(520, 360),
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, AutoScaleMode = AutoScaleMode.Dpi
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading = new Label { Text = $"Review the complete script before running it.\r\nFolder: {cwd}", UseMnemonic = false, AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(0, 0, 0, 12) };
        layout.SizeChanged += (_, _) => heading.MaximumSize = new Size(Math.Max(1, layout.ClientSize.Width - layout.Padding.Horizontal), 0);
        layout.Controls.Add(heading, 0, 0);
        var preview = new TextBox
        {
            Name = "ScriptPreview", AccessibleName = "Complete PowerShell script", Text = code,
            Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill, Font = new Font("Consolas", 10), MaxLength = int.MaxValue
        };
        layout.Controls.Add(preview, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 12, 0, 0) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var run = new Button { Text = "Run", AutoSize = true, DialogResult = DialogResult.OK };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(run);
        layout.Controls.Add(buttons, 0, 2);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = cancel;
        dialog.CancelButton = cancel;
        dialog.ActiveControl = cancel;
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }
}
