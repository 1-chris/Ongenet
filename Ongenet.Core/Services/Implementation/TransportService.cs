using System;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services.Implementation;

/// <summary>
/// Default <see cref="ITransportService"/>. Holds playback state, tempo, the start marker, and
/// the playhead. The audio engine writes the playhead via <see cref="NotifyPlayhead"/>.
/// </summary>
public class TransportService : ITransportService
{
    private TransportState _state = TransportState.Stopped;
    private Tempo _tempo = new(120.0);
    private double _startBeat;
    private double _playheadBeats;

    public TransportState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            StateChanged?.Invoke(value);
        }
    }

    public Tempo Tempo
    {
        get => _tempo;
        set
        {
            if (_tempo == value) return;
            _tempo = value;
            TempoChanged?.Invoke(value);
        }
    }

    public double StartBeat
    {
        get => _startBeat;
        set
        {
            var clamped = value < 0 ? 0 : value;
            if (_startBeat == clamped) return;
            _startBeat = clamped;
            // While stopped, the playhead rests on the marker so the UI shows where Play begins.
            if (_state == TransportState.Stopped) _playheadBeats = clamped;
            StartBeatChanged?.Invoke();
        }
    }

    public double PlayheadBeats => _playheadBeats;

    /// <summary>Count-in length in beats for the next Play; reset on Stop.</summary>
    public int CountInBeats { get; set; }

    /// <summary>True while a recording session is active (set by the recording service).</summary>
    public bool IsRecording { get; set; }

    public event Action<TransportState>? StateChanged;
    public event Action<Tempo>? TempoChanged;
    public event Action? StartBeatChanged;
    public event Action? CountInFinished;

    public void Play() => State = TransportState.Playing;

    public void Stop()
    {
        State = TransportState.Stopped;
        _playheadBeats = _startBeat;
        CountInBeats = 0;
    }

    public void NotifyPlayhead(double beats) => _playheadBeats = beats;

    public void NotifyCountInFinished() => CountInFinished?.Invoke();
}
