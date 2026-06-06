using VlessMonitor.Checks;

namespace VlessMonitor.Core;

public enum OverallStatus { Unknown, Green, Yellow, Red }

public class MonitorState
{
    public Dictionary<string, CheckResult> Checks { get; init; } = new();
    public OverallStatus Overall { get; init; } = OverallStatus.Unknown;
    public string Diagnosis { get; init; } = "Инициализация...";
    public DateTime LastUpdate { get; init; } = DateTime.Now;
}

/// <summary>
/// Runs all checks on a timer, computes the overall traffic-light status,
/// and raises <see cref="StateChanged"/> with a fresh snapshot.
/// </summary>
public class Checker
{
    public Config Cfg;
    public event Action<MonitorState>? StateChanged;

    /// <summary>Optional probe so logs can record whether xray is alive each cycle.</summary>
    public Func<bool>? XrayRunningProbe { set { _xrayRunning = value; } }
    private Func<bool>? _xrayRunning;

    private MonitorState _state = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private int _cycle;

    public Checker(Config cfg) => Cfg = cfg;

    public MonitorState State { get { lock (_lock) return _state; } }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop() => _cts?.Cancel();

    public void RunNow() => _ = Task.Run(SafeRunAsync);

    private async Task SafeRunAsync()
    {
        try { await RunAllAsync(); }
        catch (Exception ex) { Logger.Error("Сбой в цикле проверок", ex); }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        await SafeRunAsync();
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(Cfg.CheckIntervalSec), ct); }
            catch (TaskCanceledException) { break; }
            if (!ct.IsCancellationRequested) await SafeRunAsync();
        }
    }

    private async Task RunAllAsync()
    {
        var cfg = Cfg;
        var vps = cfg.VpsHost;
        var results = new Dictionary<string, CheckResult>();
        _cycle++;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Logger.Info($"───── Цикл #{_cycle} начат ───── VPS={vps} VLESS-порт={cfg.VlessPort} " +
                    $"SOCKS5={cfg.LocalSocks5Port} xray-запущен={(_xrayRunning?.Invoke() ?? false)}");

        // ── Pings (parallel) ──
        var pingTasks = cfg.PingTargets
            .Select(kv => Connectivity.PingHostAsync(kv.Value, kv.Key))
            .ToList();

        // ── TCP probes (parallel) ──
        var tcpVless = Connectivity.TcpProbeAsync(vps, cfg.VlessPort, $"Порт VLESS ({cfg.VlessPort})");
        var tcpProbe = Connectivity.TcpProbeAsync(vps, cfg.ProbeServerPort, $"Порт probe ({cfg.ProbeServerPort})");

        await Task.WhenAll(pingTasks.Concat(new[] { tcpVless, tcpProbe }));

        foreach (var t in pingTasks) Add(results, t.Result);
        Add(results, tcpVless.Result);
        Add(results, tcpProbe.Result);

        // ── Local xray status — ALWAYS shown, independent of probe server ──
        var xrayLocal = await Tunnel.CheckXrayLocalAsync(
            _xrayRunning?.Invoke() ?? false, cfg.LocalSocks5Port);
        Add(results, xrayLocal);

        // ── External HTTP through tunnel (the real tunnel-health signal) ──
        var ext = await Tunnel.CheckExternalHttpAsync(cfg.ProxyUrl);
        Add(results, ext);

        // ── Probe server direct (optional infrastructure) ──
        var direct = await Tunnel.CheckHttpDirectAsync(vps, cfg.ProbeServerPort, cfg.ProbeSecret);
        Add(results, direct);

        // ── Tunnel checks (only if probe server reachable) ──
        if (direct.Ok)
        {
            var e2e = await Tunnel.CheckTunnelE2EAsync(vps, cfg.ProbeServerPort, cfg.ProbeSecret, cfg.ProxyUrl);
            Add(results, e2e);

            var ratio = Tunnel.CheckLatencyRatio(direct.Value, e2e.Ok ? e2e.Value : null, cfg.LatencyRatioThreshold);
            Add(results, ratio);

            // DPI freeze test — heavier, every 3rd cycle
            if (_cycle % 3 == 1)
            {
                var dpi = await Tunnel.CheckDpiFreezeAsync(vps, cfg.ProbeServerPort, cfg.ProbeSecret, cfg.ProxyUrl, cfg.DpiProbeSizeKb);
                Add(results, dpi);
            }

            var stats = await Tunnel.CheckXrayStatsAsync(vps, cfg.ProbeServerPort, cfg.ProbeSecret);
            Add(results, stats);
        }

        var (overall, diagnosis) = Classify(results, cfg);
        sw.Stop();

        // ── Per-cycle summary block (the part you hand off for analysis) ──
        Logger.Info($"Результаты цикла #{_cycle} (за {sw.ElapsedMilliseconds} мс):");
        foreach (var r in results.Values
                     .OrderBy(x => x.Category)
                     .ThenBy(x => x.Name))
        {
            var mark = r.Ok ? "OK  " : "FAIL";
            var val = r.Value.HasValue ? $" value={r.Value}" : "";
            Logger.Info($"    [{mark}] {r.Category,-7} | {r.Name} | {r.Message}{val} | {r.Comment}");
        }
        Logger.Info($"ИТОГ цикла #{_cycle}: {overall.ToString().ToUpper()} — {diagnosis.Replace("\n", " / ")}");

        var snapshot = new MonitorState
        {
            Checks = results,
            Overall = overall,
            Diagnosis = diagnosis,
            LastUpdate = DateTime.Now,
        };

        lock (_lock) _state = snapshot;
        StateChanged?.Invoke(snapshot);
    }

    private static void Add(Dictionary<string, CheckResult> d, CheckResult r) => d[r.Name] = r;

    private (OverallStatus, string) Classify(Dictionary<string, CheckResult> r, Config cfg)
    {
        CheckResult? Get(string n) => r.TryGetValue(n, out var v) ? v : null;
        bool OkOf(string n) => Get(n)?.Ok ?? false;

        var vpsPingName = cfg.PingTargets.FirstOrDefault(kv => kv.Value == cfg.VpsHost).Key;
        bool vpsUp = vpsPingName != null && OkOf(vpsPingName);

        // Real tunnel-health signals (do NOT depend on the optional probe server).
        var xrayLocal = Get("Локальный клиент xray");
        var ext = Get("Интернет через туннель");

        // Probe-server-dependent diagnostics — optional, never drive a "broken" verdict.
        var dpi = Get("DPI: тест на заморозку 16 КБ");
        var ratio = Get("DPI: соотношение задержек");

        // RU vs international reachability
        bool ruOk = true, intlOk = true;
        foreach (var kv in cfg.PingTargets)
        {
            var res = Get(kv.Key);
            if (res == null) continue;
            if (kv.Key.Contains("RU")) ruOk = ruOk && res.Ok;
            else if (kv.Value is "8.8.8.8" or "1.1.1.1") intlOk = intlOk && res.Ok;
        }

        var issues = new List<string>();

        // ── RED: no internet / upstream / VPS / port / xray down ──
        if (!intlOk && !ruOk)
            return (OverallStatus.Red, "Нет интернета — не пингуются ни российские, ни зарубежные адреса");
        if (!intlOk && ruOk)
            return (OverallStatus.Red,
                "Зарубежные адреса недоступны, российские работают — похоже упал аплинк провайдера");

        var vlessTcp = Get($"Порт VLESS ({cfg.VlessPort})");
        if (vlessTcp is { Ok: false } && !vpsUp)
            return (OverallStatus.Red, "VPS недоступен — ни порт VLESS, ни ping не отвечают");
        if (vlessTcp is { Ok: false })
            return (OverallStatus.Red,
                $"Порт VLESS {cfg.VlessPort} закрыт — провайдер блокирует подключение к серверу");

        if (xrayLocal is { Ok: false })
            return (OverallStatus.Red, xrayLocal.Comment);

        // ── YELLOW: tunnel routes nothing, or DPI throttling detected ──
        // The decisive tunnel signal is "internet through tunnel".
        if (ext is { Ok: false })
            issues.Add("Интернет через туннель не работает — VLESS поднят, но трафик не проходит "
                       + "(возможно DPI режет соединение)");

        if (dpi is { Ok: false })
            issues.Add(dpi.Comment);

        if (ratio is { Ok: false })
            issues.Add(ratio.Comment);

        if (issues.Count > 0)
            return (OverallStatus.Yellow, string.Join("\n", issues));

        return (OverallStatus.Green, "Всё в порядке. VLESS работает нормально, замедлений нет.");
    }
}
