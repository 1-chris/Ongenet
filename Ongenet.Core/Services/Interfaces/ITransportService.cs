using System;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>
/// Controls playback state, tempo, the start marker, and the current playhead. The audio engine
/// drives <see cref="PlayheadBeats"/> while playing; the UI reads it to draw the playhead line.
/// </summary>
public interface ITransportService
{
    /// <summary>Current playback state.</summary>
    TransportState State { get; }

    /// <summary>Global tempo. Setting it raises <see cref="TempoChanged"/>.</summary>
    Tempo Tempo { get; set; }

    /// <summary>Where playback starts (and returns to on stop), in beats. Set by clicking the ruler.</summary>
    double StartBeat { get; set; }

    /// <summary>Current playhead position, in beats. Written by the engine during playback.</summary>
    double PlayheadBeats { get; }

    /// <summary>
    /// Metronome pre-roll length in beats for the next <see cref="Play"/>. When &gt; 0 the engine
    /// counts in (clicks only, playhead parked at <see cref="StartBeat"/>) before content plays.
    /// Set by the recording service; reset to 0 on <see cref="Stop"/>.
    /// </summary>
    int CountInBeats { get; set; }

    /// <summary>
    /// True while a recording session is active. Set by the recording service; the engine reads it so
    /// armed automation lanes are not driven (the user's manual moves are being captured instead).
    /// </summary>
    bool IsRecording { get; set; }

    /// <summary>Loop region start, in beats. Set via the "[" control. Raises <see cref="LoopChanged"/>.</summary>
    double LoopStart { get; set; }

    /// <summary>Loop region end, in beats. Set via the "]" control. Raises <see cref="LoopChanged"/>.</summary>
    double LoopEnd { get; set; }

    /// <summary>True when a usable loop region is set (<see cref="LoopEnd"/> &gt; <see cref="LoopStart"/>).</summary>
    bool IsLoopActive { get; }

    /// <summary>Raised when <see cref="State"/> changes.</summary>
    event Action<TransportState>? StateChanged;

    /// <summary>Raised when <see cref="Tempo"/> changes.</summary>
    event Action<Tempo>? TempoChanged;

    /// <summary>Raised when <see cref="StartBeat"/> changes.</summary>
    event Action? StartBeatChanged;

    /// <summary>Raised when <see cref="LoopStart"/> or <see cref="LoopEnd"/> changes.</summary>
    event Action? LoopChanged;

    /// <summary>Raised (from the audio thread) when the count-in finishes and content playback begins.</summary>
    event Action? CountInFinished;

    /// <summary>Starts playback from <see cref="StartBeat"/>.</summary>
    void Play();

    /// <summary>Stops playback and returns the playhead to <see cref="StartBeat"/>.</summary>
    void Stop();

    /// <summary>Engine hook: reports the current playhead position (no event; the UI polls).</summary>
    void NotifyPlayhead(double beats);

    /// <summary>Engine hook: signals that the count-in pre-roll has elapsed (raises <see cref="CountInFinished"/>).</summary>
    void NotifyCountInFinished();
}
