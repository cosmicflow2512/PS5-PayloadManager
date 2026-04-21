namespace PS5AutoPayloadTool.Core;

public enum LogLevel { Info, Success, Warn, Error }

public record LogEvent(DateTime When, string Message, LogLevel Level);

public static class LogBus
{
    public static event Action<LogEvent>? OnLog;
    public static event Action<string, string>? OnStateChange; // state, profile

    public static void Log(string message, LogLevel level = LogLevel.Info)
        => OnLog?.Invoke(new LogEvent(DateTime.Now, message, level));

    public static void StateChange(string state, string profile)
        => OnStateChange?.Invoke(state, profile);
}
