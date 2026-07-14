using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Soft tanh saturation: input drive into a waveshaper, dry/wet mix, and output level.
/// </summary>
public sealed class SaturatorEffect : IAudioEffect
{
    public const string TypeId = "saturator";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double Drive { get; set; } = 4.0;
    public double Mix { get; set; } = 1.0;
    public double OutputDb { get; set; }

    public string Name => "Saturator";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Drive", 1.0, 24.0, () => Drive, v => Drive = v, "0.0"),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v),
        new FloatParameter("Output", -24.0, 12.0, () => OutputDb, v => OutputDb = v, "0.#", "dB")
    };

    public void Prepare(AudioFormat format) { }

    public IAudioEffect Clone() => new SaturatorEffect
    {
        Enabled = Enabled, Drive = Drive, Mix = Mix, OutputDb = OutputDb
    };

    public void Process(Span<float> buffer)
    {
        var drive = (float)Math.Max(1e-6, Drive);
        var mix = (float)Math.Clamp(Mix, 0, 1);
        var output = (float)AudioMath.Db2Lin(OutputDb);

        for (var i = 0; i < buffer.Length; i++)
        {
            var dry = buffer[i];
            var wet = WaveShaper.Shape(dry, ShaperType.Tanh, drive);
            buffer[i] = (dry * (1 - mix) + wet * mix) * output;
        }
    }
}
