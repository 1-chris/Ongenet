using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Mid/side stereo width + balance with per-channel M/S matrix gains.
/// </summary>
public sealed class StereoWidthEffect : IAudioEffect
{
    public const string TypeId = "stereowidth";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double Width { get; set; } = 1.0;
    public double Pan { get; set; }
    public double MidGain { get; set; } = 1.0;
    public double SideGain { get; set; } = 1.0;
    public double LeftGain { get; set; } = 1.0;
    public double RightGain { get; set; } = 1.0;
    public float Correlation { get; private set; } = 1f;

    private int _channels = 2;

    public string Name => "Stereo Width";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Width", 0.0, 2.0, () => Width, v => Width = v, "0.##"),
        new FloatParameter("Pan", -1.0, 1.0, () => Pan, v => Pan = v, "0.##"),
        new FloatParameter("Mid Gain", 0.0, 2.0, () => MidGain, v => MidGain = v, "0.##"),
        new FloatParameter("Side Gain", 0.0, 2.0, () => SideGain, v => SideGain = v, "0.##"),
        new FloatParameter("Left Gain", 0.0, 2.0, () => LeftGain, v => LeftGain = v, "0.##"),
        new FloatParameter("Right Gain", 0.0, 2.0, () => RightGain, v => RightGain = v, "0.##")
    };

    public void Prepare(AudioFormat format) => _channels = format.Channels < 1 ? 1 : format.Channels;

    public IAudioEffect Clone() => new StereoWidthEffect
    {
        Enabled = Enabled, Width = Width, Pan = Pan,
        MidGain = MidGain, SideGain = SideGain, LeftGain = LeftGain, RightGain = RightGain
    };

    public void Process(Span<float> buffer)
    {
        var channels = _channels;
        if (channels < 2) return;

        var width = (float)Math.Clamp(Width, 0, 2);
        var midGain = (float)Math.Clamp(MidGain, 0, 2);
        var sideGain = (float)Math.Clamp(SideGain, 0, 2);
        var leftGain = (float)Math.Clamp(LeftGain, 0, 2);
        var rightGain = (float)Math.Clamp(RightGain, 0, 2);
        var (gl, gr) = Mixing.StripGains(1.0, Math.Clamp(Pan, -1, 1));
        const float center = 1.41421356f;
        gl *= center; gr *= center;

        var frames = buffer.Length / channels;
        double ll = 0, rr = 0, lr = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            var l = buffer[i];
            var r = buffer[i + 1];
            var mid = (l + r) * 0.5f * midGain;
            var side = (l - r) * 0.5f * sideGain * width;
            buffer[i] = (mid + side) * leftGain * gl;
            buffer[i + 1] = (mid - side) * rightGain * gr;
            ll += buffer[i] * buffer[i];
            rr += buffer[i + 1] * buffer[i + 1];
            lr += buffer[i] * buffer[i + 1];
        }
        var denom = Math.Sqrt(ll * rr);
        Correlation = denom > 1e-12 ? (float)Math.Clamp(lr / denom, -1, 1) : 1f;
    }
}
