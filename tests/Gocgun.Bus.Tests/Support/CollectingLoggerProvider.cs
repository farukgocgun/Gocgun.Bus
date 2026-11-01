// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Support;

using System.Collections.Concurrent;
using Gocgun.Bus.Tests.Messages;
using Microsoft.Extensions.Logging;

/// <summary>
/// Collects formatted log lines.
/// </summary>
public sealed class CollectingLoggerProvider : ILoggerProvider
{
    /// <summary>
    /// Gets the formatted lines.
    /// </summary>
    public ConcurrentQueue<string> Lines { get; } = new();

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        return new Collector(this.Lines);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class Collector : ILogger
    {
        private readonly ConcurrentQueue<string> lines;

        public Collector(ConcurrentQueue<string> lines)
        {
            this.lines = lines;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            this.lines.Enqueue(formatter(state, exception));
        }
    }
}
