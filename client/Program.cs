using System.Threading;
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

        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        Application.Run(new TrayApplicationContext());
    }
}
