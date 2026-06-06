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
    private readonly CheckBox _storeHistory;
    private readonly CheckBox _verboseLog;

    public Config Result => _cfg;

    public SettingsForm(Config cfg, bool firstRun = false)
    {
        _cfg = cfg;
        Text = firstRun
            ? "VLESS Monitor — первый запуск: вставьте VLESS-ссылку"
            : "Настройки — VLESS Monitor";
        Size = new Size(780, 640);
        MinimumSize = new Size(720, 560);
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
        AddRow(glVless, "VLESS-ссылка:", _uri,
            "Ссылка вашего прокси из панели x-ui / клиента. Из неё берётся адрес сервера и параметры туннеля.");
        _port = MakeNum(1024, 65535, 1);
        AddRow(glVless, "Локальный SOCKS5 порт:", _port,
            "Порт на вашем ПК, который поднимает встроенный xray. Меняйте, только если 10808 уже занят другой программой.");
        root.Controls.Add(grpVless);

        // ── General checks group ──
        var grpChk = MakeGroup("Параметры проверок", out var glChk);
        _interval = MakeNum(10, 3600, 5); _interval.Value = 30;
        AddRow(glChk, "Интервал проверки (сек):", _interval,
            "Как часто опрашивать сервер и туннель. Меньше — быстрее заметит проблему, но чуть больше трафика и нагрузки.");
        _storeHistory = MakeCheck("Хранить историю проверок");
        AddCheckRow(glChk, _storeHistory,
            "Сохранять историю на диск (папка logs), чтобы графики оставались после перезапуска. Выключено — история только в памяти.");
        root.Controls.Add(grpChk);

        // ── DPI params group ──
        var grpDpi = MakeGroup("Параметры DPI — блокировки и замедления", out var glDpi);
        _dpiKb = MakeNum(8, 256, 4); _dpiKb.Value = 24;
        AddRow(glDpi, "Размер DPI-теста (КБ):", _dpiKb,
            "Сколько данных качать через туннель в тесте на заморозку. РКН рвёт соединение после ~16 КБ — тест должен быть больше, поэтому 24.");
        _ratio = MakeNum(2, 20, 1); _ratio.Value = 4;
        AddRow(glDpi, "Порог замедления (×):", _ratio,
            "Во сколько раз туннель может быть медленнее прямого соединения, прежде чем счесть это замедлением (throttling). 4 — разумный дефолт.");
        root.Controls.Add(grpDpi);

        // ── Telegram group ──
        var grpTg = MakeGroup("Уведомления Telegram (необязательно)", out var glTg);
        _tgEnable = MakeCheck("Включить уведомления");
        AddCheckRow(glTg, _tgEnable,
            "Присылать в Telegram сообщение при смене статуса (например, когда туннель упал).");
        _tgToken = MakeText("123456:ABC...");
        AddRow(glTg, "Bot token:", _tgToken, "Токен бота от @BotFather.");
        _tgChat = MakeText("-100...");
        AddRow(glTg, "Chat ID:", _tgChat, "ID чата или канала, куда слать уведомления.");
        root.Controls.Add(grpTg);

        // ── System group ──
        var grpSys = MakeGroup("Система", out var glSys);
        _autostart = MakeCheck("Запускать вместе с Windows");
        AddCheckRow(glSys, _autostart,
            "Стартовать свёрнутым в трей при входе в Windows.");
        _verboseLog = MakeCheck("Подробный лог (debug)");
        AddCheckRow(glSys, _verboseLog,
            "Писать в лог расширенную отладку, включая вывод xray. Включайте, когда нужно воспроизвести проблему для отчёта.");
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
        _storeHistory.Checked = _cfg.StoreHistory;
        _verboseLog.Checked = _cfg.VerboseLog;
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
        _cfg.StoreHistory = _storeHistory.Checked;
        _cfg.VerboseLog = _verboseLog.Checked;

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
            ColumnCount = 3,
            AutoSize = true,
            BackColor = Theme.Bg,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); // label
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200)); // field
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));  // hint
        grp.Controls.Add(layout);
        return grp;
    }

    private void AddRow(TableLayoutPanel layout, string label, Control field, string? hint = null)
    {
        int row = layout.RowCount;
        var lbl = new Label
        {
            Text = label, ForeColor = Theme.Fg,
            Font = new Font(Theme.UiFont, 9.5f),
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left, AutoSize = true,
            Margin = new Padding(0, 8, 8, 4),
        };
        layout.Controls.Add(lbl, 0, row);
        layout.Controls.Add(field, 1, row);
        if (hint != null) layout.Controls.Add(MakeHint(hint), 2, row);
        layout.RowCount++;
    }

    /// <summary>A checkbox row: checkbox in the field column, hint on the right.</summary>
    private void AddCheckRow(TableLayoutPanel layout, CheckBox box, string hint)
    {
        int row = layout.RowCount;
        layout.Controls.Add(new Label { AutoSize = true, Margin = new Padding(0) }, 0, row);
        layout.Controls.Add(box, 1, row);
        layout.Controls.Add(MakeHint(hint), 2, row);
        layout.RowCount++;
    }

    private Label MakeHint(string text) => new()
    {
        Text = text,
        ForeColor = Theme.FgDim,
        Font = new Font(Theme.UiFont, 8.5f),
        AutoSize = true,
        MaximumSize = new Size(340, 0),  // wrap at ~340px, grow vertically
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(12, 6, 0, 4),
    };

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
