using System.Diagnostics;
using System.IO.Compression;

namespace VlessMonitor.Core;

/// <summary>
/// Downloads xray-core (if missing) and runs it as a child process,
/// exposing a local SOCKS5 proxy for the VLESS tunnel.
/// </summary>
public class XrayManager
{
    private const string XrayDownloadUrl =
        "https://github.com/XTLS/Xray-core/releases/latest/download/Xray-windows-64.zip";

    public static string XrayDir => Path.Combine(AppContext.BaseDirectory, "xray");
    public static string XrayExe => Path.Combine(XrayDir, "xray.exe");

    private Process? _process;
    private string? _configFile;
    private readonly object _lock = new();

    public VlessUri Vless;
    public int SocksPort;

    public XrayManager(VlessUri vless, int socksPort)
    {
        Vless = vless;
        SocksPort = socksPort;
    }

    public bool IsRunning
    {
        get { lock (_lock) return _process is { HasExited: false }; }
    }

    public static bool XrayExists => File.Exists(XrayExe);

    /// <summary>Download and extract xray.exe. Reports progress via callback.</summary>
    public static async Task<bool> DownloadXrayAsync(Action<string> progress)
    {
        try
        {
            Directory.CreateDirectory(XrayDir);
            var zipPath = Path.Combine(XrayDir, "xray.zip");

            progress("Скачивание xray-core...");
            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromMinutes(3);
                http.DefaultRequestHeaders.Add("User-Agent", "VlessMonitor");

                using var resp = await http.GetAsync(
                    XrayDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();

                var total = resp.Content.Headers.ContentLength ?? 0;
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(zipPath);

                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buffer)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n));
                    read += n;
                    if (total > 0)
                        progress($"Скачивание xray-core... {read * 100 / total}%");
                }
            }

            progress("Распаковка...");
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var entry = zip.Entries.FirstOrDefault(
                    e => e.Name.Equals("xray.exe", StringComparison.OrdinalIgnoreCase));
                if (entry == null) { progress("xray.exe не найден в архиве"); return false; }
                entry.ExtractToFile(XrayExe, overwrite: true);
            }
            File.Delete(zipPath);

            progress("xray-core готов");
            return true;
        }
        catch (Exception ex)
        {
            progress($"Ошибка: {ex.Message}");
            return false;
        }
    }

    public bool Start()
    {
        lock (_lock)
        {
            if (_process is { HasExited: false }) return true;
            if (!XrayExists) return false;

            var config = Vless.BuildXrayConfig(SocksPort);
            _configFile = Path.Combine(Path.GetTempPath(),
                $"xray_cfg_{Guid.NewGuid():N}.json");
            File.WriteAllText(_configFile, config);

            try
            {
                _process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = XrayExe,
                        Arguments = $"run -c \"{_configFile}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    }
                };
                _process.Start();
                Thread.Sleep(800); // let it bind the port
                if (_process.HasExited) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            try
            {
                if (_process is { HasExited: false })
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(3000);
                }
            }
            catch { /* ignore */ }
            _process = null;

            if (_configFile != null && File.Exists(_configFile))
            {
                try { File.Delete(_configFile); } catch { }
                _configFile = null;
            }
        }
    }

    public bool Restart()
    {
        Stop();
        Thread.Sleep(400);
        return Start();
    }
}
