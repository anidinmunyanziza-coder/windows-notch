namespace Atoll.Core.Contracts;

public enum LogLevel { None, Error, Warn, Info, Debug }

public interface ILogger : IDisposable
{
    void Log(LogLevel level, string message, Exception? exception = null);
    void LogError(string message, Exception? exception = null);
    void LogWarn(string message, Exception? exception = null);
    void LogInfo(string message, Exception? exception = null);
    void LogDebug(string message, Exception? exception = null);
}
