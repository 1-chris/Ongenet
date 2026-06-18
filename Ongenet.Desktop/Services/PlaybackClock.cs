using System;
using Avalonia.Threading;

namespace Ongenet.Desktop.Services
{
    /// <summary>
    /// Default <see cref="IPlaybackClock"/>: a single always-running <see cref="DispatcherTimer"/> on the
    /// UI thread. Subscribers gate their own work on the transport state, so an idle tick is cheap.
    /// </summary>
    public sealed class PlaybackClock : IPlaybackClock
    {
        public event Action? Tick;

        public PlaybackClock()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += (_, _) => Tick?.Invoke();
            timer.Start();
        }
    }
}
