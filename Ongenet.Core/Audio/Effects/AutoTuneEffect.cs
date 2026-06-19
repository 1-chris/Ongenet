using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Music;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Pitch-correction (auto-tune). Detects the incoming signal's fundamental (<see cref="PitchDetector"/>),
/// finds the nearest note in the chosen key/scale (the same scales as the MIDI generator, via
/// <see cref="MusicTheory"/>), and retunes the audio toward it with a delay-line
/// <see cref="PitchShifter"/>. All controls are generic parameters, so the UI and project save/load are
/// automatic.
/// </summary>
public sealed class AutoTuneEffect : IAudioEffect
{
    public const string TypeId = "autotune";

    private static readonly string[] ScaleNames = Enum.GetNames(typeof(ScaleType));

    string IAudioEffect.TypeId => TypeId;
    public string Name => "Auto-Tune";
    public bool Enabled { get; set; } = true;

    /// <summary>Key tonic pitch class (0 = C), index into <see cref="MusicTheory.NoteNames"/>.</summary>
    public int KeyIndex { get; set; }

    /// <summary>Scale index into <see cref="ScaleType"/>.</summary>
    public int ScaleIndex { get; set; }

    /// <summary>Correction strength: 0 = no shift, 1 = fully snapped to the target note.</summary>
    public double Amount { get; set; } = 1.0;

    /// <summary>Retune speed (ms): low = the hard "classic" snap, higher = a natural glide to pitch.</summary>
    public double RetuneMs { get; set; } = 4.0;

    /// <summary>Dry/wet blend.</summary>
    public double Mix { get; set; } = 1.0;

    /// <summary>Reference pitch for A4 (Hz).</summary>
    public double ReferenceHz { get; set; } = 440.0;

    // Detection runs at most every this many samples (bounds CPU for small engine blocks and keeps
    // the analysis hop stable regardless of block size).
    private const int DetectHop = 256;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private readonly PitchDetector _detector = new();
    private PitchShifter[] _shifters = Array.Empty<PitchShifter>();
    private readonly OnePole _ratioSmooth = new();
    private double _lastPeriod;     // last voiced period (samples)
    private double _lastF0;         // last voiced f0 (Hz), for octave-guarding the next detection
    private double _lastRatio = 1.0;// last correction ratio, held through unvoiced gaps so it stays pinned
    private int _sinceDetect;       // samples processed since the last detection
    private int _currentNote = -1;  // the scale note we're currently pinned to (MIDI; -1 = none yet)

    // Stay pinned to the current note until the input pitch moves more than this far (semitones)
    // from it. Wider than 0.5 so vibrato / drift near a note boundary doesn't flutter between notes.
    private const double NoteHoldSemitones = 0.7;

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Key", MusicTheory.NoteNames, () => KeyIndex, v => KeyIndex = v),
        new ChoiceParameter("Scale", ScaleNames, () => ScaleIndex, v => ScaleIndex = v),
        new FloatParameter("Amount", 0.0, 1.0, () => Amount, v => Amount = v, "0%", "", 1.0),
        new FloatParameter("Retune", 0.0, 200.0, () => RetuneMs, v => RetuneMs = v, "0", "ms", 1.0),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v, "0%", "", 1.0),
        new FloatParameter("Ref", 415.0, 465.0, () => ReferenceHz, v => ReferenceHz = v, "0", "Hz", 1.0)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;

        _detector.Configure(_sampleRate, 70.0, 1000.0);
        _shifters = new PitchShifter[_channels];
        for (var ch = 0; ch < _channels; ch++)
        {
            _shifters[ch] = new PitchShifter();
            _shifters[ch].Configure(_sampleRate);
        }

        _ratioSmooth.SetSmoothTime(RetuneMs, _sampleRate);
        _ratioSmooth.Reset(1.0);
        _lastPeriod = 0;
        _lastF0 = 0;
        _lastRatio = 1.0;
        _sinceDetect = 0;
        _currentNote = -1;
    }

    public void Process(Span<float> buffer)
    {
        var channels = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / channels;

        // Re-detect the pitch and recompute the target correction periodically (every DetectHop
        // samples). Between detections — and through unvoiced gaps (consonants/breath) — the last
        // ratio/period are held so the voice stays pinned instead of drifting back to no-shift.
        _sinceDetect += frames;
        if (_sinceDetect >= DetectHop)
        {
            _sinceDetect = 0;
            UpdateCorrection();
        }

        for (var ch = 0; ch < channels; ch++) _shifters[ch].SetPeriod(_lastPeriod);
        _ratioSmooth.SetSmoothTime(RetuneMs, _sampleRate);
        var mix = AudioMath.Clamp(Mix, 0.0, 1.0);

        for (var f = 0; f < frames; f++)
        {
            var ratio = _ratioSmooth.ProcessLP(_lastRatio);

            // Feed a mono mix to the detector for subsequent detections.
            var mono = 0f;
            for (var ch = 0; ch < channels; ch++) mono += buffer[f * channels + ch];
            _detector.Push(mono / channels);

            for (var ch = 0; ch < channels; ch++)
            {
                var i = f * channels + ch;
                var dry = buffer[i];
                _shifters[ch].SetRatio(ratio);
                var wet = _shifters[ch].Process(dry);
                buffer[i] = (float)(dry * (1.0 - mix) + wet * mix);
            }
        }
    }

    // Detects f0 and updates the target correction ratio. On an unvoiced/uncertain frame it leaves
    // the previous correction in place (held), so brief gaps don't un-tune the note.
    private void UpdateCorrection()
    {
        var f0 = _detector.Detect();
        if (f0 <= 0) return; // unvoiced → hold the last correction

        // Octave-guard against the previous pitch: YIN occasionally reports a half/double octave,
        // which would make the output leap. Fold the new pitch into the octave of the last one.
        if (_lastF0 > 0)
        {
            while (f0 > _lastF0 * 1.5) f0 *= 0.5;
            while (f0 < _lastF0 * 0.6667) f0 *= 2.0;
        }

        _lastF0 = f0;
        _lastPeriod = _sampleRate / f0; // lock the shifter grains to the input period

        var refHz = ReferenceHz <= 0 ? 440.0 : ReferenceHz;
        var midiFloat = 69.0 + 12.0 * Math.Log2(f0 / refHz);
        var scale = (ScaleType)Math.Clamp(ScaleIndex, 0, ScaleNames.Length - 1);

        // Note hysteresis: hold the current scale note until the input clearly moves off it, so the
        // output pins solidly instead of flickering between neighbouring notes near a boundary.
        if (_currentNote < 0 || Math.Abs(midiFloat - _currentNote) > NoteHoldSemitones)
            _currentNote = MusicTheory.SnapToScale(midiFloat, KeyIndex, scale);

        var targetHz = refHz * Math.Pow(2.0, (_currentNote - 69.0) / 12.0);

        // Pull toward the target by Amount, in the semitone (log) domain. Amount 1 = output lands
        // exactly on the scale note (the hard, pinned auto-tune sound).
        var semis = 12.0 * Math.Log2(targetHz / f0) * AudioMath.Clamp(Amount, 0.0, 1.0);
        _lastRatio = AudioMath.Clamp(Math.Pow(2.0, semis / 12.0), 0.5, 2.0);
    }

    public IAudioEffect Clone() => new AutoTuneEffect
    {
        Enabled = Enabled,
        KeyIndex = KeyIndex,
        ScaleIndex = ScaleIndex,
        Amount = Amount,
        RetuneMs = RetuneMs,
        Mix = Mix,
        ReferenceHz = ReferenceHz
    };
}
