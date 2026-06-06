using VlessMonitor.Core;
using VlessMonitor.Checks;

namespace VlessMonitor.UI;

/// <summary>
/// Main dashboard, laid out like Windows 11 Task Manager → Performance:
/// group list on the left, a grid of per-member graphs on the right,
/// extended stats at the bottom. Responsive and theme-aware.
/// </summary>
public class MonitorWindow : Form
{
    public event Action? RefreshRequested;
    public event Action? SettingsRequested;
    public event Action? DiagnosticsRequested;

    private static readonly Dictionary<CheckCategory, string> CatTitle = new()
    {
        [CheckCategory.Ping]   = "Доступность серверов",
        [CheckCategory.Port]   = "Порты",
        [CheckCategory.Tunnel] = "Туннель VLESS",
        [CheckCategory.Dpi]    = "DPI — блокировки и замедления",
        [CheckCategory.Stats]  = "Статистика",
        [CheckCategory.General]= "Прочее",
    };
    private static readonly CheckCategory[] CatOrder =
        { CheckCategory.Ping, CheckCategory.Port, CheckCategory.Tunnel,
          CheckCategory.Dpi, CheckCategory.Stats, CheckCategory.General };

    private readonly Label _overall;
    private readonly Label _diag;
    private readonly BufferedPanel _sidebar;
    private readonly Label _rightTitle;
    private readonly BufferedPanel _grid;
    private readonly BufferedPanel _stats;
    private readonly FlowLayoutPanel _periodBar;

    private readonly Dictionary<CheckCategory, GroupItem> _items = new();
    private readonly Dictionary<string, MetricGraph> _graphs = new();
    private CheckCategory? _selected;
    private TimeSpan _window = TimeSpan.FromMinutes(1);
    private string _gridKey = "";
    private readonly Config? _cfg;

    public MonitorWindow(Config? cfg = null)
    {
        _cfg = cfg;
        Theme.Refresh();
        Text = "VLESS Monitor";
        Size = new Size(1100, 720);
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterScreen;
        RestoreBounds(cfg);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font(Theme.UiFont, 9.5f);
        BackColor = Theme.Current.WindowBg;
        Icon = IconFactory.Create(OverallStatus.Unknown);

        // ── Header ──
        var header = new BufferedPanel { Dock = DockStyle.Top, Height = 60, BackColor = Theme.Current.WindowBg };
        _overall = new Label
        {
            Text = "Проверяю…", AutoSize = true, Location = new Point(18, 10),
            Font = new Font(Theme.UiFont, 15f, FontStyle.Bold), ForeColor = Theme.Current.TextSecondary,
        };
        _diag = new Label
        {
            Text = "", AutoSize = false, Location = new Point(20, 38), Size = new Size(700, 18),
            Font = new Font(Theme.UiFont, 9f), ForeColor = Theme.Current.TextSecondary,
            AutoEllipsis = true,
        };
        header.Controls.Add(_overall);
        header.Controls.Add(_diag);

        _periodBar = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight, AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(600, 14),
            BackColor = Theme.Current.WindowBg,
        };
        foreach (var (label, span) in new[] {
                     ("1 мин", TimeSpan.FromMinutes(1)),
                     ("5 мин", TimeSpan.FromMinutes(5)),
                     ("1 час", TimeSpan.FromHours(1)) })
        {
            var b = new FluentButton { Text = label, Width = 64, Height = 28, Accent = span == _window };
            b.Tag = span;
            b.Click += (s, _) => SetPeriod((TimeSpan)((Control)s!).Tag!);
            _periodBar.Controls.Add(b);
        }
        var btnRefresh = new FluentButton { Text = "Обновить", Width = 92, Height = 28, Accent = true };
        btnRefresh.Click += (_, _) => RefreshRequested?.Invoke();
        var btnSettings = new FluentButton { Text = "Настройки", Width = 96, Height = 28 };
        btnSettings.Click += (_, _) => SettingsRequested?.Invoke();
        var btnDiag = new FluentButton { Text = "Диагностика", Width = 108, Height = 28 };
        btnDiag.Click += (_, _) => DiagnosticsRequested?.Invoke();
        _periodBar.Controls.Add(new Label { Width = 12 });
        _periodBar.Controls.Add(btnRefresh);
        _periodBar.Controls.Add(btnSettings);
        _periodBar.Controls.Add(btnDiag);
        header.Controls.Add(_periodBar);
        header.Resize += (_, _) => _periodBar.Location =
            new Point(header.Width - _periodBar.PreferredSize.Width - 16, 14);

        // ── Sidebar ──
        _sidebar = new BufferedPanel
        {
            Dock = DockStyle.Left, Width = 250, AutoScroll = true,
            BackColor = Theme.Current.WindowBg, Padding = new Padding(8),
        };

        // ── Right: title + grid + stats ──
        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Current.WindowBg };
        _rightTitle = new Label
        {
            Dock = DockStyle.Top, Height = 36, Padding = new Padding(14, 8, 0, 0),
            Font = new Font(Theme.UiFont, 13f, FontStyle.Bold), ForeColor = Theme.Current.TextPrimary,
            Text = "",
        };
        _stats = new BufferedPanel
        {
            Dock = DockStyle.Bottom, Height = 150, BackColor = Theme.Current.WindowBg,
            Padding = new Padding(14, 8, 14, 8), AutoScroll = true,
        };
        _grid = new BufferedPanel
        {
            Dock = DockStyle.Fill, BackColor = Theme.Current.WindowBg, Padding = new Padding(10),
        };
        _grid.Resize += (_, _) => LayoutGrid();
        right.Controls.Add(_grid);
        right.Controls.Add(_stats);
        right.Controls.Add(_rightTitle);

        Controls.Add(right);
        Controls.Add(_sidebar);
        Controls.Add(header);

        Theme.Changed += OnThemeChanged;
        FormClosing += (s, e) =>
        {
            SaveBounds();
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
    }

    private void RestoreBounds(Config? cfg)
    {
        if (cfg is { WinW: > 200, WinH: > 200 })
        {
            // Only restore if the saved rect is visible on some screen.
            var rect = new Rectangle(cfg.WinX, cfg.WinY, cfg.WinW, cfg.WinH);
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = rect;
            }
            else Size = new Size(cfg.WinW, cfg.WinH);
        }
    }

    private void SaveBounds()
    {
        if (_cfg == null || WindowState != FormWindowState.Normal) return;
        _cfg.WinX = Bounds.X; _cfg.WinY = Bounds.Y;
        _cfg.WinW = Bounds.Width; _cfg.WinH = Bounds.Height;
        try { _cfg.Save(); } catch { }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.UseDarkTitleBar(Handle, Theme.Current.IsDark);
        Native.UseRoundedCorners(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (Native.IsImmersiveColorSetChange(m)) Theme.ReloadAndNotify();
    }

    private void SetPeriod(TimeSpan span)
    {
        _window = span;
        foreach (Control c in _periodBar.Controls)
            if (c is FluentButton b && b.Tag is TimeSpan ts) { b.Accent = ts == span; b.Invalidate(); }
        RefreshGraphs();
        RefreshStats();
    }

    // ── State update ─────────────────────────────────────────
    private OverallStatus _lastOverall = OverallStatus.Unknown;

    public void UpdateState(MonitorState state)
    {
        if (InvokeRequired) { BeginInvoke(() => ApplyState(state)); return; }
        ApplyState(state);
    }

    private void ApplyState(MonitorState state)
    {
        _lastOverall = state.Overall;
        var p = Theme.Current;
        var (text, color) = state.Overall switch
        {
            OverallStatus.Green  => ("● Всё в порядке", p.Success),
            OverallStatus.Yellow => ("◑ Обнаружены проблемы", p.Caution),
            OverallStatus.Red    => ("● Связь нарушена", p.Critical),
            _                    => ("○ Проверяю…", p.TextSecondary),
        };
        _overall.Text = text; _overall.ForeColor = color;
        _diag.Text = state.Diagnosis.Replace("\n", "   •   ");
        _diag.ForeColor = color;
        Icon = IconFactory.Create(state.Overall);

        RebuildSidebar();
        RefreshSidebar();
        RefreshGraphs();
        RefreshStats();
    }

    // ── Sidebar ──────────────────────────────────────────────
    private void RebuildSidebar()
    {
        var cats = MetricsStore.Instance.Categories()
            .OrderBy(c => Array.IndexOf(CatOrder, c)).ToList();
        var key = string.Join(",", cats);
        if (key == _sidebarKey) return;
        _sidebarKey = key;

        _sidebar.SuspendLayout();
        _sidebar.Controls.Clear();
        _items.Clear();
        int y = 8;
        foreach (var cat in cats)
        {
            var item = new GroupItem(cat, CatTitle.GetValueOrDefault(cat, cat.ToString()))
            {
                Location = new Point(8, y),
                Width = _sidebar.ClientSize.Width - 24,
            };
            item.Clicked += () => Select(cat);
            _sidebar.Controls.Add(item);
            _items[cat] = item;
            y += item.Height + 6;
        }
        _sidebar.ResumeLayout();

        if (_selected == null && cats.Count > 0) Select(cats[0]);
    }
    private string _sidebarKey = "";

    private void RefreshSidebar()
    {
        foreach (var (cat, item) in _items)
        {
            var series = MetricsStore.Instance.ByCategory(cat);
            item.Selected = _selected == cat;
            item.Update(series, _window);
        }
    }

    private void Select(CheckCategory cat)
    {
        _selected = cat;
        _rightTitle.Text = CatTitle.GetValueOrDefault(cat, cat.ToString());
        foreach (var (c, item) in _items) { item.Selected = c == cat; item.Invalidate(); }
        RebuildGrid();
        RefreshGraphs();
        RefreshStats();
    }

    // ── Graph grid ───────────────────────────────────────────
    private void RebuildGrid()
    {
        if (_selected == null) return;
        var series = MetricsStore.Instance.ByCategory(_selected.Value);
        var key = string.Join(",", series.Select(s => s.Name));
        if (key == _gridKey) return;
        _gridKey = key;

        _grid.SuspendLayout();
        foreach (var g in _graphs.Values) { _grid.Controls.Remove(g); g.Dispose(); }
        _graphs.Clear();
        foreach (var s in series)
        {
            var g = new MetricGraph();
            _grid.Controls.Add(g);
            _graphs[s.Name] = g;
        }
        _grid.ResumeLayout();
        LayoutGrid();
    }

    private void LayoutGrid()
    {
        if (_graphs.Count == 0) return;
        int pad = 10;
        int avail = _grid.ClientSize.Width - pad;
        int cols = Math.Max(1, Math.Min(4, avail / 360));
        int rows = (int)Math.Ceiling(_graphs.Count / (double)cols);
        int cw = (avail - pad * (cols - 1)) / cols;
        int ch = Math.Max(150, (_grid.ClientSize.Height - pad - pad * rows) / rows);

        int i = 0;
        foreach (var g in _graphs.Values)
        {
            int r = i / cols, c = i % cols;
            g.Location = new Point(pad + c * (cw + pad), pad + r * (ch + pad));
            g.Size = new Size(cw, ch);
            i++;
        }
    }

    private void RefreshGraphs()
    {
        if (_selected == null) return;
        foreach (var s in MetricsStore.Instance.ByCategory(_selected.Value))
            if (_graphs.TryGetValue(s.Name, out var g))
                g.Update(s, _window);
    }

    // ── Extended stats ───────────────────────────────────────
    private void RefreshStats()
    {
        if (_selected == null) return;
        var p = Theme.Current;
        _stats.SuspendLayout();
        _stats.Controls.Clear();
        int y = 4;
        foreach (var s in MetricsStore.Instance.ByCategory(_selected.Value))
        {
            var (min, avg, max, uptime) = s.Stats(_window);
            var last = s.Last;
            var color = (last?.Ok ?? true) ? p.Success : p.Critical;

            string line = s.Kind == MetricKind.State
                ? $"{s.Name}:  аптайм {uptime:0}%"
                : $"{s.Name}:  тек {last?.Value ?? 0:0.#} {s.Unit}   •   min {min:0.#}  avg {avg:0.#}  max {max:0.#}   •   аптайм {uptime:0}%";

            var lbl = new Label
            {
                Text = line, AutoSize = false, Width = _stats.ClientSize.Width - 20, Height = 22,
                Location = new Point(4, y), ForeColor = color,
                Font = new Font(Theme.UiFont, 9f), TextAlign = ContentAlignment.MiddleLeft,
            };
            _stats.Controls.Add(lbl);
            y += 24;
        }
        _stats.ResumeLayout();
    }

    // ── Theme ────────────────────────────────────────────────
    private void OnThemeChanged()
    {
        if (InvokeRequired) { BeginInvoke(OnThemeChanged); return; }
        var p = Theme.Current;
        BackColor = p.WindowBg;
        foreach (Control c in new Control[] { _sidebar, _grid, _stats })
            c.BackColor = p.WindowBg;
        _rightTitle.ForeColor = p.TextPrimary;
        Native.UseDarkTitleBar(Handle, p.IsDark);
        foreach (var g in _graphs.Values) g.ApplyTheme();
        foreach (var it in _items.Values) it.Invalidate();
        Icon = IconFactory.Create(_lastOverall);
        Invalidate(true);
    }

    public void ShowAndFocus()
    {
        Show(); WindowState = FormWindowState.Normal; BringToFront(); Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Theme.Changed -= OnThemeChanged;
        base.Dispose(disposing);
    }

    // ── Sidebar item control ─────────────────────────────────
    private class GroupItem : Control
    {
        private readonly string _title;
        private readonly CheckCategory _cat;
        private readonly Sparkline _spark;
        private string _summary = "";
        private bool _allOk = true;
        public bool Selected { get; set; }
        public event Action? Clicked;

        public GroupItem(CheckCategory cat, string title)
        {
            _cat = cat; _title = title;
            Height = 64;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            _spark = new Sparkline
            {
                Parent = this, Width = 70, Height = 34,
                Location = new Point(Width - 78, 18),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            _spark.Click += (_, _) => Clicked?.Invoke();
            Click += (_, _) => Clicked?.Invoke();
        }

        public void Update(IReadOnlyList<MetricSeries> series, TimeSpan window)
        {
            _allOk = series.All(s => s.Last?.Ok ?? true);
            int ok = series.Count(s => s.Last?.Ok ?? false);
            _summary = $"{ok}/{series.Count} ок";
            var rep = series.FirstOrDefault(s => s.Kind == MetricKind.Numeric) ?? series.FirstOrDefault();
            if (rep != null)
                _spark.SetData(rep.Window(window), rep.Kind,
                    (rep.Last?.Ok ?? true) ? Theme.Current.Accent : Theme.Current.Critical);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var p = Theme.Current;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = CheckRowControl.RoundedRect(rect, 8))
            {
                using var bg = new SolidBrush(Selected ? p.ControlHover : p.CardBg);
                g.FillPath(bg, path);
                if (Selected)
                {
                    using var pen = new Pen(p.Accent, 1.5f);
                    g.DrawPath(pen, path);
                    // accent bar on the left
                    using var bar = new SolidBrush(p.Accent);
                    g.FillRectangle(bar, 0, 10, 3, Height - 20);
                }
            }

            var dotColor = _allOk ? p.Success : p.Critical;
            using (var dot = new SolidBrush(dotColor))
                g.FillEllipse(dot, 12, Height / 2 - 5, 10, 10);

            using (var tb = new SolidBrush(p.TextPrimary))
            using (var f = new Font(Theme.UiFont, 9.5f, FontStyle.Bold))
                g.DrawString(_title, f, tb, new RectangleF(30, 10, Width - 110, 20));
            using (var sb = new SolidBrush(p.TextSecondary))
            using (var f = new Font(Theme.UiFont, 8.5f))
                g.DrawString(_summary, f, sb, new RectangleF(30, 32, Width - 110, 18));
        }
    }
}
