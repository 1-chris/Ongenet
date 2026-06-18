using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Ongenet.Core.Models.Logging;

namespace Ongenet.Core.Services.Implementation;

/// <summary>
/// Logger provider that creates loggers writing to an ObservableCollection.
/// Designed for UI binding in Avalonia/WPF applications.
/// </summary>
public class ObservableCollectionLoggerProvider : ILoggerProvider
{
    private readonly ObservableCollection<LogEntry> _logEntries;
    private readonly ConcurrentDictionary<string, ObservableCollectionLogger> _loggers;
    private readonly Func<LogLevel, bool>? _filter;
    private readonly int _maxLogEntries;
    private readonly object _lock = new object();
    private Action<Action>? _dispatcher;
    private bool _disposed;

    /// <summary>
    /// Gets the collection of log entries that can be bound to UI.
    /// </summary>
    public ObservableCollection<LogEntry> LogEntries => _logEntries;

    /// <summary>
    /// Sets a dispatcher function to ensure collection modifications happen on the correct thread.
    /// </summary>
    /// <param name="dispatcher">A delegate that executes the given action on the UI thread.</param>
    public void SetDispatcher(Action<Action> dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ObservableCollectionLoggerProvider"/> class.
    /// </summary>
    /// <param name="filter">Optional filter to determine which log levels to capture (default: Info and above).</param>
    /// <param name="maxLogEntries">Maximum number of log entries to retain (default 1000).</param>
    public ObservableCollectionLoggerProvider(Func<LogLevel, bool>? filter = null, int maxLogEntries = 1000)
    {
        _logEntries = new ObservableCollection<LogEntry>();
        _loggers = new ConcurrentDictionary<string, ObservableCollectionLogger>();
        _filter = filter ?? (level => level >= LogLevel.Debug); // Default: Debug and above
        _maxLogEntries = maxLogEntries;
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ObservableCollectionLoggerProvider));
        }

        return _loggers.GetOrAdd(categoryName, name =>
            new ObservableCollectionLogger(name, _logEntries, _filter, _maxLogEntries, _lock, () => _dispatcher));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _loggers.Clear();
            _disposed = true;
        }
    }

    /// <summary>
    /// Clears all log entries from the collection.
    /// </summary>
    public void ClearLogs()
    {
        _logEntries.Clear();
    }
}
