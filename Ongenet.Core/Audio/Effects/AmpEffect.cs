using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A simple amp: waveshape drive into a tone (low-pass) control, optional cabinet sim, then level and dry/wet mix.
/// </summary>
public sealed class AmpEffect : IAudioEffect
{
    public const string TypeId = "amp";

    string IAudioEffect.TypeId => TypeId;

    private static readonly string[] CabNames = { "Clean", "Warm", "Fold", "Aggro" };

    public bool Enabled { get; set; } = true;

    public double Drive { get; set; } = 6.0;
    public double Tone { get; set; } = 0.5;
    public double LevelDb { get; set; }
    public double Mix { get; set; } = 1.0;
    public int CabCharacter { get; set; }
    public double CabMix { get; set; } = 0.5;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private Biquad[] _tone = Array.Empty<Biquad>();
    private CabinetSimDsp[] _cab = Array.Empty<CabinetSimDsp>();
    private BiquadCoefficients _coeffs = BiquadCoefficients.Identity;
    private double _lastTone = double.NaN, _lastSr = double.NaN;

    public string Name => "Amp";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Drive", 1.0, 24.0, () => Drive, v => Drive = v, "0.0"),
        new FloatParameter("Tone", 0.0, 1.0, () => Tone, v => Tone = v),
        new FloatParameter("Level", -24.0, 12.0, () => LevelDb, v => LevelDb = v, "0.#", "dB"),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v),
        new ChoiceParameter("Cab Character", CabNames, () => CabCharacter, v => CabCharacter = v),
        new FloatParameter("Cab Mix", 0.0, 1.0, () => CabMix, v => CabMix = v)
    };

    public void Prepare(AudioFormat format)
    {
        var sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        var channels = format.Channels < 1 ? 1 : format.Channels;
        var tone = new Biquad[channels];
        var cab = new CabinetSimDsp[channels];
        for (var c = 0; c < channels; c++)
        {
            cab[c] = new CabinetSimDsp();
            cab[c].Prepare(sampleRate);
        }

        // Publish fully-built arrays with single assignments — RebuildTracks can call Prepare from the UI
        // thread while Process runs on the audio worker pool (e.g. after "Render clip to new track").
        _sampleRate = sampleRate;
        _channels = channels;
        _tone = tone;
        _cab = cab;
        _lastTone = double.NaN;
    }

    public IAudioEffect Clone() => new AmpEffect
    {
        Enabled = Enabled, Drive = Drive, Tone = Tone, LevelDb = LevelDb, Mix = Mix,
        CabCharacter = CabCharacter, CabMix = CabMix
    };

    public void Process(Span<float> buffer)
    {
        var channels = Math.Min(_channels < 1 ? 1 : _channels, _tone.Length);
        if (channels <= 0 || _tone.Length == 0 || _cab.Length == 0) return;

        var tone = Math.Clamp(Tone, 0, 1);
        if (tone != _lastTone || _sampleRate != _lastSr)
        {
            var freq = 400.0 * Math.Pow(30.0, tone);
            _coeffs = BiquadCoefficients.Compute(FilterMode.LowPass, freq, 0.707, _sampleRate);
            _lastTone = tone;
            _lastSr = _sampleRate;
        }

        var coeffs = _coeffs;
        var drive = (float)Math.Max(1e-6, Drive);
        var level = (float)AudioMath.Db2Lin(LevelDb);
        var mix = (float)Math.Clamp(Mix, 0, 1);
        var cabMix = (float)Math.Clamp(CabMix, 0, 1);
        var cabChar = Math.Clamp(CabCharacter, 0, CabNames.Length - 1);
        var bq = _tone;
        var cab = _cab;
        if (bq.Length < channels || cab.Length < channels) return;

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var dry = buffer[i + c];
                var shaped = WaveShaper.Shape(dry, ShaperType.Tanh, drive);
                var toned = (float)bq[c].Process(coeffs, shaped);
                var cabDsp = cab[c];
                if (cabDsp is null) return;
                cabDsp.CharacterIndex = cabChar;
                cabDsp.Mix = cabMix;
                var wet = cabDsp.Process(toned);
                buffer[i + c] = (dry * (1 - mix) + wet * mix) * level;
            }
        }
    }
}
