using VlessMonitor.Core;
using VlessMonitor.Checks;

namespace VlessMonitor.UI;

/// <summary>
/// Fluent 2 status dashboard. Rows are persistent and update in place —
/// no full redraw on each cycle, so there is no flicker.
/// </summary>
public class StatusForm : Form
{
    private readonly BufferedPanel _header;
    private readonly Label _statusLabel;
    private readonly Label _timeLabel;
    private readonly BufferedPanel _list;
    private readonly Label _diagLabel;
    private readonly FluentButton _btnRefresh;
    private readonly FluentButton _btnSettings;
    private readonly System.Windows.Forms.Timer _ageTimer;

    private readonly Dictionary<string, CheckRowControl> _rows = new();
    private readonly List<Control> _layoutControls = new();
    private string _layoutKey = "";
    private DateTime _lastUpdate = DateTime.MinValue;

    public event Action? RefreshRequested;
    public event Action? SettingsRequested;

    private static readonly Dictionary<CheckCategory, string> CatLabel = new()
    {
        [CheckCategory.Ping]   = "ДОСТУПНОСТЬ СЕРВЕРОВ",
        [CheckCategory.Port]   = "ПОРТЫ",
        [CheckCategory.Tunnel] = "ТУННЕЛЬ VLESS",
        [CheckCategory.Dpi]    = "DPI — БЛОКИРОВКИ И ЗАМЕДЛЕНИЯ",
        [CheckCategory.Stats]  = "СТАТИСТИКА",
        [CheckCategory.General]= "ПРОЧЕЕ",
    };
    private static readonly CheckCategory[] CatOrder =
    {
        CheckCategory.Ping, CheckCategory.Port, CheckCategory.Tunnel,
        CheckCategory.Dpi, CheckCategory.Stats, CheckCategory.General,
    };

    public StatusForm()
    {
        Theme.Refresh();

        Text = "VLESS Monitor";
        Size = new Size(860, 640);
        MinimumSize = new Size(640, 460);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font(Theme.UiFont, 9.5f);
        DoubleBuffered = true;
        Icon = IconFactory.Create(OverallStatus.Unknown);

        // ── Footer (diagnosis) ──
        var footer = new BufferedPanel { Dock = DockStyle.Bottom, Height = 64 };
        _diagLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font(Theme.UiFont, 10f),
            Padding = new Padding(20, 10, 20, 10),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        footer.Controls.Add(_diagLabel);

        // ── Header ──
        _header = new BufferedPanel { Dock = DockStyle.Top, Height = 68 };

        _statusLabel = new Label
        {
            Text = "Проверяю…",
            Font = new Font(Theme.UiFont, 16f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 18),
        };
        _header.Controls.Add(_statusLabel);

        _btnSettings = new FluentButton { Text = "Настройки", Width = 110 };
        _btnSettings.Click += (_, _) => SettingsRequested?.Invoke();
        _btnRefresh = new FluentButton { Text = "Обновить", Width = 100, Accent = true };
        _btnRefresh.Click += (_, _) => RefreshRequested?.Invoke();

        _timeLabel = new Label
        {
            Text = "", AutoSize = true,
            Font = new Font(Theme.UiFont, 8.5f),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };

        _header.Controls.Add(_btnSettings);
        _header.Controls.Add(_btnRefresh);
        _header.Controls.Add(_timeLabel);
        _header.Resize += (_, _) => LayoutHeader();

        // ── List ──
        _list = new BufferedPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(16, 8, 16, 8),
        };
        _list.Resize += (_, _) => ResizeRows();

        Controls.Add(_list);
        Controls.Add(footer);
        Controls.Add(_header);

        ApplyTheme();
        LayoutHeader();

        _ageTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _ageTimer.Tick += (_, _) => UpdateAge();
        _ageTimer.Start();

        Theme.Changed += OnThemeChanged;
        FormClosing += (s, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
    }

    // ── Theme ────────────────────────────────────────────────
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWindowChrome();
    }

    private void ApplyWindowChrome()
    {
        Native.UseDarkTitleBar(Handle, Theme.Current.IsDark);
        Native.UseRoundedCorners(Handle);
    }

    private void ApplyTheme()
    {
        var p = Theme.Current;
        BackColor = p.WindowBg;
        _header.BackColor = p.WindowBg;
        _list.BackColor = p.WindowBg;
        _diagLabel.Parent!.BackColor = p.WindowBg;
        _statusLabel.BackColor = p.WindowBg;
        _timeLabel.BackColor = p.WindowBg;
        _timeLabel.ForeColor = p.TextSecondary;
        foreach (var c in _layoutControls)
            c.BackColor = p.WindowBg;
    }

    private void OnThemeChanged()
    {
        if (InvokeRequired) { BeginInvoke(OnThemeChanged); return; }
        ApplyTheme();
        ApplyWindowChrome();
        Icon = IconFactory.Create(_lastOverall);
        _header.Invalidate(true);
        _list.Invalidate(true);
        foreach (var r in _rows.Values) r.Invalidate();
        Invalidate(true);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (Native.IsImmersiveColorSetChange(m))
            Theme.ReloadAndNotify();
    }

    private void LayoutHeader()
    {
        _btnSettings.Location = new Point(_header.Width - _btnSettings.Width - 20, 18);
        _btnRefresh.Location = new Point(_btnSettings.Left - _btnRefresh.Width - 8, 18);
        _timeLabel.Location = new Point(_btnRefresh.Left - 160, 26);
        _timeLabel.Width = 150;
        _timeLabel.TextAlign = ContentAlignment.MiddleRight;
    }

    // ── State updates ────────────────────────────────────────
    private OverallStatus _lastOverall = OverallStatus.Unknown;

    public void UpdateState(MonitorState state)
    {
        if (InvokeRequired) { BeginInvoke(() => ApplyState(state)); return; }
        ApplyState(state);
    }

    private void ApplyState(MonitorState state)
    {
        _lastUpdate = state.LastUpdate;
        _lastOverall = state.Overall;
        var p = Theme.Current;

        var (text, color) = state.Overall switch
        {
            OverallStatus.Green  => ("Всё в порядке", p.Success),
            OverallStatus.Yellow => ("Обнаружены проблемы", p.Caution),
            OverallStatus.Red    => ("Связь нарушена", p.Critical),
            _                    => ("Проверяю…", p.TextSecondary),
        };
        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;
        _diagLabel.Text = state.Diagnosis;
        _diagLabel.ForeColor = color;
        Icon = IconFactory.Create(state.Overall);

        var sorted = state.Checks.Values
            .OrderBy(r => Array.IndexOf(CatOrder, r.Category))
            .ThenBy(r => r.Name)
            .ToList();

        // Layout key = ordered structure. Rebuild layout only if it changed.
        var key = string.Join("|", sorted.Select(r => r.Category + ":" + r.Name));
        if (key != _layoutKey)
        {
            RebuildLayout(sorted);
            _layoutKey = key;
        }

        // In-place value updates — no flicker.
        foreach (var r in sorted)
            if (_rows.TryGetValue(r.Name, out var row))
                row.SetResult(r);
    }

    private void RebuildLayout(List<CheckResult> sorted)
    {
        _list.SuspendLayout();
        foreach (var c in _layoutControls) { _list.Controls.Remove(c); c.Dispose(); }
        _layoutControls.Clear();
        _rows.Clear();

        int y = 8;
        int width = _list.ClientSize.Width - 32 - SystemInformation.VerticalScrollBarWidth;
        CheckCategory? cat = null;

        foreach (var r in sorted)
        {
            if (cat != r.Category)
            {
                cat = r.Category;
                var hdr = new SectionHeader(CatLabel.GetValueOrDefault(r.Category, r.Category.ToString()))
                {
                    Location = new Point(16, y),
                    Width = width,
                    BackColor = Theme.Current.WindowBg,
                };
                _list.Controls.Add(hdr);
                _layoutControls.Add(hdr);
                y += hdr.Height + 2;
            }

            var row = new CheckRowControl
            {
                Location = new Point(16, y),
                Width = width,
                BackColor = Theme.Current.WindowBg,
            };
            row.SetResult(r);
            _list.Controls.Add(row);
            _layoutControls.Add(row);
            _rows[r.Name] = row;
            y += row.Height + 6;
        }

        _list.ResumeLayout();
    }

    private void ResizeRows()
    {
        int width = _list.ClientSize.Width - 32 - SystemInformation.VerticalScrollBarWidth;
        if (width < 100) return;
        foreach (var c in _layoutControls)
            c.Width = width;
    }

    private void UpdateAge()
    {
        if (_lastUpdate == DateTime.MinValue) return;
        var ago = (int)(DateTime.Now - _lastUpdate).TotalSeconds;
        _timeLabel.Text = ago < 60 ? $"обновлено {ago}с назад" : $"обновлено {ago / 60}м назад";
    }

    public void ShowAndFocus()
    {
        Show();
        WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Theme.Changed -= OnThemeChanged;
        base.Dispose(disposing);
    }
}
