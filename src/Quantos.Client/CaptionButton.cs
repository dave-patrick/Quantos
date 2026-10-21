using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Quantos.Client;

/// <summary>
/// The window's own min, maximize and close buttons, drawn the way Windows 11 draws them: a thin
/// glyph, a rounded patch on hover, and a red close. The glyphs are drawn rather than taken from a
/// font, because the font's dash, square and multiplication sign look out of place next to the
/// app's own icons.
/// </summary>
public sealed class CaptionButton : Control
{
    public enum Glyph { Minimize, Maximize, Close }

    public static readonly Color Chrome = Color.FromArgb(12, 19, 29);       // --bg
    public static readonly Color Ink = Color.FromArgb(197, 208, 223);        // --text-soft
    public static readonly Color InkBright = Color.FromArgb(237, 242, 250);  // --text
    public static readonly Color HoverFill = Color.FromArgb(23, 38, 59);    // --bg-hover
    public static readonly Color PressFill = Color.FromArgb(28, 46, 66);    // --bg-active
    public static readonly Color CloseFill = Color.FromArgb(196, 43, 28);

    private readonly Glyph _glyph;
    private bool _hovered;
    private bool _pressed;

    public CaptionButton(Glyph glyph)
    {
        _glyph = glyph;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Chrome;
        Size = new Size(46, 34);
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    /// <summary>True while the window is maximized, so the button shows the restore glyph.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [DefaultValue(false)]
    public bool Maximized { get; set; }

    /// <summary>Raises the click, so the title bar can act as if the button was pressed.</summary>
    public void PerformClick() => OnClick(EventArgs.Empty);

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var ink = Ink;
        if (_hovered)
        {
            var fill = _glyph == Glyph.Close ? CloseFill : (_pressed ? PressFill : HoverFill);
            using var patch = RoundedPath(new Rectangle(3, 3, Width - 6, Height - 6), 5);
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, patch);
            ink = InkBright;
        }

        // A 10x10 glyph box, centred, drawn on the pixel grid so the lines stay crisp.
        var box = new Rectangle((Width - 10) / 2, (Height - 10) / 2 + 1, 10, 10);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(ink, 1f);
        switch (_glyph)
        {
            case Glyph.Minimize:
                g.DrawLine(pen, box.Left, box.Bottom - 1, box.Right - 1, box.Bottom - 1);
                break;
            case Glyph.Maximize:
                if (Maximized)
                {
                    // Restore: a smaller square behind, a bigger one in front.
                    g.DrawRectangle(pen, box.Left + 3, box.Top, 6, 6);
                    g.DrawLines(pen, [new Point(box.Left, box.Top + 3), new Point(box.Left, box.Top), new Point(box.Left + 3, box.Top)]);
                    g.DrawLines(pen, [new Point(box.Right - 4, box.Bottom - 1), new Point(box.Right - 1, box.Bottom - 1), new Point(box.Right - 1, box.Bottom - 4)]);
                }
                else
                {
                    g.DrawRectangle(pen, box.Left, box.Top, 9, 9);
                }
                break;
            case Glyph.Close:
                g.DrawLine(pen, box.Left, box.Top, box.Right - 1, box.Bottom - 1);
                g.DrawLine(pen, box.Right - 1, box.Top, box.Left, box.Bottom - 1);
                break;
        }
    }

    /// <summary>A rounded rectangle path, used for the hover patch.</summary>
    private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
