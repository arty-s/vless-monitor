using System.Net;
using System.Diagnostics;
using System.Text.Json;

namespace VlessMonitor.Checks;

/// <summary>Checks that flow through the local VLESS SOCKS5 proxy, plus DPI heuristics.</summary>
public static class Tunnel
{
    private const string UA =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124.0.0.0 Safari/537.36";

    private static HttpClient MakeClient(string? proxyUrl, TimeSpan timeout)
    {
        var handler = new HttpClientHandler();
        if (proxyUrl != null)
        {
            handler.Proxy = new WebProxy(proxyUrl); // .NET 6+ supports socks5://
            handler.UseProxy = true;
        }
        var client = new HttpClient(handler) { Timeout = timeout };
        client.DefaultRequestHeaders.Add("User-Agent", UA);
        return client;
    }

    public static async Task<CheckResult> CheckHttpDirectAsync(string vps, int port, string secret)
    {
        const string name = "Probe-сервер (прямое соединение)";
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = MakeClient(null, TimeSpan.FromSeconds(5));
            client.DefaultRequestHeaders.Add("X-Token", secret);
            var resp = await client.GetAsync($"http://{vps}:{port}/health");
            sw.Stop();

            if (resp.IsSuccessStatusCode)
                return Ok(name, sw.ElapsedMilliseconds,
                    "Сервер отвечает напрямую — VPS живой и доступен", CheckCategory.Tunnel);

            return Fail(name, $"HTTP {(int)resp.StatusCode}",
                "Сервер ответил с ошибкой — что-то не так на стороне VPS", CheckCategory.Tunnel);
        }
        catch (TaskCanceledException)
        {
            return Fail(name, "таймаут",
                "VPS не отвечает — возможно перегружен или недоступен", CheckCategory.Tunnel);
        }
        catch
        {
            return Fail(name, "нет соединения",
                "Probe-сервер не запущен или порт не открыт на VPS", CheckCategory.Tunnel);
        }
    }

    public static async Task<CheckResult> CheckTunnelE2EAsync(
        string vps, int port, string secret, string proxyUrl)
    {
        const string name = "Туннель — сквозная проверка";
        var bid = Guid.NewGuid().ToString("N");
        var baseUrl = $"http://{vps}:{port}";
        var sw = Stopwatch.StartNew();

        try
        {
            using var proxied = MakeClient(proxyUrl, TimeSpan.FromSeconds(10));
            proxied.DefaultRequestHeaders.Add("X-Token", secret);
            var resp = await proxied.PostAsync($"{baseUrl}/beacon/{bid}", null);
            sw.Stop();
            if (!resp.IsSuccessStatusCode)
                return Fail(name, $"HTTP {(int)resp.StatusCode}",
                    "Туннель ответил ошибкой — xray запущен, но что-то не так", CheckCategory.Tunnel);
        }
        catch (TaskCanceledException)
        {
            return Fail(name, "таймаут",
                "Данные через туннель не дошли — возможно DPI блокирует VLESS", CheckCategory.Tunnel);
        }
        catch
        {
            return Fail(name, "ошибка прокси",
                "xray не запущен или SOCKS5 порт не отвечает — туннель сломан", CheckCategory.Tunnel);
        }

        // Verify arrival directly
        try
        {
            using var direct = MakeClient(null, TimeSpan.FromSeconds(5));
            direct.DefaultRequestHeaders.Add("X-Token", secret);
            var verify = await direct.GetAsync($"{baseUrl}/beacon/{bid}");
            if (verify.IsSuccessStatusCode)
            {
                var body = await verify.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("received", out var r) && r.GetBoolean())
                {
                    var ms = sw.ElapsedMilliseconds;
                    var speed = ms < 200 ? "быстро" : ms < 600 ? "нормально" : "медленно";
                    return Ok(name, ms,
                        $"Туннель работает — данные проходят насквозь ({speed})", CheckCategory.Tunnel);
                }
            }
        }
        catch { /* fall through */ }

        return Fail(name, "маяк не дошёл",
            "Запрос ушёл, но до сервера не добрался — DPI режет трафик на полпути", CheckCategory.Tunnel);
    }

    public static async Task<CheckResult> CheckExternalHttpAsync(string proxyUrl)
    {
        const string name = "Интернет через туннель";
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = MakeClient(proxyUrl, TimeSpan.FromSeconds(8));
            var resp = await client.GetAsync("http://cp.cloudflare.com/");
            sw.Stop();
            if (resp.IsSuccessStatusCode)
            {
                var ms = sw.ElapsedMilliseconds;
                var speed = ms < 300 ? "быстро" : ms < 800 ? "нормально" : "медленно";
                return Ok(name, ms, $"Интернет через туннель работает ({speed})", CheckCategory.Tunnel);
            }
            return Fail(name, $"HTTP {(int)resp.StatusCode}",
                "Туннель работает, но сайт ответил ошибкой — временная проблема", CheckCategory.Tunnel);
        }
        catch (TaskCanceledException)
        {
            return Fail(name, "таймаут",
                "Интернет через туннель не работает — возможно DPI блокирует VLESS", CheckCategory.Tunnel);
        }
        catch
        {
            return Fail(name, "ошибка прокси",
                "xray не запущен — локальный SOCKS5 прокси не отвечает", CheckCategory.Tunnel);
        }
    }

    public static async Task<CheckResult> CheckDpiFreezeAsync(
        string vps, int port, string secret, string proxyUrl, int probeKb)
    {
        const string name = "DPI: тест на заморозку 16 КБ";
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = MakeClient(proxyUrl, TimeSpan.FromSeconds(15));
            client.DefaultRequestHeaders.Add("X-Token", secret);

            using var resp = await client.GetAsync(
                $"http://{vps}:{port}/stream/{probeKb}",
                HttpCompletionOption.ResponseHeadersRead);

            await using var stream = await resp.Content.ReadAsStreamAsync();
            var buffer = new byte[1024];
            long received = 0;
            var lastChunk = Stopwatch.StartNew();
            double? stallAt = null;
            int n;
            while ((n = await stream.ReadAsync(buffer)) > 0)
            {
                received += n;
                if (lastChunk.Elapsed.TotalSeconds > 4.0 && stallAt == null)
                    stallAt = received / 1024.0;
                lastChunk.Restart();
            }
            sw.Stop();

            double receivedKb = received / 1024.0;
            if (receivedKb < 12)
                return Fail(name, $"стоп на {receivedKb:F0} КБ",
                    $"Обнаружена блокировка РКН — соединение обрывается на {receivedKb:F0} КБ. " +
                    "Это признак правила «25 пакетов» систем ТСПУ", CheckCategory.Dpi);

            if (stallAt != null)
                return Fail(name, $"зависание на {stallAt:F0} КБ",
                    $"Провайдер замедляет соединение после {stallAt:F0} КБ. " +
                    "Это throttling — не полная блокировка, но трафик искусственно тормозится",
                    CheckCategory.Dpi);

            double kbps = received / (sw.Elapsed.TotalSeconds + 0.001) / 1024.0;
            return new CheckResult
            {
                Name = name, Ok = true, Value = Math.Round(kbps),
                Message = $"{receivedKb:F0} КБ @ {kbps:F0} КБ/с",
                Comment = "Блокировки 16 КБ не обнаружено — данные проходят без обрывов",
                Category = CheckCategory.Dpi,
            };
        }
        catch (TaskCanceledException)
        {
            return Fail(name, "таймаут",
                "Соединение зависло — возможно DPI заморозил передачу данных", CheckCategory.Dpi);
        }
        catch
        {
            return Fail(name, "ошибка",
                "Не удалось выполнить тест — туннель недоступен", CheckCategory.Dpi);
        }
    }

    public static CheckResult CheckLatencyRatio(double? directMs, double? tunnelMs, double threshold)
    {
        const string name = "DPI: соотношение задержек";
        if (directMs == null || tunnelMs == null)
            return Fail(name, "нет данных",
                "Нет данных для сравнения — другие проверки не выполнились", CheckCategory.Dpi);

        double ratio = tunnelMs.Value / Math.Max(directMs.Value, 1);
        if (ratio > threshold)
            return Fail(name, $"{ratio:F1}× (прямо {directMs:F0} / туннель {tunnelMs:F0} мс)",
                $"Туннель в {ratio:F1} раз медленнее прямого соединения — " +
                "вероятно провайдер искусственно замедляет VLESS-трафик", CheckCategory.Dpi);

        var comment = ratio > threshold * 0.6
            ? "Небольшое замедление туннеля — пока в пределах нормы"
            : "Туннель работает без замедлений — накладные расходы в норме";
        return new CheckResult
        {
            Name = name, Ok = true, Value = Math.Round(ratio, 1),
            Message = $"{ratio:F1}× ({directMs:F0} / {tunnelMs:F0} мс)",
            Comment = comment, Category = CheckCategory.Dpi,
        };
    }

    public static async Task<CheckResult> CheckXrayStatsAsync(string vps, int port, string secret)
    {
        const string name = "Статистика xray";
        try
        {
            using var client = MakeClient(null, TimeSpan.FromSeconds(5));
            client.DefaultRequestHeaders.Add("X-Token", secret);
            var resp = await client.GetAsync($"http://{vps}:{port}/xray/stats");
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                double up = doc.RootElement.TryGetProperty("uplink_mb", out var u) ? u.GetDouble() : 0;
                double dn = doc.RootElement.TryGetProperty("downlink_mb", out var d) ? d.GetDouble() : 0;
                var comment = (up + dn) == 0
                    ? "Трафик пока не зафиксирован — возможно xray только запустился"
                    : "Счётчики трафика xray — суммарно с момента последнего перезапуска";
                return new CheckResult
                {
                    Name = name, Ok = true,
                    Message = $"↑{up:F1}  ↓{dn:F1} МБ",
                    Comment = comment, Category = CheckCategory.Stats,
                };
            }
            return Fail(name, $"HTTP {(int)resp.StatusCode}",
                "Не удалось получить статистику с сервера", CheckCategory.Stats);
        }
        catch
        {
            return Fail(name, "недоступно",
                "Probe-сервер недоступен — статистика недоступна", CheckCategory.Stats);
        }
    }

    // ── Helpers ──────────────────────────────────────────────
    private static CheckResult Ok(string name, double ms, string comment, CheckCategory cat) =>
        new() { Name = name, Ok = true, Value = ms, Message = $"{ms:F0} мс", Comment = comment, Category = cat };

    private static CheckResult Fail(string name, string msg, string comment, CheckCategory cat) =>
        new() { Name = name, Ok = false, Message = msg, Comment = comment, Category = cat };
}
