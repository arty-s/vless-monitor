using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Diagnostics;

namespace VlessMonitor.Checks;

/// <summary>Direct connectivity: ICMP ping and TCP port probes.</summary>
public static class Connectivity
{
    private static string PingComment(long ms, int loss)
    {
        if (loss >= 80) return "Не отвечает — сервер недоступен или заблокирован";
        if (loss >= 30) return $"Нестабильно — {loss}% пакетов теряется, возможны помехи";
        if (loss > 0) return $"Небольшие потери ({loss}%) — связь в целом работает";
        if (ms < 30) return "Отлично — связь очень быстрая";
        if (ms < 80) return "Хорошо — связь нормальная";
        if (ms < 150) return "Нормально — задержка заметная, но терпимая";
        if (ms < 300) return "Медленно — высокая задержка, возможны тормоза";
        return "Очень медленно — похоже на замедление провайдером";
    }

    public static async Task<CheckResult> PingHostAsync(string host, string label)
    {
        try
        {
            using var ping = new Ping();
            int ok = 0;
            long totalMs = 0;
            const int count = 3;

            for (int i = 0; i < count; i++)
            {
                try
                {
                    var reply = await ping.SendPingAsync(host, 2000);
                    if (reply.Status == IPStatus.Success)
                    {
                        ok++;
                        totalMs += reply.RoundtripTime;
                    }
                }
                catch { /* count as loss */ }
            }

            int loss = (count - ok) * 100 / count;
            if (ok > 0)
            {
                long avg = totalMs / ok;
                return new CheckResult
                {
                    Name = label,
                    Ok = loss < 80,
                    Value = avg,
                    Message = loss > 0 ? $"{avg} мс, {loss}% потерь" : $"{avg} мс",
                    Comment = PingComment(avg, loss),
                    Category = CheckCategory.Ping,
                };
            }

            return new CheckResult
            {
                Name = label,
                Ok = false,
                Message = "100% потерь",
                Comment = "Не отвечает — адрес недоступен",
                Category = CheckCategory.Ping,
            };
        }
        catch (Exception ex)
        {
            return new CheckResult
            {
                Name = label,
                Ok = false,
                Message = ex.Message,
                Comment = "Ошибка при проверке — возможно нет интернета",
                Category = CheckCategory.Ping,
            };
        }
    }

    public static async Task<CheckResult> TcpProbeAsync(string host, int port,
        string? friendlyName = null)
    {
        var name = friendlyName ?? $"TCP {host}:{port}";
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var timeout = Task.Delay(5000);
            var done = await Task.WhenAny(connectTask, timeout);
            sw.Stop();

            if (done == timeout || !client.Connected)
            {
                return new CheckResult
                {
                    Name = name,
                    Ok = false,
                    Message = "нет ответа",
                    Comment = "Порт закрыт или заблокирован — провайдер не пропускает соединение",
                    Category = CheckCategory.Port,
                };
            }

            return new CheckResult
            {
                Name = name,
                Ok = true,
                Value = sw.ElapsedMilliseconds,
                Message = $"{sw.ElapsedMilliseconds} мс",
                Comment = "Порт открыт — сервер принимает подключения",
                Category = CheckCategory.Port,
            };
        }
        catch
        {
            return new CheckResult
            {
                Name = name,
                Ok = false,
                Message = "нет ответа",
                Comment = "Порт закрыт или заблокирован — провайдер не пропускает соединение",
                Category = CheckCategory.Port,
            };
        }
    }
}
