using System.Collections.ObjectModel;

namespace Ongenet.Desktop.Services.Notifications
{
    /// <summary>
    /// Service for managing application logs and notifications.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// Gets the collection of log messages.
        /// </summary>
        ObservableCollection<string> Logs { get; }

        /// <summary>
        /// Gets all logs as a single text string (for legacy compatibility).
        /// </summary>
        string LogText { get; }

        /// <summary>
        /// Logs a message with timestamp.
        /// </summary>
        /// <param name="message">The message to log.</param>
        void Log(string message);

        /// <summary>
        /// Clears all log messages.
        /// </summary>
        void ClearLogs();
    }
}
