using System;
using System.Threading;
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
    // Stored as the raw bits of a double and accessed via Interlocked: the audio thread writes the
    // playhead every block while the UI and MIDI-input threads read it (e.g. to timestamp recorded
    // notes). A plain double field is not guaranteed atomic across threads by the .NET memory model.
    private long _playheadBits;
    private double _loopStart;
    private double _loopEnd;

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
            if (_state == TransportState.Stopped) SetPlayhead(clamped);
            StartBeatChanged?.Invoke();
        }
    }

    public double PlayheadBeats => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _playheadBits));

    private void SetPlayhead(double beats)
        => Interlocked.Exchange(ref _playheadBits, BitConverter.DoubleToInt64Bits(beats));

    /// <summary>Count-in length in beats for the next Play; reset on Stop.</summary>
    public int CountInBeats { get; set; }

    /// <summary>True while a recording session is active (set by the recording service).</summary>
    public bool IsRecording { get; set; }

    public double LoopStart
    {
        get => _loopStart;
        set
        {
            var clamped = value < 0 ? 0 : value;
            if (_loopStart == clamped) return;
            _loopStart = clamped;
            LoopChanged?.Invoke();
        }
    }

    public double LoopEnd
    {
        get => _loopEnd;
        set
        {
            var clamped = value < 0 ? 0 : value;
            if (_loopEnd == clamped) return;
            _loopEnd = clamped;
            LoopChanged?.Invoke();
        }
    }

    public bool IsLoopActive => _loopEnd > _loopStart;

    public event Action<TransportState>? StateChanged;
    public event Action<Tempo>? TempoChanged;
    public event Action? StartBeatChanged;
    public event Action? LoopChanged;
    public event Action? CountInFinished;

    public void Play() => State = TransportState.Playing;

    public void Stop()
    {
        State = TransportState.Stopped;
        SetPlayhead(_startBeat);
        CountInBeats = 0;
    }

    public void NotifyPlayhead(double beats) => SetPlayhead(beats);

    public void NotifyCountInFinished() => CountInFinished?.Invoke();
}
