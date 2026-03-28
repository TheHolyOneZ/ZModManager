using System;
using ZModManager.Models;

namespace ZModManager.Services;

public class LogService
{
    public event Action<LogEntry>? EntryAdded;

    public void Log(LogLevel level, string message)
    {
        var entry = new LogEntry { Level = level, Message = message };
        EntryAdded?.Invoke(entry);
    }

    public void Info(string message)    => Log(LogLevel.Info,    message);
    public void Success(string message) => Log(LogLevel.Success, message);
    public void Warn(string message)    => Log(LogLevel.Warning, message);
    public void Error(string message)   => Log(LogLevel.Error,   message);
}
