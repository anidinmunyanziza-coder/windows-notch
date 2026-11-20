using System;
using Atoll.Core.Contracts;

namespace Atoll.Core.Infrastructure;

/// <summary>
/// Minimal debug logger for the foundation. Replaced by Serilog/NLog in a
/// later phase (the original had a <c>logLevel</c> setting and log export).
/// </summary>
public sealed class ConsoleLogger : ILogger
{
    private readonly LogLevel _minimumLevel;
    public ConsoleLogger(LogLevel minimumLevel = LogLevel.Info) => _minimumLevel = minimumLevel;

    private static string LevelPrefix(LogLevel l) => l switch
    {
        LogLevel.Error => "ERROR",
        LogLevel.Warn => "WARN ",
        LogLevel.Info => "INFO ",
        LogLevel.Debug => "DEBUG",
        _ => "     "
    };

    private bool Enabled(LogLevel l) => l >= _minimumLevel && l != LogLevel.None;

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (!Enabled(level)) return;
        var line = $"[{DateTimeOffset.UtcNow:HH:mm:ss.fff}] {LevelPrefix(level)} {message}";
        if (exception is not null) line += $" | {exception}";
        Console.WriteLine(line);
    }

    public void LogError(string message, Exception? exception = null) => Log(LogLevel.Error, message, exception);
    public void LogWarn(string message, Exception? exception = null) => Log(LogLevel.Warn, message, exception);
    public void LogInfo(string message, Exception? exception = null) => Log(LogLevel.Info, message, exception);
    public void LogDebug(string message, Exception? exception = null) => Log(LogLevel.Debug, message, exception);

    public void Dispose() { }
}
