using System;

namespace ZModManager.Models;

public class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public LogLevel   Level     { get; init; }
    public string     Message   { get; init; } = string.Empty;

    public string FormattedTime => Timestamp.ToString("HH:mm:ss");
}
