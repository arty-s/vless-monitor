using VlessMonitor.Core;

namespace VlessMonitor.UI;

/// <summary>
/// The heart of the app: owns the tray icon, the checker, xray, and all windows.
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusItem;
    private Config _cfg;
    private Checker _checker = null!;
    private XrayManager _xray = null!;
    private StatusForm? _statusForm;
    private OverallStatus _lastStatus = OverallStatus.Unknown;

    public TrayApplicationContext()
    {
        Logger.Start();
        _cfg = Config.Load();
        Logger.Info($"Конфиг загружен: VLESS={(string.IsNullOrEmpty(_cfg.VlessUri) ? "<пусто>" : _cfg.VpsHost + ":" + _cfg.VlessPort)}, " +
                    $"интервал={_cfg.CheckIntervalSec}с, DPI-проба={_cfg.DpiProbeSizeKb}КБ, порог={_cfg.LatencyRatioThreshold}×");

        // ── Ensure xray exists: embedded resource first, download as fallback ──
        if (!XrayManager.XrayExists && !XrayManager.EnsureFromEmbedded())
        {
            Logger.Warn("Встроенный xray.exe не найден — предлагаю скачать.");
            using var dlg = new DownloadForm();
            if (dlg.ShowDialog() != DialogResult.OK || !dlg.Success)
            {
                ExitThread();
                _tray = null!;
                _statusItem = null!;
                return;
            }
        }

        // ── First run: need a VLESS URI ──
        if (string.IsNullOrWhiteSpace(_cfg.VlessUri))
        {
            using var settings = new SettingsForm(_cfg, firstRun: true);
            if (settings.ShowDialog() != DialogResult.OK ||
                string.IsNullOrWhiteSpace(_cfg.VlessUri))
            {
                ExitThread();
                _tray = null!;
                _statusItem = null!;
                return;
            }
            _cfg = settings.Result;
        }

        // ── Tray icon + menu ──
        _statusItem = new ToolStripMenuItem("○  Инициализация...") { Enabled = false };

        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer() };
        menu.BackColor = Theme.Bg;
        menu.ForeColor = Theme.Fg;
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("📊  Открыть статусы", null, (_, _) => OpenStatus());
        menu.Items.Add("⟳  Проверить сейчас", null, (_, _) => _checker.RunNow());
        menu.Items.Add("⚙  Настройки", null, (_, _) => OpenSettings());
        menu.Items.Add("📁  Открыть папку логов", null, (_, _) => OpenLogsFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("✕  Выход", null, (_, _) => ExitApp());

        _tray = new NotifyIcon
        {
            Icon = IconFactory.Create(OverallStatus.Unknown),
            Text = "VLESS Monitor: проверка...",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => OpenStatus();

        // ── Start xray ──
        var vless = VlessUri.Parse(_cfg.VlessUri);
        _xray = new XrayManager(vless, _cfg.LocalSocks5Port);
        if (!_xray.Start())
        {
            MessageBox.Show(
                "Не удалось запустить xray.\nПроверьте VLESS-ссылку в настройках.",
                "Ошибка xray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ── Start checker ──
        _checker = new Checker(_cfg);
        _checker.XrayRunningProbe = () => _xray.IsRunning;
        _checker.StateChanged += OnStateChanged;
        _checker.Start();
        Logger.Info("Мониторинг запущен.");
    }

    private void OpenLogsFolder()
    {
        try
        {
            Directory.CreateDirectory(Logger.LogDir);
            System.Diagnostics.Process.Start("explorer.exe", Logger.LogDir);
        }
        catch (Exception ex) { Logger.Error("Не удалось открыть папку логов", ex); }
    }

    private void OnStateChanged(MonitorState state)
    {
        // Tray updates must happen on the UI thread
        if (_tray == null) return;
        try
        {
            if (_statusForm?.IsHandleCreated == true)
                _statusForm.UpdateState(state);

            UpdateTray(state);
        }
        catch { /* shutting down */ }
    }

    private void UpdateTray(MonitorState state)
    {
        void Apply()
        {
            var (icon, tip) = state.Overall switch
            {
                OverallStatus.Green  => ("● Всё OK", "VLESS Monitor: всё работает"),
                OverallStatus.Yellow => ("◑ Проблемы", "VLESS Monitor: есть проблемы"),
                OverallStatus.Red    => ("● Обрыв", "VLESS Monitor: связь нарушена"),
                _                    => ("○", "VLESS Monitor: проверка..."),
            };

            var firstLine = state.Diagnosis.Split('\n')[0];
            _statusItem.Text = $"{icon}  {Truncate(firstLine, 50)}";

            if (state.Overall != _lastStatus)
            {
                _lastStatus = state.Overall;
                var old = _tray.Icon;
                _tray.Icon = IconFactory.Create(state.Overall);
                old?.Dispose();
            }
            _tray.Text = Truncate(tip, 63);
        }

        if (_statusItem.Owner?.InvokeRequired == true)
            _statusItem.Owner.BeginInvoke(Apply);
        else
            Apply();
    }

    private static string Truncate(string s, int n) =>
        s.Length <= n ? s : s[..n];

    private void OpenStatus()
    {
        if (_statusForm == null || _statusForm.IsDisposed)
        {
            _statusForm = new StatusForm();
            _statusForm.RefreshRequested += () => _checker.RunNow();
            _statusForm.SettingsRequested += OpenSettings;
            _statusForm.UpdateState(_checker.State);
        }
        _statusForm.ShowAndFocus();
        _statusForm.UpdateState(_checker.State);
    }

    private void OpenSettings()
    {
        using var settings = new SettingsForm(_cfg);
        if (settings.ShowDialog() == DialogResult.OK)
        {
            Logger.Info("Настройки изменены пользователем — применяю.");
            _cfg = settings.Result;
            _checker.Cfg = _cfg;

            // Restart xray with new VLESS / port
            var vless = VlessUri.Parse(_cfg.VlessUri);
            _xray.Vless = vless;
            _xray.SocksPort = _cfg.LocalSocks5Port;
            _xray.Restart();

            _checker.RunNow();
        }
    }

    private void ExitApp()
    {
        Logger.Info("Выход по запросу пользователя.");
        try { _checker?.Stop(); } catch { }
        try { _xray?.Stop(); } catch { }
        Logger.Stop();
        if (_tray != null) _tray.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _xray?.Stop(); } catch { }
            _tray?.Dispose();
        }
        base.Dispose(disposing);
    }
}
