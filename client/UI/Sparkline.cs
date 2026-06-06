using System.Drawing.Drawing2D;
using VlessMonitor.Core;

namespace VlessMonitor.UI;

/// <summary>Tiny self-drawn live graph for sidebar items (cheap, no ScottPlot).</summary>
public class Sparkline : Control
{
    private List<MetricSample> _data = new();
    private MetricKind _kind = MetricKind.Numeric;
    private Color _line = Theme.Current.Accent;

    public Sparkline()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
    }

    public void SetData(List<MetricSample> data, MetricKind kind, Color line)
    {
        _data = data; _kind = kind; _line = line;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var p = Theme.Current;
        int w = Width, h = Height;
        if (_data.Count < 2) return;

        if (_kind == MetricKind.State)
        {
            // up/down band: green where ok, red where not
            float dx = (float)w / _data.Count;
            for (int i = 0; i < _data.Count; i++)
            {
                using var b = new SolidBrush(_data[i].Ok
                    ? Color.FromArgb(150, p.Success) : Color.FromArgb(180, p.Critical));
                g.FillRectangle(b, i * dx, 0, dx + 1, h);
            }
            return;
        }

        double min = _data.Min(s => s.Value);
        double max = _data.Max(s => s.Value);
        if (max - min < 1e-6) { min -= 1; max += 1; }

        var pts = new PointF[_data.Count];
        for (int i = 0; i < _data.Count; i++)
        {
            float x = (float)i / (_data.Count - 1) * (w - 2) + 1;
            float y = h - 2 - (float)((_data[i].Value - min) / (max - min)) * (h - 4);
            pts[i] = new PointF(x, y);
        }

        // fill under line
        using (var path = new GraphicsPath())
        {
            path.AddLines(pts);
            path.AddLine(pts[^1].X, h, pts[0].X, h);
            path.CloseFigure();
            using var fill = new SolidBrush(Color.FromArgb(40, _line));
            g.FillPath(fill, path);
        }
        using var pen = new Pen(_line, 1.5f);
        g.DrawLines(pen, pts);
    }
}
