using System;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>
/// Records live MIDI input (from <see cref="IPreviewService"/>) into the armed instrument tracks.
/// Starting a recording plays a one-bar metronome count-in (via the transport's count-in), then
/// captures notes against the moving playhead until the transport stops; on stop the captured
/// notes are committed as MIDI clips.
/// </summary>
public interface IRecordingService
{
    /// <summary>True while a recording session is active (count-in or capturing).</summary>
    bool IsRecording { get; }

    /// <summary>
    /// Input-quantize grid in beats: recorded note starts snap to the nearest multiple. 0 (default)
    /// disables quantize, capturing notes at their exact played position. E.g. 0.25 = 1/16 in 4/4.
    /// </summary>
    double InputQuantizeBeats { get; set; }

    /// <summary>Raised when <see cref="IsRecording"/> changes (may fire from the audio thread).</summary>
    event Action? StateChanged;

    /// <summary>
    /// Begins a recording session: arms targets (armed instrument tracks, or the selected
    /// instrument track as a fallback), starts the count-in, and begins playback. No-op if
    /// already recording or if there is no instrument track to record into.
    /// </summary>
    void StartRecording();

    /// <summary>
    /// Ends the recording session: finalises the captured notes and stops the transport.
    /// </summary>
    void StopRecording();

    /// <summary>
    /// Pumps the live recording: grows the take clip and its notes to the current playhead so the
    /// timeline shows the take filling in. Called each frame by the timeline timer while recording.
    /// </summary>
    void RefreshLive();
}
