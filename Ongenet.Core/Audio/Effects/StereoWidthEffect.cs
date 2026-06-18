using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Mid/side stereo width + balance. Width 0 = mono, 1 = unchanged, 2 = extra wide. Pan biases the
/// stereo balance (constant power). Mono tracks are unaffected by width.
/// </summary>
public sealed class StereoWidthEffect : IAudioEffect
{
    public const string TypeId = "stereowidth";

    public bool Enabled { get; set; } = true;

    public double Width { get; set; } = 1.0;
    public double Pan { get; set; }

    private int _channels = 2;

    public string Name => "Stereo Width";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Width", 0.0, 2.0, () => Width, v => Width = v, "0.##"),
        new FloatParameter("Pan", -1.0, 1.0, () => Pan, v => Pan = v, "0.##")
    };

    public void Prepare(AudioFormat format) => _channels = format.Channels < 1 ? 1 : format.Channels;

    public IAudioEffect Clone() => new StereoWidthEffect { Enabled = Enabled, Width = Width, Pan = Pan };

    public void Process(Span<float> buffer)
    {
        var channels = _channels;
        if (channels < 2) return; // width/balance only meaningful in stereo

        var width = (float)Math.Clamp(Width, 0, 2);
        var (gl, gr) = Mixing.StripGains(1.0, Math.Clamp(Pan, -1, 1));
        const float center = 1.41421356f; // √2 so centred pan is unity
        gl *= center; gr *= center;

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            var l = buffer[i];
            var r = buffer[i + 1];
            var mid = (l + r) * 0.5f;
            var side = (l - r) * 0.5f * width;
            buffer[i] = (mid + side) * gl;
            buffer[i + 1] = (mid - side) * gr;
        }
    }
}
