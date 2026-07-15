using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Music;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Pitch-correction (auto-tune) with optional harmony voices via additional <see cref="PitchShifter"/>s.
/// </summary>
public sealed class AutoTuneEffect : IAudioEffect
{
    public const string TypeId = "autotune";

    private static readonly string[] ScaleNames = Enum.GetNames(typeof(ScaleType));

    string IAudioEffect.TypeId => TypeId;
    public string Name => "Auto-Tune";
    public bool Enabled { get; set; } = true;

    public int KeyIndex { get; set; }
    public int ScaleIndex { get; set; }
    public double Amount { get; set; } = 1.0;
    public double RetuneMs { get; set; } = 4.0;
    public double Mix { get; set; } = 1.0;
    public double ReferenceHz { get; set; } = 440.0;
    public int HarmonyVoices { get; set; }
    public double HarmonyInterval1 { get; set; } = 7.0;
    public double HarmonyInterval2 { get; set; } = 12.0;
    public double HarmonyInterval3 { get; set; } = -12.0;
    public double HarmonyMix { get; set; } = 0.35;

    private const int DetectHop = 256;
    private const int MaxHarmonyVoices = 3;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private PitchDetector _detector = new();
    private PitchShifter[] _shifters = Array.Empty<PitchShifter>();
    private PitchShifter[][] _harmony = Array.Empty<PitchShifter[]>();
    private OnePole _ratioSmooth = new();
    private double _lastPeriod;
    private double _lastF0;
    private double _lastRatio = 1.0;
    private int _sinceDetect;
    private int _currentNote = -1;

    private const double NoteHoldSemitones = 0.7;

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Key", MusicTheory.NoteNames, () => KeyIndex, v => KeyIndex = v),
        new ChoiceParameter("Scale", ScaleNames, () => ScaleIndex, v => ScaleIndex = v),
        new FloatParameter("Amount", 0.0, 1.0, () => Amount, v => Amount = v, "0%", "", 1.0),
        new FloatParameter("Retune", 0.0, 200.0, () => RetuneMs, v => RetuneMs = v, "0", "ms", 1.0),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v, "0%", "", 1.0),
        new FloatParameter("Ref", 415.0, 465.0, () => ReferenceHz, v => ReferenceHz = v, "0", "Hz", 1.0),
        new ChoiceParameter("Harmony Voices", new[] { "0", "1", "2", "3" }, () => HarmonyVoices, v => HarmonyVoices = v),
        new FloatParameter("Interval 1", -24.0, 24.0, () => HarmonyInterval1, v => HarmonyInterval1 = v, "0", "st"),
        new FloatParameter("Interval 2", -24.0, 24.0, () => HarmonyInterval2, v => HarmonyInterval2 = v, "0", "st"),
        new FloatParameter("Interval 3", -24.0, 24.0, () => HarmonyInterval3, v => HarmonyInterval3 = v, "0", "st"),
        new FloatParameter("Harmony Mix", 0.0, 1.0, () => HarmonyMix, v => HarmonyMix = v)
    };

    public void Prepare(AudioFormat format)
    {
        var sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        var channels = format.Channels < 1 ? 1 : format.Channels;

        var detector = new PitchDetector();
        detector.Configure(sampleRate, 70.0, 1000.0);
        var shifters = new PitchShifter[channels];
        var harmony = new PitchShifter[channels][];
        for (var ch = 0; ch < channels; ch++)
        {
            shifters[ch] = new PitchShifter();
            shifters[ch].Configure(sampleRate);
            harmony[ch] = new PitchShifter[MaxHarmonyVoices];
            for (var h = 0; h < MaxHarmonyVoices; h++)
            {
                harmony[ch][h] = new PitchShifter();
                harmony[ch][h].Configure(sampleRate);
            }
        }

        var ratioSmooth = new OnePole();
        ratioSmooth.SetSmoothTime(RetuneMs, sampleRate);
        ratioSmooth.Reset(1.0);

        // Publish fully-built state with single assignments — RebuildTracks can call Prepare from the UI
        // thread while Process runs on the audio worker pool (e.g. after "Render clip to new track").
        _sampleRate = sampleRate;
        _channels = channels;
        _detector = detector;
        _shifters = shifters;
        _harmony = harmony;
        _ratioSmooth = ratioSmooth;
        _lastPeriod = 0;
        _lastF0 = 0;
        _lastRatio = 1.0;
        _sinceDetect = 0;
        _currentNote = -1;
    }

    public void Process(Span<float> buffer)
    {
        var shifters = _shifters;
        var harmony = _harmony;
        var detector = _detector;
        var ratioSmooth = _ratioSmooth;
        var channels = Math.Min(_channels < 1 ? 1 : _channels, shifters.Length);
        if (channels <= 0 || harmony.Length < channels) return;
        var frames = buffer.Length / channels;

        _sinceDetect += frames;
        if (_sinceDetect >= DetectHop)
        {
            _sinceDetect = 0;
            UpdateCorrection(detector);
        }

        for (var ch = 0; ch < channels; ch++)
        {
            var shifter = shifters[ch];
            var harm = harmony[ch];
            if (shifter is null || harm is null) return;
            shifter.SetPeriod(_lastPeriod);
            for (var h = 0; h < MaxHarmonyVoices; h++)
            {
                if (harm[h] is null) return;
                harm[h].SetPeriod(_lastPeriod);
            }
        }

        ratioSmooth.SetSmoothTime(RetuneMs, _sampleRate);
        var mix = AudioMath.Clamp(Mix, 0.0, 1.0);
        var harmonyMix = (float)Math.Clamp(HarmonyMix, 0, 1);
        var voiceCount = Math.Clamp(HarmonyVoices, 0, MaxHarmonyVoices);
        var intervals = new[] { HarmonyInterval1, HarmonyInterval2, HarmonyInterval3 };

        for (var f = 0; f < frames; f++)
        {
            var ratio = ratioSmooth.ProcessLP(_lastRatio);

            var mono = 0f;
            for (var ch = 0; ch < channels; ch++) mono += buffer[f * channels + ch];
            detector.Push(mono / channels);

            for (var ch = 0; ch < channels; ch++)
            {
                var i = f * channels + ch;
                var dry = buffer[i];
                var shifter = shifters[ch];
                shifter.SetRatio(ratio);
                var wet = shifter.Process(dry);

                if (voiceCount > 0 && harmonyMix > 1e-6f)
                {
                    var harmSum = 0f;
                    for (var h = 0; h < voiceCount; h++)
                    {
                        var harmRatio = ratio * Math.Pow(2.0, intervals[h] / 12.0);
                        var hs = harmony[ch][h];
                        hs.SetRatio(harmRatio);
                        harmSum += hs.Process(dry);
                    }
                    wet += harmSum / voiceCount * harmonyMix;
                }

                buffer[i] = (float)(dry * (1.0 - mix) + wet * mix);
            }
        }
    }

    private void UpdateCorrection(PitchDetector detector)
    {
        var f0 = detector.Detect();
        if (f0 <= 0) return;

        if (_lastF0 > 0)
        {
            while (f0 > _lastF0 * 1.5) f0 *= 0.5;
            while (f0 < _lastF0 * 0.6667) f0 *= 2.0;
        }

        _lastF0 = f0;
        _lastPeriod = _sampleRate / f0;

        var refHz = ReferenceHz <= 0 ? 440.0 : ReferenceHz;
        var midiFloat = 69.0 + 12.0 * Math.Log2(f0 / refHz);
        var scale = (ScaleType)Math.Clamp(ScaleIndex, 0, ScaleNames.Length - 1);

        if (_currentNote < 0 || Math.Abs(midiFloat - _currentNote) > NoteHoldSemitones)
            _currentNote = MusicTheory.SnapToScale(midiFloat, KeyIndex, scale);

        var targetHz = refHz * Math.Pow(2.0, (_currentNote - 69.0) / 12.0);
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
        ReferenceHz = ReferenceHz,
        HarmonyVoices = HarmonyVoices,
        HarmonyInterval1 = HarmonyInterval1,
        HarmonyInterval2 = HarmonyInterval2,
        HarmonyInterval3 = HarmonyInterval3,
        HarmonyMix = HarmonyMix
    };
}
