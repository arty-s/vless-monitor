using Microsoft.Win32;
using VlessMonitor.Core;

namespace VlessMonitor.UI;

/// <summary>Settings dialog. On OK, writes config.json and returns DialogResult.OK.</summary>
public class SettingsForm : Form
{
    private const string AutorunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutorunName = "VlessMonitor";

    private readonly Config _cfg;
    private readonly TextBox _uri;
    private readonly NumericUpDown _port;
    private readonly NumericUpDown _interval;
    private readonly NumericUpDown _dpiKb;
    private readonly NumericUpDown _ratio;
    private readonly CheckBox _tgEnable;
    private readonly TextBox _tgToken;
    private readonly TextBox _tgChat;
    private readonly CheckBox _autostart;

    public Config Result => _cfg;

    public SettingsForm(Config cfg, bool firstRun = false)
    {
        _cfg = cfg;
        Text = firstRun
            ? "VLESS Monitor — первый запуск: вставьте VLESS-ссылку"
            : "Настройки — VLESS Monitor";
        Size = new Size(640, 560);
        MinimumSize = new Size(600, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Theme.Refresh();
        BackColor = Theme.Bg;
        ForeColor = Theme.Fg;
        Font = new Font(Theme.UiFont, 9.5f);
        Icon = IconFactory.Create(OverallStatus.Unknown);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(16),
            BackColor = Theme.Bg,
            AutoScroll = true,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // ── VLESS group ──
        var grpVless = MakeGroup("VLESS подключение", out var glVless);
        _uri = MakeText("vless://UUID@host:port?...");
        AddRow(glVless, "VLESS-ссылка:", _uri);
        _port = MakeNum(1024, 65535, 1);
        AddRow(glVless, "Локальный SOCKS5 порт:", _port);
        root.Controls.Add(grpVless);

        // ── Checks group ──
        var grpChk = MakeGroup("Параметры проверок", out var glChk);
        _interval = MakeNum(10, 3600, 5); _interval.Value = 30;
        AddRow(glChk, "Интервал проверки (сек):", _interval);
        _dpiKb = MakeNum(8, 256, 4); _dpiKb.Value = 24;
        AddRow(glChk, "Размер DPI-теста (КБ):", _dpiKb);
        _ratio = MakeNum(2, 20, 1); _ratio.Value = 4;
        AddRow(glChk, "Порог замедления (×):", _ratio);
        root.Controls.Add(grpChk);

        // ── Telegram group ──
        var grpTg = MakeGroup("Уведомления Telegram (необязательно)", out var glTg);
        _tgEnable = MakeCheck("Включить уведомления");
        glTg.Controls.Add(_tgEnable, 0, glTg.RowCount); glTg.SetColumnSpan(_tgEnable, 2);
        glTg.RowCount++;
        _tgToken = MakeText("123456:ABC...");
        AddRow(glTg, "Bot token:", _tgToken);
        _tgChat = MakeText("-100...");
        AddRow(glTg, "Chat ID:", _tgChat);
        root.Controls.Add(grpTg);

        // ── System group ──
        var grpSys = MakeGroup("Система", out var glSys);
        _autostart = MakeCheck("Запускать вместе с Windows");
        glSys.Controls.Add(_autostart, 0, 0); glSys.SetColumnSpan(_autostart, 2);
        glSys.RowCount++;
        root.Controls.Add(grpSys);

        // ── Buttons ──
        var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Bg };
        var btnSave = new Button
        {
            Text = "Сохранить", Size = new Size(120, 34),
            FlatStyle = FlatStyle.Flat, BackColor = Theme.Bg3, ForeColor = Theme.Green,
            Cursor = Cursors.Hand, Anchor = AnchorStyles.Right | AnchorStyles.Top,
        };
        btnSave.FlatAppearance.BorderColor = Theme.Green;
        btnSave.Click += OnSave;

        var btnCancel = new Button
        {
            Text = "Отмена", Size = new Size(100, 34),
            FlatStyle = FlatStyle.Flat, BackColor = Theme.Bg3, ForeColor = Theme.FgDim,
            Cursor = Cursors.Hand, Anchor = AnchorStyles.Right | AnchorStyles.Top,
        };
        btnCancel.FlatAppearance.BorderColor = Theme.Bg3;
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        btnPanel.Controls.Add(btnSave);
        btnPanel.Controls.Add(btnCancel);
        btnPanel.Resize += (_, _) =>
        {
            btnSave.Location = new Point(btnPanel.Width - btnSave.Width - 16, 11);
            btnCancel.Location = new Point(btnSave.Left - btnCancel.Width - 8, 11);
        };

        Controls.Add(root);
        Controls.Add(btnPanel);

        LoadValues();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.UseDarkTitleBar(Handle, Theme.Current.IsDark);
        Native.UseRoundedCorners(Handle);
    }

    private void LoadValues()
    {
        _uri.Text = _cfg.VlessUri;
        _port.Value = Clamp(_cfg.LocalSocks5Port, _port);
        _interval.Value = Clamp(_cfg.CheckIntervalSec, _interval);
        _dpiKb.Value = Clamp(_cfg.DpiProbeSizeKb, _dpiKb);
        _ratio.Value = Clamp((int)_cfg.LatencyRatioThreshold, _ratio);
        _tgEnable.Checked = _cfg.NotifyTelegram;
        _tgToken.Text = _cfg.TelegramBotToken;
        _tgChat.Text = _cfg.TelegramChatId;
        _autostart.Checked = IsAutostartSet();
    }

    private static decimal Clamp(int v, NumericUpDown n) =>
        Math.Max(n.Minimum, Math.Min(n.Maximum, v));

    private void OnSave(object? sender, EventArgs e)
    {
        var uri = _uri.Text.Trim();
        if (uri.Length > 0 && !uri.StartsWith("vless://"))
        {
            MessageBox.Show("VLESS-ссылка должна начинаться с vless://",
                "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (uri.Length == 0)
        {
            MessageBox.Show("Вставьте VLESS-ссылку — без неё мониторинг не запустится.",
                "Нужна ссылка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _cfg.VlessUri = uri;
        _cfg.LocalSocks5Port = (int)_port.Value;
        _cfg.CheckIntervalSec = (int)_interval.Value;
        _cfg.DpiProbeSizeKb = (int)_dpiKb.Value;
        _cfg.LatencyRatioThreshold = (double)_ratio.Value;
        _cfg.NotifyTelegram = _tgEnable.Checked;
        _cfg.TelegramBotToken = _tgToken.Text.Trim();
        _cfg.TelegramChatId = _tgChat.Text.Trim();
        _cfg.StartWithWindows = _autostart.Checked;

        try { _cfg.Save(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось сохранить config.json:\n{ex.Message}",
                "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        SetAutostart(_autostart.Checked);

        DialogResult = DialogResult.OK;
        Close();
    }

    // ── Autostart via registry ──
    private static bool IsAutostartSet()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(AutorunKey);
            return k?.GetValue(AutorunName) != null;
        }
        catch { return false; }
    }

    private static void SetAutostart(bool enable)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(AutorunKey, writable: true);
            if (k == null) return;
            if (enable)
            {
                var exe = Environment.ProcessPath ?? Application.ExecutablePath;
                k.SetValue(AutorunName, $"\"{exe}\"");
            }
            else if (k.GetValue(AutorunName) != null)
            {
                k.DeleteValue(AutorunName, throwOnMissingValue: false);
            }
        }
        catch { /* non-critical */ }
    }

    // ── UI builder helpers ──
    private GroupBox MakeGroup(string title, out TableLayoutPanel layout)
    {
        var grp = new GroupBox
        {
            Text = title,
            ForeColor = Theme.Lavender,
            Font = new Font(Theme.FontFamily, 9f, FontStyle.Bold),
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 6, 10, 10),
            Margin = new Padding(0, 0, 0, 12),
        };
        layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            BackColor = Theme.Bg,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grp.Controls.Add(layout);
        return grp;
    }

    private void AddRow(TableLayoutPanel layout, string label, Control field)
    {
        int row = layout.RowCount;
        var lbl = new Label
        {
            Text = label, ForeColor = Theme.Fg,
            Font = new Font(Theme.FontFamily, 9.5f),
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left, AutoSize = true,
            Margin = new Padding(0, 8, 8, 4),
        };
        layout.Controls.Add(lbl, 0, row);
        layout.Controls.Add(field, 1, row);
        layout.RowCount++;
    }

    private TextBox MakeText(string placeholder) => new()
    {
        PlaceholderText = placeholder,
        BackColor = Theme.Bg2,
        ForeColor = Theme.Fg,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font(Theme.FontFamily, 9.5f),
        Dock = DockStyle.Fill,
        Margin = new Padding(0, 6, 0, 4),
        Height = 28,
    };

    private NumericUpDown MakeNum(int min, int max, int step) => new()
    {
        Minimum = min, Maximum = max, Increment = step,
        BackColor = Theme.Bg2, ForeColor = Theme.Fg,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font(Theme.FontFamily, 9.5f),
        Width = 130,
        Margin = new Padding(0, 6, 0, 4),
        Anchor = AnchorStyles.Left,
    };

    private CheckBox MakeCheck(string text) => new()
    {
        Text = text, ForeColor = Theme.Fg,
        Font = new Font(Theme.FontFamily, 9.5f),
        AutoSize = true, Margin = new Padding(0, 6, 0, 4),
    };
}
