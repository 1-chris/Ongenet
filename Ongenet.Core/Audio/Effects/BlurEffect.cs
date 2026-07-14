using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Serial all-pass diffusion that smears transients into a dense wash without colouring the
/// spectrum. Size scales delay times; Amount sets diffusion feedback.
/// </summary>
public sealed class BlurEffect : IAudioEffect
{
    public const string TypeId = "blur";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double Size { get; set; } = 0.6;
    public double Amount { get; set; } = 0.5;
    public double Mix { get; set; } = 0.5;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private AllpassDiffuser[] _diffusers = Array.Empty<AllpassDiffuser>();
    private double _lastSize = double.NaN, _lastAmount = double.NaN, _lastSr = double.NaN;

    public string Name => "Blur";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Size", 0.05, 1.0, () => Size, v => Size = v),
        new FloatParameter("Amount", 0.0, 0.9, () => Amount, v => Amount = v),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        var diffusers = new AllpassDiffuser[_channels];
        for (var c = 0; c < _channels; c++) diffusers[c] = new AllpassDiffuser();
        _diffusers = diffusers;
        _lastSize = double.NaN;
    }

    public IAudioEffect Clone() => new BlurEffect
    {
        Enabled = Enabled, Size = Size, Amount = Amount, Mix = Mix
    };

    public void Process(Span<float> buffer)
    {
        var diffusers = _diffusers;
        if (diffusers.Length == 0) return;
        var channels = Math.Min(_channels < 1 ? 1 : _channels, diffusers.Length);
        var size = Math.Clamp(Size, 0.05, 1);
        var amount = Math.Clamp(Amount, 0, 0.9);

        if (size != _lastSize || amount != _lastAmount || _sampleRate != _lastSr)
        {
            for (var c = 0; c < channels; c++)
                diffusers[c].Configure(size, amount, (int)_sampleRate);
            _lastSize = size;
            _lastAmount = amount;
            _lastSr = _sampleRate;
        }

        var mix = (float)Math.Clamp(Mix, 0, 1);
        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var dry = buffer[i + c];
                var wet = diffusers[c].Process(dry);
                buffer[i + c] = dry * (1 - mix) + wet * mix;
            }
        }
    }
}
