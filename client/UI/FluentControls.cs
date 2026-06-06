using System.Drawing.Drawing2D;
using VlessMonitor.Checks;

namespace VlessMonitor.UI;

/// <summary>Panel with double buffering to eliminate flicker.</summary>
public class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint, true);
    }

    // Reduce scroll flicker
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
            return cp;
        }
    }
}

/// <summary>
/// A single check row, drawn as a Fluent card. Self-painting and self-updating:
/// <see cref="SetResult"/> repaints only when the value actually changed,
/// so periodic updates don't rebuild or flicker the whole window.
/// </summary>
public class CheckRowControl : Control
{
    private string _name = "";
    private string _comment = "";
    private string _raw = "";
    private bool _ok;
    private bool _hasData;

    public CheckRowControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw, true);
        Height = 52;
    }

    public void SetResult(CheckResult r)
    {
        var comment = r.Comment.Length > 0 ? r.Comment : r.Message;
        var raw = r.Comment.Length > 0 ? r.Message : "";

        if (_hasData && _name == r.Name && _comment == comment &&
            _raw == raw && _ok == r.Ok)
            return; // nothing changed — no repaint

        _name = r.Name;
        _comment = comment;
        _raw = raw;
        _ok = r.Ok;
        _hasData = true;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var p = Theme.Current;
        var rect = new Rectangle(0, 0, Width - 1, Height - 3);

        // Card background + 1px stroke
        using (var path = RoundedRect(rect, 8))
        {
            using var bg = new SolidBrush(p.CardBg);
            g.FillPath(bg, path);
            using var pen = new Pen(p.CardStroke, 1);
            g.DrawPath(pen, path);
        }

        // Status dot (filled circle with soft ring)
        var statusColor = _ok ? p.Success : p.Critical;
        int dotD = 10, dotX = 16, dotY = (Height - 3) / 2 - dotD / 2;
        using (var ring = new SolidBrush(Color.FromArgb(40, statusColor)))
            g.FillEllipse(ring, dotX - 3, dotY - 3, dotD + 6, dotD + 6);
        using (var dot = new SolidBrush(statusColor))
            g.FillEllipse(dot, dotX, dotY, dotD, dotD);

        string font = Theme.UiFont;
        int nameX = 40, nameW = 230;
        int valX = nameX + nameW + 8;
        int valW = Width - valX - 16;

        // Check name (primary)
        using (var nameBrush = new SolidBrush(p.TextPrimary))
        using (var f = new Font(font, 9.5f))
            g.DrawString(_name, f, nameBrush,
                new RectangleF(nameX, 0, nameW, Height - 3),
                new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });

        // Comment (status-coloured, big) + raw value (secondary, small)
        bool hasRaw = _raw.Length > 0;
        var commentColor = _ok ? p.Success : p.Caution;
        using (var cBrush = new SolidBrush(commentColor))
        using (var f = new Font(font, 9.5f))
            g.DrawString(_comment, f, cBrush,
                new RectangleF(valX, hasRaw ? 7 : 0, valW, hasRaw ? 20 : Height - 3),
                new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });

        if (hasRaw)
            using (var rBrush = new SolidBrush(p.TextSecondary))
            using (var f = new Font(font, 8f))
                g.DrawString(_raw, f, rBrush,
                    new RectangleF(valX, 26, valW, 18),
                    new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
    }

    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>Section header label (uppercase, secondary) — Win11 Settings style group title.</summary>
public class SectionHeader : Control
{
    public SectionHeader(string text)
    {
        Text = text;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        Height = 30;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var p = Theme.Current;
        using var brush = new SolidBrush(p.TextSecondary);
        using var f = new Font(Theme.UiFont, 8.5f, FontStyle.Bold);
        g.DrawString(Text, f, brush, new RectangleF(16, 0, Width - 16, Height),
            new StringFormat { LineAlignment = StringAlignment.Center });
    }
}

/// <summary>Pill button with Fluent hover/accent styling.</summary>
public class FluentButton : Button
{
    public bool Accent { get; set; }
    private bool _hover;

    public FluentButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint, true);
        Height = 32;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var p = Theme.Current;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        Color fill, text;
        if (Accent)
        {
            fill = _hover ? ControlPaint.Light(p.Accent, 0.1f) : p.Accent;
            text = p.AccentText;
        }
        else
        {
            fill = _hover ? p.ControlHover : p.ControlFill;
            text = p.TextPrimary;
        }

        using (var path = CheckRowControl.RoundedRect(rect, 6))
        {
            using var b = new SolidBrush(fill);
            g.FillPath(b, path);
            if (!Accent)
            {
                using var pen = new Pen(p.CardStroke, 1);
                g.DrawPath(pen, path);
            }
        }

        using var tb = new SolidBrush(text);
        using var f = new Font(Theme.UiFont, 9.5f);
        g.DrawString(Text, f, tb, rect,
            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
    }
}
