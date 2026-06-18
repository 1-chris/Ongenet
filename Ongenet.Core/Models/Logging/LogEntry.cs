using System;
using Microsoft.Extensions.Logging;

namespace Ongenet.Core.Models.Logging;

/// <summary>
/// Represents a single log entry with timestamp, level, category, and message.
/// </summary>
public class LogEntry
{
    /// <summary>
    /// Gets the timestamp when the log entry was created.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Gets the log level (Trace, Debug, Information, Warning, Error, Critical).
    /// </summary>
    public LogLevel Level { get; }

    /// <summary>
    /// Gets the category/source of the log (typically the full class name).
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Gets the log message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the exception associated with this log entry, if any.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LogEntry"/> class.
    /// </summary>
    public LogEntry(DateTime timestamp, LogLevel level, string category, string message, Exception? exception = null)
    {
        Timestamp = timestamp;
        Level = level;
        Category = category;
        Message = message;
        Exception = exception;
    }

    /// <summary>
    /// Gets a formatted string representation of this log entry.
    /// </summary>
    public string FormattedMessage => $"{Timestamp:HH:mm:ss} [{Level}] {GetShortCategory()}: {Message}";

    /// <summary>
    /// Gets a shortened category name (last part of namespace).
    /// </summary>
    private string GetShortCategory()
    {
        var parts = Category.Split('.');
        return parts.Length > 0 ? parts[^1] : Category;
    }

    /// <summary>
    /// Returns the formatted message representation.
    /// </summary>
    public override string ToString() => FormattedMessage;
}
