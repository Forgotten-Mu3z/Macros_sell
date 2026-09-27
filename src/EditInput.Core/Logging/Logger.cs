using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace EditInput.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

public interface ILogger
{
    bool DebugEnabled { get; }
    void Log(LogLevel level, string category, string message);
}

public static class LoggerExtensions
{
    public static void Debug(this ILogger log, string category, string message)
    {
        if (log.DebugEnabled) log.Log(LogLevel.Debug, category, message);
    }

    public static void Info(this ILogger log, string category, string message) => log.Log(LogLevel.Info, category, message);
    public static void Warn(this ILogger log, string category, string message) => log.Log(LogLevel.Warning, category, message);

    public static void Error(this ILogger log, string category, string message, Exception? ex = null) =>
        log.Log(LogLevel.Error, category, ex is null ? message : $"{message}: {ex}");
}

public sealed record LogEntry(long Sequence, LogLevel Level, string Text);

/// <summary>
/// Thread-safe logger. Callers never block on disk I/O: lines go to an in-memory ring (for the debug panel)
/// and a queue drained by a background writer. Only app events and configured control inputs are ever
/// logged; the input hooks never pass unrelated keystrokes to the logger.
/// </summary>
public sealed class Logger : ILogger, IDisposable
{
    private const int RingCapacity = 400;

    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private readonly LinkedList<LogEntry> _ring = new();
    private readonly object _ringGate = new();
    private readonly BlockingCollection<string> _fileQueue = new(new ConcurrentQueue<string>(), 10_000);
    private readonly Thread? _writer;
    private readonly string? _directory;
    private long _sequence;
    private volatile bool _debugEnabled;

    public Logger(string? logDirectory)
    {
        _directory = logDirectory;
        if (_directory is null) return;
        try { System.IO.Directory.CreateDirectory(_directory); }
        catch { _directory = null; return; }

        _writer = new Thread(WriterLoop) { IsBackground = true, Name = "LogWriter", Priority = ThreadPriority.BelowNormal };
        _writer.Start();
    }

    public bool DebugEnabled
    {
        get => _debugEnabled;
        set => _debugEnabled = value;
    }

    public string? Directory => _directory;

    /// <summary>Raised on the logging thread; subscribers must marshal to their own thread.</summary>
    public event Action<LogEntry>? EntryAdded;

    public void Log(LogLevel level, string category, string message)
    {
        if (level == LogLevel.Debug && !_debugEnabled) return;

        var text = string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.Now:HH:mm:ss.fff} [+{_uptime.Elapsed.TotalMilliseconds,10:F3} ms] {LevelTag(level)} {category}: {message}");
        var entry = new LogEntry(Interlocked.Increment(ref _sequence), level, text);

        lock (_ringGate)
        {
            _ring.AddLast(entry);
            if (_ring.Count > RingCapacity) _ring.RemoveFirst();
        }

        // Debug lines only hit disk when debug logging is on; warnings/errors are always kept for troubleshooting.
        if (_writer is not null && (level != LogLevel.Info || _debugEnabled))
        {
            try { _fileQueue.TryAdd(text); }
            catch (InvalidOperationException) { /* shutting down (includes ObjectDisposedException) */ }
        }

        try { EntryAdded?.Invoke(entry); }
        catch { /* a faulty UI subscriber must never break logging */ }
    }

    public IReadOnlyList<LogEntry> GetRecent()
    {
        lock (_ringGate) return _ring.ToList();
    }

    public void ClearRecent()
    {
        lock (_ringGate) _ring.Clear();
    }

    private static string LevelTag(LogLevel l) => l switch
    {
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warning => "WRN",
        _ => "ERR",
    };

    private void WriterLoop()
    {
        var sb = new StringBuilder();
        try
        {
            foreach (var line in _fileQueue.GetConsumingEnumerable())
            {
                sb.Clear().AppendLine(line);
                while (sb.Length < 64_000 && _fileQueue.TryTake(out var more)) sb.AppendLine(more);
                try
                {
                    var path = Path.Combine(_directory!, $"edit-input-{DateTime.Now:yyyyMMdd}.log");
                    File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                }
                catch
                {
                    // Disk problems must not take the input engine down; drop the batch.
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        _fileQueue.CompleteAdding();
        _writer?.Join(TimeSpan.FromSeconds(2));
        _fileQueue.Dispose();
    }
}

/// <summary>Logger that discards everything (tests, design time).</summary>
public sealed class NullLogger : ILogger
{
    public static readonly NullLogger Instance = new();
    public bool DebugEnabled => false;
    public void Log(LogLevel level, string category, string message) { }
}
