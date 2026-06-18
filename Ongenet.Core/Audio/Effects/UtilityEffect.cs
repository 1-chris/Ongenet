using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A utility/gain stage: output gain, stereo balance, mono fold-down, and phase invert — the
/// everyday fixes you reach for at the end of a chain.
/// </summary>
public sealed class UtilityEffect : IAudioEffect
{
    public const string TypeId = "utility";

    public bool Enabled { get; set; } = true;

    public double GainDb { get; set; }
    public double Pan { get; set; }
    public bool Mono { get; set; }
    public bool InvertPhase { get; set; }

    private int _channels = 2;

    public string Name => "Utility";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Gain", -24.0, 24.0, () => GainDb, v => GainDb = v, "0.#", "dB"),
        new FloatParameter("Pan", -1.0, 1.0, () => Pan, v => Pan = v, "0.##"),
        new BoolParameter("Mono", () => Mono, v => Mono = v),
        new BoolParameter("Invert Phase", () => InvertPhase, v => InvertPhase = v)
    };

    public void Prepare(AudioFormat format) => _channels = format.Channels < 1 ? 1 : format.Channels;

    public IAudioEffect Clone() => new UtilityEffect
    {
        Enabled = Enabled, GainDb = GainDb, Pan = Pan, Mono = Mono, InvertPhase = InvertPhase
    };

    public void Process(Span<float> buffer)
    {
        var channels = _channels;
        var gain = (float)AudioMath.Db2Lin(GainDb) * (InvertPhase ? -1f : 1f);
        var stereo = channels >= 2;
        var (gl, gr) = Mixing.StripGains(1.0, Math.Clamp(Pan, -1, 1));
        const float center = 1.41421356f;
        gl *= center; gr *= center;

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;

            if (Mono && stereo)
            {
                var m = (buffer[i] + buffer[i + 1]) * 0.5f;
                buffer[i] = m;
                buffer[i + 1] = m;
            }

            if (stereo)
            {
                buffer[i] *= gain * gl;
                buffer[i + 1] *= gain * gr;
            }
            else
            {
                for (var c = 0; c < channels; c++) buffer[i + c] *= gain;
            }
        }
    }
}
