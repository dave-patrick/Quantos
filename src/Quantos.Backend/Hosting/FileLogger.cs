using System.Collections.Concurrent;
using System.Globalization;

namespace Quantos.Backend.Hosting;

/// <summary>Daily log files in client-data/logs.</summary>
public sealed class FileLoggerProvider(string directory) : ILoggerProvider
{
    private readonly BlockingCollection<string> _queue = new(4096);
    private Thread? _writer;

    public ILogger CreateLogger(string categoryName)
    {
        EnsureWriter();
        return new FileLogger(categoryName, _queue);
    }

    private void EnsureWriter()
    {
        if (_writer is not null) return;
        lock (_queue)
        {
            if (_writer is not null) return;
            Directory.CreateDirectory(directory);
            _writer = new Thread(() =>
            {
                foreach (var line in _queue.GetConsumingEnumerable())
                {
                    try { File.AppendAllText(Path.Combine(directory, $"backend-{DateTime.Now:yyyyMMdd}.log"), line); }
                    catch (IOException) { }
                }
            }) { IsBackground = true, Name = "quantos-log" };
            _writer.Start();
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _writer?.Join(TimeSpan.FromSeconds(2));
    }

    private sealed class FileLogger(string category, BlockingCollection<string> queue) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || queue.IsAddingCompleted) return;
            var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} [{logLevel}] {category}: {formatter(state, exception)}{(exception is null ? "" : Environment.NewLine + exception)}{Environment.NewLine}";
            queue.TryAdd(line);
        }
    }
}
