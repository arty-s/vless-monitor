using System.Threading;
using VlessMonitor.Core;
using VlessMonitor.UI;

namespace VlessMonitor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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
}
