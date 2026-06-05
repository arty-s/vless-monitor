using System.Collections.Concurrent;
using System.Text;

namespace VlessMonitor.Core;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>
/// Thread-safe file logger. Writes one file per day to <c>logs/</c> next to the exe.
/// Designed so logs can be handed off for offline analysis of detection accuracy.
/// </summary>
public static class Logger
{
    private static readonly object _lock = new();
    private static readonly BlockingCollection<string> _queue = new();
    private static string _logDir = "";
    private static string _currentFile = "";
    private static DateTime _currentDate = DateTime.MinValue;
    private static Thread? _writer;
    private static volatile bool _started;

    public static LogLevel MinLevel { get; set; } = LogLevel.Debug;

    public static string LogDir
    {
        get
        {
            if (_logDir.Length == 0)
                _logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            return _logDir;
        }
    }

    public static void Start()
    {
        if (_started) return;
        _started = true;
        Directory.CreateDirectory(LogDir);
        CleanupOldLogs(keepDays: 14);

        _writer = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "LoggerWriter",
        };
        _writer.Start();

        Info("==================== VLESS Monitor запущен ====================");
        Info($"Версия: {typeof(Logger).Assembly.GetName().Version}");
        Info($"Папка приложения: {AppContext.BaseDirectory}");
        Info($"OS: {Environment.OSVersion}");
    }

    public static void Stop()
    {
        if (!_started) return;
        Info("==================== Остановка ====================");
        _queue.CompleteAdding();
        try { _writer?.Join(2000); } catch { }
        _started = false;
    }

    public static void Debug(string msg) => Write(LogLevel.Debug, msg);
    public static void Info(string msg)  => Write(LogLevel.Info, msg);
    public static void Warn(string msg)  => Write(LogLevel.Warn, msg);
    public static void Error(string msg) => Write(LogLevel.Error, msg);

    public static void Error(string msg, Exception ex) =>
        Write(LogLevel.Error, $"{msg} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(LogLevel level, string msg)
    {
        if (level < MinLevel) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpper(),-5}] {msg}";
        if (_started && !_queue.IsAddingCompleted)
        {
            try { _queue.Add(line); } catch { }
        }
        System.Diagnostics.Debug.WriteLine(line);
    }

    private static void WriterLoop()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try
            {
                RollFileIfNeeded();
                File.AppendAllText(_currentFile, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { /* never crash on logging */ }
        }
    }

    private static void RollFileIfNeeded()
    {
        var today = DateTime.Now.Date;
        if (today != _currentDate || _currentFile.Length == 0)
        {
            _currentDate = today;
            _currentFile = Path.Combine(LogDir, $"vless-monitor-{today:yyyy-MM-dd}.log");
        }
    }

    private static void CleanupOldLogs(int keepDays)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-keepDays);
            foreach (var f in Directory.GetFiles(LogDir, "vless-monitor-*.log"))
            {
                if (File.GetLastWriteTime(f) < cutoff)
                    try { File.Delete(f); } catch { }
            }
        }
        catch { }
    }
}
