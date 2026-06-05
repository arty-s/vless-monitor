namespace VlessMonitor.Checks;

public enum CheckCategory { Ping, Port, Tunnel, Dpi, Stats, General }

/// <summary>
/// Result of a single check. <see cref="Comment"/> is the human-friendly
/// explanation shown as the main text; <see cref="Message"/> is the raw value.
/// </summary>
public class CheckResult
{
    public string Name { get; init; } = "";
    public bool Ok { get; init; }
    public double? Value { get; init; }      // latency ms / throughput
    public string Message { get; init; } = ""; // raw, e.g. "45 мс"
    public string Comment { get; init; } = ""; // plain-language explanation
    public CheckCategory Category { get; init; } = CheckCategory.General;
    public DateTime Timestamp { get; init; } = DateTime.Now;
}
