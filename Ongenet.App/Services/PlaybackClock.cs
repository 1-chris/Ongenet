using System;

namespace Ongenet.App.Services
{
    /// <summary>
    /// Default <see cref="IPlaybackClock"/>. Does NOT own a timer — it is pumped from the transport bar's
    /// render-frame loop (always visible) and from the timeline while the Arrangement tab is open.
    /// Self-throttles <see cref="Tick"/> to ~30Hz.
    /// </summary>
    public sealed class PlaybackClock : IPlaybackClock
    {
        private const long MinIntervalMs = 30; // ~33Hz cap on the fan-out
        private long _lastTickMs;

        public event Action? Tick;

        public void Pump()
        {
            var now = Environment.TickCount64;
            if (now - _lastTickMs < MinIntervalMs) return;
            _lastTickMs = now;
            Tick?.Invoke();
        }
    }
}
