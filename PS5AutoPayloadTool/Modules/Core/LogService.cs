using System.IO;
using System.Text;

namespace PS5AutoPayloadTool.Modules.Core;

public enum LogLevel { DEBUG, INFO, WARN, ERROR }

public record LogEntry(DateTime Time, LogLevel Level, string Module, string Message)
{
    public string TimeStr    => Time.ToString("HH:mm:ss");
    public string LevelStr   => Level.ToString();
    public string Formatted  => $"[{TimeStr}] {LevelStr,-5} {Module,-20} {Message}";
}

/// <summary>
/// Global structured logger.  Thread-safe.  Writes to an in-memory buffer and
/// a rotating log file.  Fire-and-forget: never throws.
/// </summary>
public static class LogService
{
    private const int    MaxEntries  = 3000;
    private const long   MaxFileSize = 2 * 1024 * 1024; // 2 MB

    private static readonly List<LogEntry> _entries = new();
    private static readonly object         _lock    = new();
    private static StreamWriter?           _writer;

    public static bool DebugMode { get; set; } = false;

    /// <summary>Fires on the calling thread whenever an entry is added.</summary>
    public static event Action<LogEntry>? EntryAdded;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public static void Init(string logPath, bool debugMode)
    {
        DebugMode = debugMode;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            RotateIfNeeded(logPath);
            _writer = new StreamWriter(logPath, append: true, Encoding.UTF8) { AutoFlush = true };
            Info("LogService", $"Session started  debug={debugMode}");
        }
        catch { /* logging must never crash the app */ }
    }

    public static void Close()
    {
        try { lock (_lock) { _writer?.Dispose(); _writer = null; } }
        catch { }
    }

    // ── Write helpers ─────────────────────────────────────────────────────────

    public static void Debug(string module, string message) => Write(LogLevel.DEBUG, module, message);
    public static void Info (string module, string message) => Write(LogLevel.INFO,  module, message);
    public static void Warn (string module, string message) => Write(LogLevel.WARN,  module, message);
    public static void Error(string module, string message) => Write(LogLevel.ERROR, module, message);

    public static void Write(LogLevel level, string module, string message)
    {
        if (level == LogLevel.DEBUG && !DebugMode) return;

        var entry = new LogEntry(DateTime.Now, level, module, message);

        lock (_lock)
        {
            if (_entries.Count >= MaxEntries) _entries.RemoveAt(0);
            _entries.Add(entry);
            try { _writer?.WriteLine(entry.Formatted); } catch { }
        }

        // Fire outside the lock so subscribers can safely call back into LogService
        try { EntryAdded?.Invoke(entry); } catch { }
    }

    // ── Query ─────────────────────────────────────────────────────────────────

    public static List<LogEntry> GetAll()
    {
        lock (_lock) return new List<LogEntry>(_entries);
    }

    public static void Clear()
    {
        lock (_lock) _entries.Clear();
    }

    public static string Export()
    {
        lock (_lock)
            return string.Join("\n", _entries.Select(e => e.Formatted));
    }

    // ── Rotation ──────────────────────────────────────────────────────────────

    private static void RotateIfNeeded(string logPath)
    {
        try
        {
            if (!File.Exists(logPath)) return;
            if (new FileInfo(logPath).Length < MaxFileSize) return;
            var backup = logPath + ".1";
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(logPath, backup);
        }
        catch { }
    }
}
