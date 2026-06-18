using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace Ongenet.Desktop.Services.Notifications
{
    /// <summary>
    /// Implementation of notification service for managing application logs.
    /// Thread-safe and UI-thread aware for Avalonia applications.
    /// </summary>
    public class NotificationService : INotificationService
    {
        private readonly ObservableCollection<string> _logs = new();
        private const int MaxLogEntries = 1000;

        /// <inheritdoc/>
        public ObservableCollection<string> Logs => _logs;

        /// <inheritdoc/>
        public string LogText => string.Join("\n", _logs);

        /// <inheritdoc/>
        public void Log(string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _logs.Add($"{DateTime.Now:HH:mm:ss} {message}");

                // Maintain max log size
                if (_logs.Count > MaxLogEntries)
                {
                    _logs.RemoveAt(0);
                }
            });
        }

        /// <inheritdoc/>
        public void ClearLogs()
        {
            Dispatcher.UIThread.Post(() =>
            {
                _logs.Clear();
            });
        }
    }
}
