using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Independent left/right panning with a stereo width control. Each input channel is routed to the
/// outputs via its own pan law before mid/side width is applied.
/// </summary>
public sealed class DualPanEffect : IAudioEffect
{
    public const string TypeId = "dual_pan";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double PanL { get; set; }
    public double PanR { get; set; }
    public double Width { get; set; } = 1.0;

    private int _channels = 2;

    public string Name => "Dual Pan";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Pan L", -1.0, 1.0, () => PanL, v => PanL = v, "0.##"),
        new FloatParameter("Pan R", -1.0, 1.0, () => PanR, v => PanR = v, "0.##"),
        new FloatParameter("Width", 0.0, 2.0, () => Width, v => Width = v, "0.##")
    };

    public void Prepare(AudioFormat format) => _channels = format.Channels < 1 ? 1 : format.Channels;

    public IAudioEffect Clone() => new DualPanEffect
    {
        Enabled = Enabled, PanL = PanL, PanR = PanR, Width = Width
    };

    public void Process(Span<float> buffer)
    {
        if (_channels < 2) return;

        var width = (float)Math.Clamp(Width, 0, 2);
        AudioMath.PanGains(PanL, out var ll, out var lr);
        AudioMath.PanGains(PanR, out var rl, out var rr);
        const float center = 1.41421356f;
        ll *= center; lr *= center; rl *= center; rr *= center;

        var frames = buffer.Length / _channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * _channels;
            var l = buffer[i];
            var r = buffer[i + 1];
            var outL = l * ll + r * rl;
            var outR = l * lr + r * rr;
            var mid = (outL + outR) * 0.5f;
            var side = (outL - outR) * 0.5f * width;
            buffer[i] = mid + side;
            buffer[i + 1] = mid - side;
        }
    }
}
