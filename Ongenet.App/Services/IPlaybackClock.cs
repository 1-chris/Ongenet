using System;

namespace Ongenet.App.Services
{
    /// <summary>
    /// A steady ~30fps UI heartbeat. View models that show values the audio engine mutates directly
    /// (parameters driven by automation, track volume/pan) subscribe to <see cref="Tick"/> and re-read
    /// their model during playback, so the on-screen controls move in real time with the automation.
    ///
    /// It is pumped from the timeline's render-frame loop (<see cref="Pump"/>) rather than owning its
    /// own timer: a separate ~30Hz DispatcherTimer competing with the render-frame callback was causing
    /// the compositor to miss vsync and drop to 30fps during playback. Pump() self-throttles to ~30Hz.
    /// </summary>
    public interface IPlaybackClock
    {
        event Action? Tick;

        /// <summary>Advance the heartbeat; raises <see cref="Tick"/> at most ~30x/sec. Called once per
        /// render frame by the timeline so there's a single per-frame work source on the UI thread.</summary>
        void Pump();
    }
}
