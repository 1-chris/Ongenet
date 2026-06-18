using System;

namespace Ongenet.Desktop.Services
{
    /// <summary>
    /// A steady ~30fps UI heartbeat. View models that show values the audio engine mutates directly
    /// (parameters driven by automation, track volume/pan) subscribe to <see cref="Tick"/> and re-read
    /// their model during playback, so the on-screen controls move in real time with the automation.
    /// </summary>
    public interface IPlaybackClock
    {
        event Action? Tick;
    }
}
