using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CodeBoss.Jobs.Tests;

public sealed class CapturingLoggerFactory : ILoggerFactory
{
    public ConcurrentQueue<string> Entries { get; } = new();
    public void AddProvider(ILoggerProvider provider) { }
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);
    public void Dispose() { }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> sink) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
            Func<TState, Exception?, string> formatter)
            => sink.Enqueue($"[{level}] {category}: {formatter(state, ex)}{(ex is null ? "" : " || " + ex)}");
    }
}

public sealed class CapturingLogger<T>(CapturingLoggerFactory factory) : ILogger<T>
{
    private readonly ILogger _inner = factory.CreateLogger(typeof(T).FullName!);
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state)!;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
        Func<TState, Exception?, string> formatter) => _inner.Log(level, id, state, ex, formatter);
}
