using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Lo-fi bitcrusher: bit-depth reduction and sample-rate decimation, with optional gate, drive, and shape.
/// </summary>
public sealed class BitcrusherEffect : IAudioEffect
{
    public const string TypeId = "bitcrusher";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double Bits { get; set; } = 8.0;
    public double Downsample { get; set; } = 4.0;
    public double Gate { get; set; } = 1.0;
    public double Shape { get; set; }
    public double Drive { get; set; }
    public bool AntiAlias { get; set; }
    public double Mix { get; set; } = 1.0;

    private int _channels = 2;
    private BitcrusherDsp[] _dsp = Array.Empty<BitcrusherDsp>();

    public string Name => "Bitcrusher";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Bits", 1.0, 16.0, () => Bits, v => Bits = v, "0"),
        new FloatParameter("Downsample", 1.0, 50.0, () => Downsample, v => Downsample = v, "0"),
        new FloatParameter("Gate", 0.0, 1.0, () => Gate, v => Gate = v, "0.00"),
        new FloatParameter("Shape", 0.0, 1.0, () => Shape, v => Shape = v, "0.00"),
        new FloatParameter("Drive", 0.0, 1.0, () => Drive, v => Drive = v, "0.00"),
        new BoolParameter("Anti-alias", () => AntiAlias, v => AntiAlias = v),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v)
    };

    public void Prepare(AudioFormat format)
    {
        var channels = format.Channels < 1 ? 1 : format.Channels;
        var dsp = new BitcrusherDsp[channels];
        for (var c = 0; c < channels; c++)
        {
            dsp[c] = new BitcrusherDsp();
            dsp[c].Reset();
        }

        // Publish fully-built arrays with single assignments — RebuildTracks can call Prepare from the UI
        // thread while Process runs on the audio worker pool (e.g. after "Render clip to new track").
        _channels = channels;
        _dsp = dsp;
    }

    public IAudioEffect Clone() => new BitcrusherEffect
    {
        Enabled = Enabled,
        Bits = Bits,
        Downsample = Downsample,
        Gate = Gate,
        Shape = Shape,
        Drive = Drive,
        AntiAlias = AntiAlias,
        Mix = Mix
    };

    public void Process(Span<float> buffer)
    {
        if (!Enabled) return;

        var dsp = _dsp;
        if (dsp.Length == 0) return;

        var channels = Math.Min(_channels, dsp.Length);
        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            for (var c = 0; c < channels; c++)
            {
                var d = dsp[c];
                if (d is null) return;
                d.Bits = Bits;
                d.Downsample = Downsample;
                d.Gate = Gate;
                d.Shape = Shape;
                d.Drive = Drive;
                d.AntiAlias = AntiAlias;
                d.Mix = Mix;
                buffer[frame * channels + c] = d.Process(buffer[frame * channels + c]);
            }
        }
    }
}
