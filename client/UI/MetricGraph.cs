using VlessMonitor.Core;

namespace VlessMonitor.UI;

/// <summary>
/// One subgraph card (like a per-core CPU graph): title + value on top,
/// a ScottPlot time-series below. Falls back to a Sparkline if ScottPlot
/// can't initialise (e.g. native lib issue), so the dashboard never breaks.
/// </summary>
public class MetricGraph : Panel
{
    private readonly Label _title;
    private readonly Control _plotHost;
    private ScottPlot.WinForms.FormsPlot? _plot;
    private Sparkline? _fallback;
    private string _seriesName = "";

    public MetricGraph()
    {
        BackColor = Theme.Current.CardBg;
        Padding = new Padding(1);

        _title = new Label
        {
            Dock = DockStyle.Top, Height = 24,
            Font = new Font(Theme.UiFont, 9f, FontStyle.Bold),
            ForeColor = Theme.Current.TextPrimary,
            BackColor = Theme.Current.CardBg,
            Padding = new Padding(8, 4, 8, 0),
        };

        try
        {
            _plot = new ScottPlot.WinForms.FormsPlot { Dock = DockStyle.Fill };
            StylePlot(_plot);
            _plotHost = _plot;
        }
        catch
        {
            _plot = null;
            _fallback = new Sparkline { Dock = DockStyle.Fill, BackColor = Theme.Current.CardBg };
            _plotHost = _fallback;
        }

        Controls.Add(_plotHost);
        Controls.Add(_title);
    }

    private static void StylePlot(ScottPlot.WinForms.FormsPlot fp)
    {
        var p = Theme.Current;
        var axisColor = ToSP(p.TextSecondary);
        fp.Plot.FigureBackground.Color = ToSP(p.CardBg);
        fp.Plot.DataBackground.Color = ToSP(p.CardBg);
        fp.Plot.Axes.Color(axisColor);
        // Explicitly colour tick LABELS too — Axes.Color leaves them dark on some builds.
        fp.Plot.Axes.Bottom.TickLabelStyle.ForeColor = axisColor;
        fp.Plot.Axes.Left.TickLabelStyle.ForeColor = axisColor;
        fp.Plot.Axes.Bottom.TickLabelStyle.FontSize = 11;
        fp.Plot.Axes.Left.TickLabelStyle.FontSize = 11;
        fp.Plot.Axes.Bottom.MajorTickStyle.Color = axisColor;
        fp.Plot.Axes.Left.MajorTickStyle.Color = axisColor;
        fp.Plot.Grid.MajorLineColor = ToSP(p.CardStroke).WithAlpha(50);
        fp.Plot.Axes.DateTimeTicksBottom();
        fp.UserInputProcessor.Disable(); // read-only dashboard, no pan/zoom
    }

    private static ScottPlot.Color ToSP(Color c) =>
        new(c.R, c.G, c.B, c.A);

    public void Update(MetricSeries series, TimeSpan window)
    {
        _seriesName = series.Name;
        var data = series.Window(window);
        var last = series.Last;
        var color = (last?.Ok ?? true) ? Theme.Current.Success : Theme.Current.Critical;

        string val = last == null ? "—"
            : series.Kind == MetricKind.State
                ? (last.Value.Ok ? "OK" : "сбой")
                : $"{last.Value.Value:0.#} {series.Unit}".Trim();
        _title.Text = $"{series.Name}   {val}";
        _title.ForeColor = color;

        if (_plot != null)
        {
            try { UpdatePlot(series, data, color); return; }
            catch { /* fall through to fallback below */ }
        }
        _fallback?.SetData(data, series.Kind, color);
    }

    private void UpdatePlot(MetricSeries series, List<MetricSample> data, Color color)
    {
        var plot = _plot!.Plot;
        plot.Clear();

        if (data.Count >= 2)
        {
            double[] xs = data.Select(s => s.Time.ToOADate()).ToArray();

            if (series.Kind == MetricKind.State)
            {
                // 1/0 step line, filled
                double[] ys = data.Select(s => s.Ok ? 1.0 : 0.0).ToArray();
                var sp = plot.Add.Scatter(xs, ys);
                sp.Color = ToSP(color);
                sp.LineWidth = 2;
                sp.MarkerSize = 0;
                sp.ConnectStyle = ScottPlot.ConnectStyle.StepHorizontal;
                plot.Axes.SetLimitsY(-0.1, 1.1);
            }
            else
            {
                double[] ys = data.Select(s => s.Value).ToArray();
                var sp = plot.Add.Scatter(xs, ys);
                sp.Color = ToSP(color);
                sp.LineWidth = 2;
                sp.MarkerSize = 0;
                sp.FillY = true;
                sp.FillYColor = ToSP(color).WithAlpha(40);
            }
            plot.Axes.AutoScale();
        }

        _plot.Refresh();
    }

    public void ApplyTheme()
    {
        BackColor = Theme.Current.CardBg;
        _title.BackColor = Theme.Current.CardBg;
        if (_plot != null) { StylePlot(_plot); _plot.Refresh(); }
        if (_fallback != null) _fallback.BackColor = Theme.Current.CardBg;
    }
}
