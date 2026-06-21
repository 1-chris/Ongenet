using System;

namespace Ongenet.Desktop.Services
{
    /// <summary>
    /// Default <see cref="IPlaybackClock"/>. Does NOT own a timer — it is pumped once per render frame
    /// from the timeline's RequestAnimationFrame loop and self-throttles <see cref="Tick"/> to ~30Hz.
    /// A separate DispatcherTimer here used to compete with that render-frame callback and make the
    /// compositor miss vsync (dropping playback to 30fps); routing through the single render-frame loop
    /// keeps frame pacing clean while still refreshing meters/inspector ~30x/sec.
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
