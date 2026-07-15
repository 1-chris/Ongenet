using System;
using System.Collections.Generic;
using System.Threading;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Pass-through BS.1770 loudness and true-peak analyser suitable for placement anywhere in a chain.
/// </summary>
public sealed class LoudnessMeterEffect : IAudioEffect, IAnalyserOnlyEffect
{
    public const string TypeId = "loudness_meter";

    private MeterState? _state;

    string IAudioEffect.TypeId => TypeId;
    public string Name => "Loudness Meter";
    public bool Enabled { get; set; } = true;
    public IReadOnlyList<Parameter> Parameters => Array.Empty<Parameter>();

    public float MomentaryLufs => Volatile.Read(ref _state)?.Loudness.MomentaryLufs ?? float.NegativeInfinity;
    public float ShortTermLufs => Volatile.Read(ref _state)?.Loudness.ShortTermLufs ?? float.NegativeInfinity;
    public float IntegratedLufs => Volatile.Read(ref _state)?.Loudness.IntegratedLufs ?? float.NegativeInfinity;
    public float TruePeakDbTp => Volatile.Read(ref _state)?.TruePeak.MaxDbTp ?? -120f;
    public float Lra => Volatile.Read(ref _state)?.Loudness.LoudnessRangeLu ?? float.NaN;

    public void Prepare(AudioFormat format)
    {
        var state = new MeterState();
        state.Loudness.Prepare(format);
        state.TruePeak.Prepare(format);
        Volatile.Write(ref _state, state);
    }

    public void Process(Span<float> buffer)
    {
        if (!Enabled) return;
        var state = Volatile.Read(ref _state);
        if (state is null) return;
        state.Loudness.Process(buffer);
        state.TruePeak.Process(buffer);
    }

    public void Reset()
    {
        var state = Volatile.Read(ref _state);
        state?.Loudness.Reset();
        state?.TruePeak.Reset();
    }

    public IAudioEffect Clone() => new LoudnessMeterEffect { Enabled = Enabled };

    private sealed class MeterState
    {
        public LoudnessMeter Loudness { get; } = new();
        public TruePeakMeter TruePeak { get; } = new();
    }
}
