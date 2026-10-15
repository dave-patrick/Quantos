using System.Collections.Concurrent;
using System.Text.Json;

namespace Quantos.Backend;

/// <summary>
/// Minimal structured (JSON lines) logger to client-data/logs/backend-YYYY-MM-DD.log.
/// Logs never contain note content or the access token.
/// </summary>
public sealed class FileLoggerProvider(string directory) : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, c => new FileLogger(this, c));

    internal void Write(string category, LogLevel level, string message, Exception? ex)
    {
        var line = JsonSerializer.Serialize(new
        {
            time = DateTimeOffset.Now.ToString("O"),
            level = level.ToString(),
            category,
            message,
            error = ex?.ToString(),
        });
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, $"backend-{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine);
            }
            catch (IOException) { /* Logging must never take the backend down. */ }
        }
    }

    public void Dispose() { }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information &&
            !(category.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) && logLevel < LogLevel.Warning);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}
