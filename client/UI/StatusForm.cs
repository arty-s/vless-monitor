using VlessMonitor.Core;
using VlessMonitor.Checks;

namespace VlessMonitor.UI;

/// <summary>
/// Detailed status window: every check shown with a plain-language comment.
/// </summary>
public class StatusForm : Form
{
    private readonly Label _statusLabel;
    private readonly Label _timeLabel;
    private readonly Label _diagLabel;
    private readonly Panel _listPanel;
    private readonly System.Windows.Forms.Timer _ageTimer;
    private DateTime _lastUpdate = DateTime.MinValue;

    public event Action? RefreshRequested;
    public event Action? SettingsRequested;

    private static readonly Dictionary<CheckCategory, string> CatLabel = new()
    {
        [CheckCategory.Ping]   = "ПИНГИ — доступность серверов",
        [CheckCategory.Port]   = "ПОРТЫ — открытые соединения",
        [CheckCategory.Tunnel] = "ТУННЕЛЬ — работа VLESS",
        [CheckCategory.Dpi]    = "DPI — поиск блокировок и замедлений",
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
        Text = "VLESS Monitor";
        Size = new Size(840, 620);
        MinimumSize = new Size(620, 440);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Fg;
        Font = new Font(Theme.FontFamily, 9.5f);
        Icon = IconFactory.Create(OverallStatus.Unknown);

        // ── Header ──
        var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Theme.Bg };

        _statusLabel = new Label
        {
            Text = "○ Проверяю...",
            Font = new Font(Theme.FontFamily, 15f, FontStyle.Bold),
            ForeColor = Theme.FgDim,
            AutoSize = true,
            Location = new Point(16, 14),
        };
        header.Controls.Add(_statusLabel);

        var btnSettings = MakeButton("⚙  Настройки", Theme.Lavender);
        btnSettings.Click += (_, _) => SettingsRequested?.Invoke();
        var btnRefresh = MakeButton("⟳  Обновить", Theme.Blue);
        btnRefresh.Click += (_, _) => RefreshRequested?.Invoke();

        _timeLabel = new Label
        {
            Text = "", ForeColor = Theme.FgDim, AutoSize = true,
            Font = new Font(Theme.FontFamily, 8.5f),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };

        header.Controls.Add(btnSettings);
        header.Controls.Add(btnRefresh);
        header.Controls.Add(_timeLabel);
        header.Resize += (_, _) => LayoutHeader(header, btnSettings, btnRefresh);
        LayoutHeader(header, btnSettings, btnRefresh);

        // ── Diagnosis footer ──
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 70, BackColor = Theme.Bg };
        _diagLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Theme.FgDim,
            Font = new Font(Theme.FontFamily, 10f),
            Padding = new Padding(16, 8, 16, 8),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        footer.Controls.Add(_diagLabel);

        // ── List ──
        _listPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            AutoScroll = true,
            Padding = new Padding(8, 4, 8, 4),
        };

        Controls.Add(_listPanel);
        Controls.Add(footer);
        Controls.Add(header);

        _ageTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _ageTimer.Tick += (_, _) => UpdateAge();
        _ageTimer.Start();

        // Hide instead of close
        FormClosing += (s, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    private void LayoutHeader(Panel header, Control btnSettings, Control btnRefresh)
    {
        btnSettings.Location = new Point(header.Width - btnSettings.Width - 14, 13);
        btnRefresh.Location = new Point(btnSettings.Left - btnRefresh.Width - 8, 13);
        _timeLabel.Location = new Point(btnRefresh.Left - 150, 20);
        _timeLabel.Width = 140;
    }

    private Button MakeButton(string text, Color color)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = false,
            Size = new Size(110, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Bg3,
            ForeColor = color,
            Font = new Font(Theme.FontFamily, 9.5f),
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        b.FlatAppearance.BorderColor = Theme.Bg3;
        b.FlatAppearance.MouseOverBackColor = Theme.Bg2;
        return b;
    }

    /// <summary>Thread-safe update entry point.</summary>
    public void UpdateState(MonitorState state)
    {
        if (InvokeRequired) { BeginInvoke(() => ApplyState(state)); return; }
        ApplyState(state);
    }

    private void ApplyState(MonitorState state)
    {
        _lastUpdate = state.LastUpdate;

        var (text, color) = state.Overall switch
        {
            OverallStatus.Green  => ("● Всё в порядке", Theme.Green),
            OverallStatus.Yellow => ("◑ Обнаружены проблемы", Theme.Yellow),
            OverallStatus.Red    => ("● Связь нарушена", Theme.Red),
            _                    => ("○ Проверяю...", Theme.FgDim),
        };
        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;

        _diagLabel.Text = state.Diagnosis;
        _diagLabel.ForeColor = color;

        Icon = IconFactory.Create(state.Overall);

        RebuildList(state);
    }

    private void RebuildList(MonitorState state)
    {
        _listPanel.SuspendLayout();
        _listPanel.Controls.Clear();

        var sorted = state.Checks.Values
            .OrderBy(r => Array.IndexOf(CatOrder, r.Category))
            .ThenBy(r => r.Name)
            .ToList();

        int y = 4;
        CheckCategory? curCat = null;
        int width = _listPanel.ClientSize.Width - 24;

        foreach (var r in sorted)
        {
            if (curCat != r.Category)
            {
                curCat = r.Category;
                var section = new Label
                {
                    Text = "  " + CatLabel.GetValueOrDefault(r.Category, r.Category.ToString()),
                    ForeColor = Theme.Lavender,
                    Font = new Font(Theme.FontFamily, 8f, FontStyle.Bold),
                    BackColor = Theme.Bg3,
                    Size = new Size(width, 24),
                    Location = new Point(8, y),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                };
                _listPanel.Controls.Add(section);
                y += 28;
            }

            var row = MakeRow(r, width);
            row.Location = new Point(8, y);
            _listPanel.Controls.Add(row);
            y += row.Height + 4;
        }

        _listPanel.ResumeLayout();
    }

    private Control MakeRow(CheckResult r, int width)
    {
        var panel = new Panel
        {
            Size = new Size(width, 46),
            BackColor = Theme.Bg2,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        // Colored dot
        var dotColor = r.Ok ? Theme.Green : Theme.Red;
        var dot = new Label
        {
            Text = "●",
            ForeColor = dotColor,
            Font = new Font(Theme.FontFamily, 13f),
            Size = new Size(28, 46),
            Location = new Point(6, 0),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        panel.Controls.Add(dot);

        // Check name
        var name = new Label
        {
            Text = r.Name,
            ForeColor = Theme.Fg,
            Font = new Font(Theme.FontFamily, 9.5f),
            Size = new Size(230, 46),
            Location = new Point(38, 0),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        panel.Controls.Add(name);

        // Comment (big) + raw value (small)
        var comment = new Label
        {
            Text = r.Comment.Length > 0 ? r.Comment : r.Message,
            ForeColor = r.Ok ? Theme.Green : Theme.Yellow,
            Font = new Font(Theme.FontFamily, 9.5f),
            Location = new Point(276, 5),
            Size = new Size(width - 286, 22),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        panel.Controls.Add(comment);

        if (r.Comment.Length > 0 && r.Message.Length > 0)
        {
            var raw = new Label
            {
                Text = r.Message,
                ForeColor = Theme.FgDim,
                Font = new Font(Theme.FontFamily, 8f),
                Location = new Point(276, 25),
                Size = new Size(width - 286, 16),
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            panel.Controls.Add(raw);
        }

        return panel;
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
}
