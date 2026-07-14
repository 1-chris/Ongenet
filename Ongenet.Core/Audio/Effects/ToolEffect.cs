using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>Tool utility: gain/pan/mono/phase plus level and correlation meters.</summary>
public sealed class ToolEffect : IAudioEffect, IAudioAnalyzerSource
{
    public const string TypeId = "tool";

    string IAudioEffect.TypeId => TypeId;
    public string Name => "Tool";
    public bool Enabled { get; set; } = true;

    public double GainDb { get; set; }
    public double Pan { get; set; }
    public bool Mono { get; set; }
    public bool InvertPhase { get; set; }

    public float PeakLeft { get; private set; }
    public float PeakRight { get; private set; }
    public float Rms { get; private set; }
    public float Correlation { get; private set; }
    public float PhaseDegrees { get; private set; }

    private int _channels = 2;
    private readonly AudioAnalyzer _analyzer = new();

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Gain", -24, 24, () => GainDb, v => GainDb = v, "0.#", "dB"),
        new FloatParameter("Pan", -1, 1, () => Pan, v => Pan = v, "0.##"),
        new BoolParameter("Mono", () => Mono, v => Mono = v),
        new BoolParameter("Invert Phase", () => InvertPhase, v => InvertPhase = v)
    };

    public void Prepare(AudioFormat format) => _channels = format.Channels < 1 ? 1 : format.Channels;

    public IAudioEffect Clone() => new ToolEffect
    {
        Enabled = Enabled, GainDb = GainDb, Pan = Pan, Mono = Mono, InvertPhase = InvertPhase
    };

    public void Process(Span<float> buffer)
    {
        if (!Enabled) return;
        var ch = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / ch;
        var gain = (float)AudioMath.Db2Lin(GainDb);
        var pan = Math.Clamp(Pan, -1, 1);
        var leftGain = (float)Math.Sqrt(0.5 * (1 - pan));
        var rightGain = (float)Math.Sqrt(0.5 * (1 + pan));

        for (var f = 0; f < frames; f++)
        {
            var l = ch > 0 ? buffer[f * ch] : 0f;
            var r = ch > 1 ? buffer[f * ch + 1] : l;
            if (Mono) { var m = 0.5f * (l + r); l = r = m; }
            l *= gain * leftGain;
            r *= gain * rightGain;
            if (InvertPhase) { l = -l; r = -r; }
            _analyzer.ProcessFrame(l, r);
            if (ch > 0) buffer[f * ch] = l;
            if (ch > 1) buffer[f * ch + 1] = r;
            for (var c = 2; c < ch; c++) buffer[f * ch + c] = 0.5f * (l + r);
        }

        _analyzer.CommitBlock();
        PeakLeft = _analyzer.PeakLeft;
        PeakRight = _analyzer.PeakRight;
        Rms = _analyzer.Rms;
        Correlation = _analyzer.Correlation;
        PhaseDegrees = _analyzer.PhaseDegrees;
    }
}
