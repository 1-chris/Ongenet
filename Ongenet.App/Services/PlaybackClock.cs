using System;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.Services
{
    /// <summary>
    /// Default <see cref="IPlaybackClock"/>. Does NOT own a timer — it is pumped from the transport bar's
    /// render-frame loop (always visible) and from the timeline while the Arrangement tab is open.
    /// Self-throttles <see cref="Tick"/> (~30 Hz desktop; slower / paused while playing in the browser
    /// demo — see <see cref="UiPerfProfile"/>).
    /// </summary>
    public sealed class PlaybackClock : IPlaybackClock
    {
        private readonly ITransportService _transport;
        private readonly long _minIntervalMs = UiPerfProfile.PlaybackClockMinIntervalMs;
        private long _lastTickMs;

        public PlaybackClock(ITransportService transport) => _transport = transport;

        public event Action? Tick;

        public void Pump()
        {
            // Browser: freeze meter/parameter fan-out during playback so Avalonia doesn't steal the
            // main thread from ScriptProcessor. Playhead overlays update via TimelineView directly.
            if (UiPerfProfile.SuppressLiveUiWhilePlaying && _transport.State == TransportState.Playing)
                return;

            var now = Environment.TickCount64;
            if (now - _lastTickMs < _minIntervalMs) return;
            _lastTickMs = now;
            Tick?.Invoke();
        }
    }
}
