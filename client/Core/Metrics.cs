using System.Text;
using System.Text.Json;
using VlessMonitor.Checks;

namespace VlessMonitor.Core;

public enum MetricKind { Numeric, State }

public readonly record struct MetricSample(DateTime Time, double Value, bool Ok);

/// <summary>One time series (per check). Numeric → line graph; State → up/down band.</summary>
public class MetricSeries
{
    public string Name { get; }
    public CheckCategory Category { get; }
    public MetricKind Kind { get; private set; }
    public string Unit { get; private set; } = "";

    private readonly List<MetricSample> _samples = new();
    private const int MaxSamples = 4000;
    private readonly object _lock = new();
    private bool _kindSet;

    public MetricSeries(string name, CheckCategory cat) { Name = name; Category = cat; }

    public void Add(MetricSample s, MetricKind kind, string unit)
    {
        lock (_lock)
        {
            // Fix the kind on the first sample so a check that occasionally
            // returns no value (e.g. a failed DPI test) doesn't flip the series
            // between line and state, which corrupted the graph and stats.
            if (!_kindSet) { Kind = kind; _kindSet = true; }
            if (unit.Length > 0) Unit = unit;
            _samples.Add(s);
            if (_samples.Count > MaxSamples) _samples.RemoveRange(0, _samples.Count - MaxSamples);
        }
    }

    /// <summary>Samples within the last <paramref name="window"/>.</summary>
    public List<MetricSample> Window(TimeSpan window)
    {
        var cutoff = DateTime.Now - window;
        lock (_lock)
            return _samples.Where(s => s.Time >= cutoff).ToList();
    }

    public MetricSample? Last { get { lock (_lock) return _samples.Count > 0 ? _samples[^1] : null; } }

    public (double min, double avg, double max, double uptime) Stats(TimeSpan window)
    {
        var w = Window(window);
        if (w.Count == 0) return (0, 0, 0, 0);
        var vals = w.Where(s => s.Ok).Select(s => s.Value).ToList();
        double uptime = 100.0 * w.Count(s => s.Ok) / w.Count;
        if (vals.Count == 0) return (0, 0, 0, uptime);
        return (vals.Min(), vals.Average(), vals.Max(), uptime);
    }
}

/// <summary>
/// In-memory metric store fed by the checker each cycle. Optionally persists
/// to logs/history.jsonl so graphs survive a restart.
/// </summary>
public class MetricsStore
{
    public static MetricsStore Instance { get; } = new();

    private readonly Dictionary<string, MetricSeries> _series = new();
    private readonly object _lock = new();
    private bool _persist;
    private string HistoryFile => Path.Combine(Logger.LogDir, "history.jsonl");

    public IReadOnlyList<MetricSeries> ByCategory(CheckCategory cat)
    {
        lock (_lock)
            return _series.Values.Where(s => s.Category == cat)
                .OrderBy(s => s.Name).ToList();
    }

    public IReadOnlyList<CheckCategory> Categories()
    {
        lock (_lock)
            return _series.Values.Select(s => s.Category).Distinct()
                .OrderBy(c => (int)c).ToList();
    }

    public void Configure(bool persist)
    {
        _persist = persist;
        if (persist) TryLoad();
    }

    /// <summary>Record one cycle's results into the series.</summary>
    public void Record(IEnumerable<CheckResult> results)
    {
        var now = DateTime.Now;
        var lines = _persist ? new StringBuilder() : null;

        lock (_lock)
        {
            foreach (var r in results)
            {
                if (!_series.TryGetValue(r.Name, out var s))
                    _series[r.Name] = s = new MetricSeries(r.Name, r.Category);

                bool numeric = r.Value.HasValue;
                double v = r.Value ?? (r.Ok ? 1 : 0);
                var kind = numeric ? MetricKind.Numeric : MetricKind.State;
                string unit = GuessUnit(r);
                s.Add(new MetricSample(now, v, r.Ok), kind, unit);

                lines?.AppendLine(JsonSerializer.Serialize(new HistRow(
                    now.ToString("o"), r.Name, (int)r.Category, v, r.Ok, numeric)));
            }
        }

        if (lines is { Length: > 0 })
            try { File.AppendAllText(HistoryFile, lines.ToString()); } catch { }
    }

    private static string GuessUnit(CheckResult r)
    {
        if (!r.Value.HasValue) return "";
        if (r.Message.Contains("КБ/с")) return "КБ/с";
        if (r.Message.Contains("×")) return "×";
        if (r.Category == CheckCategory.Ping || r.Name.Contains("Туннель") || r.Name.Contains("Интернет"))
            return "мс";
        return "";
    }

    private record HistRow(string t, string name, int cat, double v, bool ok, bool num);

    private void TryLoad()
    {
        try
        {
            if (!File.Exists(HistoryFile)) return;
            var cutoff = DateTime.Now - TimeSpan.FromHours(2);
            var kept = new List<string>();
            foreach (var line in File.ReadLines(HistoryFile))
            {
                HistRow? row;
                try { row = JsonSerializer.Deserialize<HistRow>(line); } catch { continue; }
                if (row == null) continue;
                if (!DateTime.TryParse(row.t, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var ts)) continue;
                if (ts < cutoff) continue;

                kept.Add(line);
                var cat = (CheckCategory)row.cat;
                if (!_series.TryGetValue(row.name, out var s))
                    _series[row.name] = s = new MetricSeries(row.name, cat);
                s.Add(new MetricSample(ts, row.v, row.ok),
                    row.num ? MetricKind.Numeric : MetricKind.State, "");
            }
            // Rewrite file trimmed to the kept (last 2h) window to keep it small.
            try { File.WriteAllLines(HistoryFile, kept); } catch { }
        }
        catch (Exception ex) { Logger.Error("Не удалось загрузить историю", ex); }
    }

    public void Clear()
    {
        lock (_lock) _series.Clear();
        try { if (File.Exists(HistoryFile)) File.Delete(HistoryFile); } catch { }
    }
}
