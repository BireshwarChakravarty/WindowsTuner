namespace WindowsTuner.Core.Infrastructure;

public enum LogLevel { Info, Success, Warning, Error, Output }

public sealed record LogEntry(DateTime Time, LogLevel Level, string Message)
{
    public override string ToString() => $"{Time:HH:mm:ss} {Prefix} {Message}";
    private string Prefix => Level switch
    {
        LogLevel.Success => "[ OK ]",
        LogLevel.Warning => "[WARN]",
        LogLevel.Error => "[FAIL]",
        LogLevel.Output => "     ",
        _ => "[INFO]",
    };
}

/// <summary>Application-wide log: raises an event for the UI and appends to a daily file.</summary>
public static class Log
{
    private static readonly object Gate = new();
    public static event Action<LogEntry>? Written;
    public static bool FileLoggingEnabled { get; set; } = true;

    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Success(string message) => Write(LogLevel.Success, message);
    public static void Warn(string message) => Write(LogLevel.Warning, message);
    public static void Error(string message) => Write(LogLevel.Error, message);
    public static void Output(string message) => Write(LogLevel.Output, message);

    public static void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        if (FileLoggingEnabled)
        {
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(Path.Combine(AppPaths.Logs, $"{DateTime.Now:yyyy-MM-dd}.log"),
                        entry + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never break an operation.
            }
        }
        Written?.Invoke(entry);
    }

    public static string TodayLogFile => Path.Combine(AppPaths.Logs, $"{DateTime.Now:yyyy-MM-dd}.log");
}
