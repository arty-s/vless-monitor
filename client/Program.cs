using System.Threading;
using VlessMonitor.Core;
using VlessMonitor.Checks;
using VlessMonitor.UI;

namespace VlessMonitor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (Environment.GetEnvironmentVariable("VLESSMON_SELFTEST") == "1")
        {
            SelfTest();
            return;
        }
        if (Environment.GetEnvironmentVariable("VLESSMON_SHOWUI") == "1")
        {
            ShowUiDemo();
            return;
        }
        if (Environment.GetEnvironmentVariable("VLESSMON_INSTALLTEST") == "1")
        {
            InstallTest();
            return;
        }

        // Single-instance guard
        using var mutex = new Mutex(true, "VlessMonitor_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("VLESS Monitor уже запущен (смотрите в системном трее).",
                "Уже запущено", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Catch everything that would otherwise crash silently — straight to the log.
        Application.ThreadException += (_, e) =>
            Logger.Error("Необработанное исключение в UI-потоке", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Logger.Error("Фатальное необработанное исключение", ex);
            Logger.Stop();
        };

        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        Application.Run(new TrayApplicationContext());
    }

    /// <summary>Demo mode (VLESSMON_SHOWUI=1): opens the dashboard with realistic
    /// fake data and keeps it open for visual inspection.</summary>
    private static void ShowUiDemo()
    {
        Logger.Start();
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Theme.Refresh();

        var rnd = new Random();
        CheckResult R(string n, bool ok, double? v, CheckCategory c) =>
            new() { Name = n, Ok = ok, Value = v, Message = v.HasValue ? $"{v:0} мс" : "ok", Comment = "demo", Category = c };

        void Feed()
        {
            MetricsStore.Instance.Record(new[]
            {
                R("VPS (185.121.12.210)", true, 90 + rnd.Next(8), CheckCategory.Ping),
                R("Google DNS (8.8.8.8)", true, 17 + rnd.Next(5), CheckCategory.Ping),
                R("Cloudflare (1.1.1.1)", true, 26 + rnd.Next(5), CheckCategory.Ping),
                R("Яндекс RU (77.88.8.8)", true, 7 + rnd.Next(4), CheckCategory.Ping),
                R("Порт VLESS (47572)", true, null, CheckCategory.Port),
                R("Порт probe (8765)", true, null, CheckCategory.Port),
                R("Локальный клиент xray", true, null, CheckCategory.Tunnel),
                R("Интернет через туннель", true, 340 + rnd.Next(40), CheckCategory.Tunnel),
                R("Туннель — сквозная проверка", true, 320 + rnd.Next(40), CheckCategory.Tunnel),
                R("DPI: соотношение задержек", true, 1.8 + rnd.NextDouble(), CheckCategory.Dpi),
                R("DPI: тест на заморозку 16 КБ", true, 50 + rnd.Next(10), CheckCategory.Dpi),
                R("Статистика xray", true, null, CheckCategory.Stats),
            });
        }
        for (int i = 0; i < 10; i++) { Feed(); Thread.Sleep(30); }

        var win = new MonitorWindow();
        if (Environment.GetEnvironmentVariable("VLESSMON_MAX") == "1")
            win.WindowState = FormWindowState.Maximized;
        var state = new MonitorState { Overall = OverallStatus.Green, Diagnosis = "Всё в порядке. VLESS работает нормально, замедлений нет.", LastUpdate = DateTime.Now, Checks = new() };
        int tab = int.TryParse(Environment.GetEnvironmentVariable("VLESSMON_TAB"), out var tb) ? tb : 0;
        win.Shown += (_, _) => { win.UpdateState(state); if (tab > 0) win.SelectIndex(tab); };

        string capDir = Environment.GetEnvironmentVariable("VLESSMON_CAPDIR") ?? AppContext.BaseDirectory;
        int tick = 0;
        var t = new System.Windows.Forms.Timer { Interval = 1500 };
        t.Tick += (_, _) =>
        {
            Feed();
            win.UpdateState(new MonitorState { Overall = OverallStatus.Green, Diagnosis = state.Diagnosis, LastUpdate = DateTime.Now, Checks = new() });
            tick++;
            if (tick is 2 or 4) // after data has filled in
                try { CaptureWindow(win, Path.Combine(capDir, "ui-capture.png")); } catch { }
        };
        t.Start();
        Application.Run(win);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    private static void CaptureWindow(Form win, string path)
    {
        using var bmp = new Bitmap(win.Width, win.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            IntPtr hdc = g.GetHdc();
            PrintWindow(win.Handle, hdc, 2); // PW_RENDERFULLCONTENT (captures Skia/GPU)
            g.ReleaseHdc(hdc);
        }
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Logger.Info("UI-CAPTURE saved: " + path);
    }

    /// <summary>Headless installer test (VLESSMON_INSTALLTEST=1): runs the full SSH
    /// install against VM_HOST/VM_USER/VM_PASS and logs every step.</summary>
    private static void InstallTest()
    {
        Logger.Start();
        Logger.MinLevel = LogLevel.Debug;
        try
        {
            var cred = new ServerCredentials
            {
                Host = Environment.GetEnvironmentVariable("VM_HOST") ?? "",
                Port = int.TryParse(Environment.GetEnvironmentVariable("VM_PORT"), out var pp) ? pp : 22,
                User = Environment.GetEnvironmentVariable("VM_USER") ?? "root",
                Auth = SshAuth.Password,
                Password = Environment.GetEnvironmentVariable("VM_PASS") ?? "",
            };
            void Log(string s) { Logger.Info("INSTALLTEST " + s); Console.WriteLine(s); }

            using var inst = new ServerInstaller(cred, Log);
            inst.Connect();
            var si = inst.Detect();
            var secret = ServerInstaller.GenerateSecret();
            var res = inst.Install(si, 8765, secret);
            Log($"RESULT success={res.Success} port={res.Port} tunnelOnly={res.TunnelOnly} msg={res.Message}");
            if (res.Success && !res.TunnelOnly)
            {
                bool ext = ServerInstaller.VerifyExternalAsync(cred.Host, res.Port, res.Secret).GetAwaiter().GetResult();
                Log($"EXTERNAL reachable={ext}");
            }
        }
        catch (Exception ex) { Logger.Error("INSTALLTEST упал", ex); Console.WriteLine("FAIL: " + ex.Message); }
        finally { Logger.Stop(); }
    }

    /// <summary>
    /// Headless smoke test (VLESSMON_SELFTEST=1): builds the dashboard with fake
    /// data, shows it briefly, logs any exception, then exits. Verifies that the
    /// ScottPlot/SkiaSharp graphs initialise in the published single-file exe.
    /// </summary>
    private static void SelfTest()
    {
        Logger.Start();
        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Theme.Refresh();

            CheckResult R(string n, bool ok, double? v, CheckCategory c) =>
                new() { Name = n, Ok = ok, Value = v, Message = v.HasValue ? $"{v} мс" : "ok", Comment = "self-test", Category = c };

            for (int i = 0; i < 3; i++)
            {
                MetricsStore.Instance.Record(new[]
                {
                    R("VPS", true, 90 + i, CheckCategory.Ping),
                    R("Google DNS", true, 18 + i, CheckCategory.Ping),
                    R("Порт VLESS (47572)", true, 100, CheckCategory.Port),
                    R("Туннель — сквозная проверка", true, 700 + i * 10, CheckCategory.Tunnel),
                    R("Локальный клиент xray", true, null, CheckCategory.Tunnel),
                    R("DPI: соотношение задержек", true, 3.2, CheckCategory.Dpi),
                });
                Thread.Sleep(20);
            }

            var win = new MonitorWindow();
            var state = new MonitorState
            {
                Overall = OverallStatus.Green,
                Diagnosis = "Self-test OK",
                LastUpdate = DateTime.Now,
                Checks = new(),
            };
            win.Shown += (_, _) => win.UpdateState(state);
            var t = new System.Windows.Forms.Timer { Interval = 2500 };
            t.Tick += (_, _) => { Logger.Info("SELFTEST: окно с графиками создано без краша"); Application.Exit(); };
            t.Start();
            Application.Run(win);
            Logger.Info("SELFTEST: успешно завершён");
        }
        catch (Exception ex)
        {
            Logger.Error("SELFTEST: упал", ex);
        }
        finally { Logger.Stop(); }
    }
}
