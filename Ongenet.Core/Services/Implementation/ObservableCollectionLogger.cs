using System;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Ongenet.Core.Models.Logging;

namespace Ongenet.Core.Services.Implementation;

/// <summary>
/// Logger implementation that writes log entries to an ObservableCollection for UI binding.
/// Thread-safe and supports all Microsoft.Extensions.Logging log levels.
/// </summary>
internal class ObservableCollectionLogger : ILogger
{
    private readonly string _categoryName;
    private readonly ObservableCollection<LogEntry> _logEntries;
    private readonly Func<LogLevel, bool> _filter;
    private readonly int _maxLogEntries;
    private readonly object _lock;
    private readonly Func<Action<Action>?> _dispatcherResolver;

    [ThreadStatic]
    private static bool _isAdding;

    /// <summary>
    /// Initializes a new instance of the <see cref="ObservableCollectionLogger"/> class.
    /// </summary>
    /// <param name="categoryName">The category name for this logger (typically the class name).</param>
    /// <param name="logEntries">The shared ObservableCollection to write logs to.</param>
    /// <param name="filter">Optional filter function to determine which log levels to capture.</param>
    /// <param name="maxLogEntries">Maximum number of log entries to keep.</param>
    /// <param name="commonLock">Shared lock object to synchronize collection access across all loggers.</param>
    /// <param name="dispatcherResolver">A delegate that resolves the current UI thread dispatcher.</param>
    public ObservableCollectionLogger(
        string categoryName,
        ObservableCollection<LogEntry> logEntries,
        Func<LogLevel, bool>? filter,
        int maxLogEntries,
        object commonLock,
        Func<Action<Action>?> dispatcherResolver)
    {
        _categoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
        _logEntries = logEntries ?? throw new ArgumentNullException(nameof(logEntries));
        _filter = filter ?? (_ => true);
        _maxLogEntries = maxLogEntries;
        _lock = commonLock ?? throw new ArgumentNullException(nameof(commonLock));
        _dispatcherResolver = dispatcherResolver ?? throw new ArgumentNullException(nameof(dispatcherResolver));
    }

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        // Scope not implemented for now, could be added later
        return null;
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None && _filter(logLevel);
    }

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        if (formatter == null)
        {
            throw new ArgumentNullException(nameof(formatter));
        }

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception == null)
        {
            return;
        }

        var logEntry = new LogEntry(
            DateTime.Now,
            logLevel,
            _categoryName,
            message,
            exception
        );

        // Add to collection on UI thread if needed, or synchronously with a lock
        var dispatcher = _dispatcherResolver();
        if (dispatcher != null)
        {
            dispatcher(() => AddToCollection(logEntry));
        }
        else
        {
            AddToCollection(logEntry);
        }
    }

    private void AddToCollection(LogEntry logEntry)
    {
        // Simple thread-static reentrancy check to prevent infinite loops/crashes
        if (_isAdding)
        {
            return;
        }

        _isAdding = true;
        try
        {
            lock (_lock)
            {
                _logEntries.Add(logEntry);

                // Maintain max log size
                while (_logEntries.Count > _maxLogEntries)
                {
                    _logEntries.RemoveAt(0);
                }
            }
        }
        finally
        {
            _isAdding = false;
        }
    }
}
