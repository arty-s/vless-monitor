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
