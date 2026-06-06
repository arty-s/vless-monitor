using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VlessMonitor.Core;

/// <summary>
/// Builds a shareable diagnostic bundle (zip) with logs, sanitized config,
/// environment info and the last check state. Secrets are stripped by default
/// so the bundle is safe to post in a public group.
/// </summary>
public static class Diagnostics
{
    public static string CreateBundle(Config cfg, MonitorState? state, bool includeServerIp)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var zipPath = Path.Combine(desktop, $"vless-monitor-diag-{stamp}.zip");

        var host = cfg.VpsHost;

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // environment.txt
            AddText(zip, "environment.txt", BuildEnvironment(cfg));

            // config.sanitized.json
            AddText(zip, "config.sanitized.json", SanitizeConfig(cfg, includeServerIp));

            // last-state.json
            if (state != null)
                AddText(zip, "last-state.json", BuildStateJson(state));

            // logs/*.log (sanitized)
            try
            {
                foreach (var f in Directory.GetFiles(Logger.LogDir, "vless-monitor-*.log")
                             .OrderByDescending(File.GetLastWriteTime)
                             .Take(7))
                {
                    string text = File.ReadAllText(f);
                    text = SanitizeText(text, host, cfg, includeServerIp);
                    AddText(zip, "logs/" + Path.GetFileName(f), text);
                }
            }
            catch (Exception ex)
            {
                AddText(zip, "logs/_error.txt", "Не удалось приложить логи: " + ex.Message);
            }
        }

        Logger.Info($"Создан диагностический bundle: {zipPath} (IP {(includeServerIp ? "включён" : "замаскирован")})");
        return zipPath;
    }

    public static void OpenInExplorer(string filePath)
    {
        try { Process.Start("explorer.exe", $"/select,\"{filePath}\""); } catch { }
    }

    // ── Builders ─────────────────────────────────────────────
    private static string BuildEnvironment(Config cfg)
    {
        var sb = new StringBuilder();
        var asm = typeof(Diagnostics).Assembly.GetName();
        sb.AppendLine("VLESS Monitor — diagnostic environment");
        sb.AppendLine($"install_id     : {cfg.EnsureInstallId()}");
        sb.AppendLine($"app_version    : {asm.Version}");
        sb.AppendLine($"created        : {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"os             : {Environment.OSVersion}");
        sb.AppendLine($"64bit_os/proc  : {Environment.Is64BitOperatingSystem}/{Environment.Is64BitProcess}");
        sb.AppendLine($"dotnet         : {Environment.Version}");
        sb.AppendLine($"machine_cores  : {Environment.ProcessorCount}");
        sb.AppendLine($"app_dir        : {AppContext.BaseDirectory}");
        sb.AppendLine($"xray_present   : {XrayManager.XrayExists}");
        sb.AppendLine($"xray_version   : {GetXrayVersion()}");
        sb.AppendLine($"store_history  : {cfg.StoreHistory}");
        sb.AppendLine($"verbose_log    : {cfg.VerboseLog}");
        sb.AppendLine($"check_interval : {cfg.CheckIntervalSec}s");
        sb.AppendLine($"socks5_port    : {cfg.LocalSocks5Port}");
        return sb.ToString();
    }

    private static string GetXrayVersion()
    {
        try
        {
            if (!XrayManager.XrayExists) return "(нет)";
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = XrayManager.XrayExe, Arguments = "version",
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
            });
            if (p == null) return "(?)";
            var outp = p.StandardOutput.ReadLine();
            p.WaitForExit(3000);
            return outp ?? "(?)";
        }
        catch { return "(ошибка)"; }
    }

    private static string SanitizeConfig(Config cfg, bool includeServerIp)
    {
        // Work on a shallow clone so we don't mutate the live config.
        var clone = new Config
        {
            VlessUri = RedactVless(cfg.VlessUri, includeServerIp),
            LocalSocks5Port = cfg.LocalSocks5Port,
            CheckIntervalSec = cfg.CheckIntervalSec,
            ProbeServerPort = cfg.ProbeServerPort,
            ProbeSecret = "***REDACTED***",
            DpiProbeSizeKb = cfg.DpiProbeSizeKb,
            LatencyRatioThreshold = cfg.LatencyRatioThreshold,
            StartWithWindows = cfg.StartWithWindows,
            StoreHistory = cfg.StoreHistory,
            VerboseLog = cfg.VerboseLog,
            InstallId = cfg.InstallId,
            NotifyTelegram = cfg.NotifyTelegram,
            TelegramBotToken = string.IsNullOrEmpty(cfg.TelegramBotToken) ? "" : "***REDACTED***",
            TelegramChatId = string.IsNullOrEmpty(cfg.TelegramChatId) ? "" : "***REDACTED***",
            PingTargets = includeServerIp
                ? cfg.PingTargets
                : cfg.PingTargets.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value == cfg.VpsHost ? MaskIp(kv.Value) : kv.Value),
        };
        return JsonSerializer.Serialize(clone, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    private static string RedactVless(string uri, bool includeServerIp)
    {
        if (string.IsNullOrEmpty(uri)) return "";
        try
        {
            var u = new Uri(uri);
            var host = includeServerIp ? u.Host : MaskIp(u.Host);
            // userinfo (uuid) and key query params are credentials → redact
            var q = System.Web.HttpUtility.ParseQueryString(u.Query);
            foreach (var k in new[] { "pbk", "sid", "spx" })
                if (q[k] != null) q[k] = "REDACTED";
            string sni = q["sni"] ?? "";
            if (!includeServerIp && sni.Length > 0) q["sni"] = "REDACTED";
            return $"vless://REDACTED-UUID@{host}:{u.Port}?{q}";
        }
        catch { return "(не удалось разобрать ссылку)"; }
    }

    private static string BuildStateJson(MonitorState state)
    {
        var obj = new
        {
            overall = state.Overall.ToString(),
            diagnosis = state.Diagnosis,
            last_update = state.LastUpdate,
            checks = state.Checks.Values.Select(c => new
            {
                c.Name, c.Ok, c.Value, c.Message, c.Comment,
                category = c.Category.ToString(),
                c.Timestamp,
            }),
        };
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    private static string SanitizeText(string text, string host, Config cfg, bool includeServerIp)
    {
        // Always strip the probe secret if it ever appears.
        if (!string.IsNullOrEmpty(cfg.ProbeSecret))
            text = text.Replace(cfg.ProbeSecret, "***SECRET***");

        if (!includeServerIp)
        {
            if (!string.IsNullOrEmpty(host))
                text = text.Replace(host, MaskIp(host));
            // generic IPv4 masking as a safety net
            text = Regex.Replace(text, @"\b(\d{1,3})\.(\d{1,3})\.\d{1,3}\.\d{1,3}\b",
                m => $"{m.Groups[1].Value}.{m.Groups[2].Value}.x.x");
        }
        return text;
    }

    private static string MaskIp(string ip)
    {
        var parts = ip.Split('.');
        return parts.Length == 4 ? $"{parts[0]}.{parts[1]}.x.x" : "***";
    }

    private static void AddText(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
        w.Write(content);
    }
}
