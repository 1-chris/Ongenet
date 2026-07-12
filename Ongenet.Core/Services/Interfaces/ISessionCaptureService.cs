using System;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>
/// Logs session clip launches during transport playback and materializes them as arrangement
/// timeline clips when explicitly captured (or optionally on transport stop).
/// </summary>
public interface ISessionCaptureService
{
    /// <summary>Whether session launches are being recorded for later capture.</summary>
    bool SessionRecordArmed { get; }

    /// <summary>When true, pending captures are committed automatically on transport stop.</summary>
    bool CommitOnTransportStop { get; set; }

    /// <summary>Number of launches logged since the last capture or disarm.</summary>
    int PendingLaunchCount { get; }

    /// <summary>Raised when <see cref="SessionRecordArmed"/> changes.</summary>
    event Action? SessionRecordArmedChanged;

    /// <summary>Raised when <see cref="PendingLaunchCount"/> changes.</summary>
    event Action? PendingChanged;

    void SetSessionRecordArmed(bool armed);

    /// <summary>Records a session clip launch at the given arrangement beat (only when armed).</summary>
    void LogLaunch(Guid sessionClipId, double launchBeat);

    /// <summary>Creates arrangement clips from logged launches and clears the log.</summary>
    void Capture();

    /// <summary>Captures pending launches when transport stops if <see cref="CommitOnTransportStop"/> is set.</summary>
    void OnTransportStopped();
}
