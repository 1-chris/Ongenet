using System;
using System.Collections.Generic;
using System.Threading;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>Tool utility: gain/pan/mono/phase plus level, correlation, LUFS, and true-peak meters.</summary>
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

    /// <summary>
    /// True when Tool is only metering (identity audio path). Offline renderers may skip it
    /// with <c>skipAnalysers</c> without changing the bounce.
    /// </summary>
    public bool IsMeteringOnly =>
        Math.Abs(GainDb) < 0.001 && Math.Abs(Pan) < 0.001 && !Mono && !InvertPhase;

    public float PeakLeft { get; private set; }
    public float PeakRight { get; private set; }
    public float Rms { get; private set; }
    public float Correlation { get; private set; }
    public float PhaseDegrees { get; private set; }
    public float ShortTermLufs => Volatile.Read(ref _meterState)?.Loudness.ShortTermLufs ?? float.NegativeInfinity;
    public float IntegratedLufs => Volatile.Read(ref _meterState)?.Loudness.IntegratedLufs ?? float.NegativeInfinity;
    public float MaxTruePeakDbTp => Volatile.Read(ref _meterState)?.TruePeak.MaxDbTp ?? -120f;

    private readonly AudioAnalyzer _analyzer = new();
    private MeterState? _meterState;

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Gain", -24, 24, () => GainDb, v => GainDb = v, "0.#", "dB"),
        new FloatParameter("Pan", -1, 1, () => Pan, v => Pan = v, "0.##"),
        new BoolParameter("Mono", () => Mono, v => Mono = v),
        new BoolParameter("Invert Phase", () => InvertPhase, v => InvertPhase = v)
    };

    public void Prepare(AudioFormat format)
    {
        var state = new MeterState(format.Channels < 1 ? 1 : format.Channels);
        state.Loudness.Prepare(format);
        state.TruePeak.Prepare(format);
        Volatile.Write(ref _meterState, state);
    }

    public IAudioEffect Clone() => new ToolEffect
    {
        Enabled = Enabled, GainDb = GainDb, Pan = Pan, Mono = Mono, InvertPhase = InvertPhase
    };

    public void Process(Span<float> buffer)
    {
        if (!Enabled) return;
        var state = Volatile.Read(ref _meterState);
        if (state is null) return;
        var ch = state.Channels;
        var frames = buffer.Length / ch;

        // Metering-only default (gain 0, pan 0, no mono/invert) is a true pass-through so
        // offline skipAnalysers and live monitoring produce the same bounce level.
        // Constant-power pan gains only apply when Pan is non-zero.
        var meteringOnly = IsMeteringOnly;
        var gain = meteringOnly ? 1f : (float)AudioMath.Db2Lin(GainDb);
        var pan = Math.Clamp(Pan, -1, 1);
        float leftGain = 1f, rightGain = 1f;
        if (!meteringOnly)
        {
            leftGain = (float)Math.Sqrt(0.5 * (1 - pan));
            rightGain = (float)Math.Sqrt(0.5 * (1 + pan));
            // At pan=0 with non-default gain, √0.5 each would still attenuate; restore unity.
            if (Math.Abs(pan) < 0.001)
            {
                leftGain = 1f;
                rightGain = 1f;
            }
        }

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
        state.Loudness.Process(buffer);
        state.TruePeak.Process(buffer);
    }

    private sealed class MeterState(int channels)
    {
        public int Channels { get; } = channels;
        public LoudnessMeter Loudness { get; } = new();
        public TruePeakMeter TruePeak { get; } = new();
    }
}
