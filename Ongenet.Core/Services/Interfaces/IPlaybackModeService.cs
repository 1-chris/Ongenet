using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Scheduling;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>Arrangement vs session playback and live clip launching.</summary>
public enum PlaybackMode
{
    Arrangement,
    Session,
    Hybrid
}

/// <summary>Runtime state for one launched session clip.</summary>
public sealed class SessionClipLaunchState
{
    public required SessionClip Clip { get; init; }
    public required double LaunchBeat { get; init; }
    public bool Looping => Clip.LaunchMode == SessionLaunchMode.Repeat;
}

/// <summary>
/// Controls session-view clip launching and tracks which clips are playing or queued.
/// </summary>
public interface IPlaybackModeService
{
    /// <summary>Current playback mode (arrangement, session-only, or hybrid).</summary>
    PlaybackMode Mode { get; set; }

    /// <summary>Beat grid for clip launches while playing (0 = immediate / off).</summary>
    double LaunchQuantizeBeats { get; set; }

    /// <summary>Session vs arrangement blend in hybrid mode (0 = arrangement, 1 = session).</summary>
    double SessionCrossfader { get; set; }

    /// <summary>IDs of session clips currently playing.</summary>
    IReadOnlyCollection<Guid> ActiveSessionClipIds { get; }

    /// <summary>Launch metadata keyed by session-clip id.</summary>
    IReadOnlyDictionary<Guid, SessionClipLaunchState> ActiveLaunches { get; }

    event Action? ModeChanged;
    event Action? ActiveClipsChanged;

    /// <summary>
    /// Evaluates follow actions for clips that ended between <paramref name="prevBeat"/> and
    /// <paramref name="curBeat"/>.
    /// </summary>
    void TickFollowActions(double prevBeat, double curBeat);

    /// <summary>Starts (or toggles/re-triggers) a session clip according to its launch mode.</summary>
    void LaunchClip(Guid sessionClipId);

    /// <summary>Stops a playing session clip.</summary>
    void StopClip(Guid sessionClipId);

    /// <summary>Stops every active clip on <paramref name="trackId"/>.</summary>
    void StopTrack(Guid trackId);

    /// <summary>Launches every clip in the given scene column (one per track).</summary>
    void LaunchScene(int sceneIndex);

    /// <summary>Stops all playing session clips.</summary>
    void StopAll();

    /// <summary>Queue a clip for launch at the next quantize boundary.</summary>
    void QueueClip(Guid sessionClipId);

    /// <summary>Gate mode: launch while held, stop on release.</summary>
    void GateClip(Guid sessionClipId, bool held);

    /// <summary>Fire queued launches when the playhead crosses their target beat.</summary>
    void ProcessPlayhead(double beat);
}
