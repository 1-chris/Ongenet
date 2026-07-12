using System;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>
/// Logs session clip launches during transport playback and materializes them as arrangement
/// timeline clips on stop or when the user invokes capture.
/// </summary>
public interface ISessionCaptureService
{
    /// <summary>Number of launches logged since the last capture or transport start.</summary>
    int PendingLaunchCount { get; }

    /// <summary>Raised when <see cref="PendingLaunchCount"/> changes.</summary>
    event Action? PendingChanged;

    /// <summary>Records a session clip launch at the given arrangement beat.</summary>
    void LogLaunch(Guid sessionClipId, double launchBeat);

    /// <summary>Creates arrangement clips from logged launches and clears the log.</summary>
    void Capture();

    /// <summary>Captures pending launches when transport stops (no-op when empty).</summary>
    void OnTransportStopped();
}
