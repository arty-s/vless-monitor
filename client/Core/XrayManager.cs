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

    /// <summary>
    /// Ensures xray.exe is on disk: extracts the embedded copy if present,
    /// otherwise leaves it to the caller to download. Returns true if available.
    /// </summary>
    public static bool EnsureFromEmbedded()
    {
        if (XrayExists) return true;
        try
        {
            var asm = typeof(XrayManager).Assembly;
            // Embedded as logical name "xray.exe"
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("xray.exe", StringComparison.OrdinalIgnoreCase));
            if (resName == null) return false;

            using var stream = asm.GetManifestResourceStream(resName);
            if (stream == null) return false;

            Directory.CreateDirectory(XrayDir);
            using (var file = File.Create(XrayExe))
                stream.CopyTo(file);

            Logger.Info($"xray.exe извлечён из встроенного ресурса ({new FileInfo(XrayExe).Length / 1024} КБ)");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось извлечь встроенный xray.exe", ex);
            return false;
        }
    }

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
                    },
                    EnableRaisingEvents = true,
                };

                // D4: capture xray output to the log — xray prints the exact
                // reason on a bad VLESS link / port conflict, which is the most
                // valuable signal when diagnosing other people's setups.
                _process.OutputDataReceived += (_, e) =>
                    { if (e.Data != null) Logger.Debug($"xray: {e.Data}"); };
                _process.ErrorDataReceived += (_, e) =>
                    { if (e.Data != null) Logger.Warn($"xray: {e.Data}"); };
                _process.Exited += (_, _) =>
                    { try { Logger.Warn($"xray процесс завершился (код {_process?.ExitCode})"); } catch { } };

                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                Thread.Sleep(800); // let it bind the port
                if (_process.HasExited)
                {
                    Logger.Error($"xray завершился сразу после старта (код {_process.ExitCode}). " +
                                 "Вероятно неверная VLESS-ссылка или занят порт — подробности в строках 'xray:' выше.");
                    return false;
                }
                Logger.Info($"xray запущен (PID {_process.Id}), SOCKS5 на 127.0.0.1:{SocksPort}, " +
                            $"VLESS {Vless.Host}:{Vless.Port} (security={Vless.Security}, sni={Vless.Sni})");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось запустить xray", ex);
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
        Logger.Info("Перезапуск xray...");
        Stop();
        Thread.Sleep(400);
        return Start();
    }
}
