using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A waveshaping distortion: input drive into a shaping curve (soft/hard/foldback), optional cabinet
/// sim stage, output level, and a dry/wet mix.
/// </summary>
public sealed class DistortionEffect : IAudioEffect
{
    public const string TypeId = "distortion";

    string IAudioEffect.TypeId => TypeId;

    private static readonly string[] ModeNames = { "Soft", "Hard", "Foldback" };
    private static readonly string[] CabNames = { "Off", "Clean", "Warm", "Fold", "Aggro" };

    public bool Enabled { get; set; } = true;

    public double DriveDb { get; set; } = 12.0;
    public double OutputDb { get; set; }
    public double Mix { get; set; } = 1.0;
    public int Mode { get; set; }
    public int CabCharacter { get; set; }
    public double CabMix { get; set; }

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private CabinetSimDsp[] _cab = Array.Empty<CabinetSimDsp>();

    public string Name => "Distortion";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Drive", 0.0, 48.0, () => DriveDb, v => DriveDb = v, "0.#", "dB"),
        new FloatParameter("Output", -24.0, 6.0, () => OutputDb, v => OutputDb = v, "0.#", "dB"),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v),
        new ChoiceParameter("Mode", ModeNames, () => Mode, v => Mode = v),
        new ChoiceParameter("Cab", CabNames, () => CabCharacter, v => CabCharacter = v),
        new FloatParameter("Cab Mix", 0.0, 1.0, () => CabMix, v => CabMix = v)
    };

    public void Prepare(AudioFormat format)
    {
        var sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        var channels = format.Channels < 1 ? 1 : format.Channels;
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
        _cab = cab;
    }

    public IAudioEffect Clone() => new DistortionEffect
    {
        Enabled = Enabled, DriveDb = DriveDb, OutputDb = OutputDb, Mix = Mix, Mode = Mode,
        CabCharacter = CabCharacter, CabMix = CabMix
    };

    public void Process(Span<float> buffer)
    {
        var cab = _cab;
        var channels = Math.Min(_channels < 1 ? 1 : _channels, cab.Length);
        if (channels <= 0) return;
        var drive = (float)AudioMath.Db2Lin(DriveDb);
        var output = (float)AudioMath.Db2Lin(OutputDb);
        var mix = (float)Math.Clamp(Mix, 0, 1);
        var mode = Mode;
        var cabChar = CabCharacter;
        var cabMix = (float)Math.Clamp(CabMix, 0, 1);
        var useCab = cabChar > 0 && cabMix > 1e-6f;

        var frames = buffer.Length / Math.Max(1, channels);
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var idx = channels > 1 ? i + c : i;
                var dry = buffer[idx];
                var shaped = Shape(dry * drive, mode);
                if (useCab)
                {
                    var cabDsp = cab[c];
                    if (cabDsp is null) return;
                    cabDsp.CharacterIndex = cabChar - 1;
                    cabDsp.Mix = cabMix;
                    shaped = cabDsp.Process(shaped);
                }
                buffer[idx] = (dry * (1 - mix) + shaped * mix) * output;
            }
        }
    }

    private static float Shape(float x, int mode) => mode switch
    {
        1 => Math.Clamp(x, -1f, 1f),
        2 => Foldback(x),
        _ => (float)Math.Tanh(x)
    };

    private static float Foldback(float x)
    {
        for (var guard = 0; guard < 8 && (x > 1f || x < -1f); guard++)
        {
            if (x > 1f) x = 2f - x;
            else if (x < -1f) x = -2f - x;
        }

        return x;
    }
}
