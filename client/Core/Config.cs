using System.Text.Json;
using System.Text.Json.Serialization;

namespace VlessMonitor.Core;

/// <summary>
/// Application configuration, persisted next to the exe as config.json.
/// </summary>
public class Config
{
    [JsonPropertyName("vless_uri")]
    public string VlessUri { get; set; } = "";

    [JsonPropertyName("local_socks5_port")]
    public int LocalSocks5Port { get; set; } = 10808;

    [JsonPropertyName("check_interval_sec")]
    public int CheckIntervalSec { get; set; } = 30;

    [JsonPropertyName("probe_server_port")]
    public int ProbeServerPort { get; set; } = 8765;

    [JsonPropertyName("probe_secret")]
    public string ProbeSecret { get; set; } = "vless-monitor-secret-2026";

    [JsonPropertyName("dpi_probe_size_kb")]
    public int DpiProbeSizeKb { get; set; } = 24;

    [JsonPropertyName("latency_ratio_threshold")]
    public double LatencyRatioThreshold { get; set; } = 4.0;

    [JsonPropertyName("start_with_windows")]
    public bool StartWithWindows { get; set; } = false;

    [JsonPropertyName("notify_telegram")]
    public bool NotifyTelegram { get; set; } = false;

    [JsonPropertyName("telegram_bot_token")]
    public string TelegramBotToken { get; set; } = "";

    [JsonPropertyName("telegram_chat_id")]
    public string TelegramChatId { get; set; } = "";

    [JsonPropertyName("ping_targets")]
    public Dictionary<string, string> PingTargets { get; set; } = new()
    {
        ["Google DNS (8.8.8.8)"] = "8.8.8.8",
        ["Cloudflare (1.1.1.1)"] = "1.1.1.1",
        ["Яндекс RU (77.88.8.8)"] = "77.88.8.8",
    };

    // ── Derived ──────────────────────────────────────────────
    [JsonIgnore]
    public string VpsHost
    {
        get
        {
            try { return new Uri(VlessUri).Host; }
            catch { return ""; }
        }
    }

    [JsonIgnore]
    public int VlessPort
    {
        get
        {
            try { var p = new Uri(VlessUri).Port; return p > 0 ? p : 443; }
            catch { return 443; }
        }
    }

    [JsonIgnore]
    public string ProxyUrl => $"socks5://127.0.0.1:{LocalSocks5Port}";

    // ── Persistence ──────────────────────────────────────────
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "config.json");

    public static Config Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<Config>(json);
                if (cfg != null) return cfg;
            }
        }
        catch { /* fall through to default */ }
        return new Config();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, JsonOpts);
        File.WriteAllText(ConfigPath, json);
    }
}
