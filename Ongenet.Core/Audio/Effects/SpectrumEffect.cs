using System;
using System.Collections.Generic;
using System.Threading;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Pass-through combined analyser tap: spectrum, waveform, peak meters, LUFS, and true-peak.
/// Audio is unchanged.
/// </summary>
public sealed class SpectrumEffect : IAudioEffect, ISpectrumSource, IWaveformSource, IStereoScopeSource,
    IAudioAnalyzerSource, IAnalyserOnlyEffect
{
    public const string TypeId = "spectrum";

    string IAudioEffect.TypeId => TypeId;

    private readonly SpectrumScope _scope = new();
    private readonly StereoScope _stereo = new();
    private readonly AudioAnalyzer _analyzer = new();
    private MeterState? _meterState;

    public bool Enabled { get; set; } = true;

    public string Name => "Spectrum";

    public int SampleRate => Volatile.Read(ref _meterState)?.SampleRate ?? 44100;

    public float PeakLeft => _analyzer.PeakLeft;
    public float PeakRight => _analyzer.PeakRight;
    public float Rms => _analyzer.Rms;
    public float Correlation => _analyzer.Correlation;
    public float PhaseDegrees => _analyzer.PhaseDegrees;
    public float ShortTermLufs => Volatile.Read(ref _meterState)?.Loudness.ShortTermLufs ?? float.NegativeInfinity;
    public float IntegratedLufs => Volatile.Read(ref _meterState)?.Loudness.IntegratedLufs ?? float.NegativeInfinity;
    public float MomentaryLufs => Volatile.Read(ref _meterState)?.Loudness.MomentaryLufs ?? float.NegativeInfinity;
    public float LoudnessRangeLu => Volatile.Read(ref _meterState)?.Loudness.LoudnessRangeLu ?? float.NaN;
    public float TruePeakLeftDbTp => Volatile.Read(ref _meterState)?.TruePeak.PeakLeftDbTp ?? -120f;
    public float TruePeakRightDbTp => Volatile.Read(ref _meterState)?.TruePeak.PeakRightDbTp ?? -120f;
    public float MaxTruePeakDbTp => Volatile.Read(ref _meterState)?.TruePeak.MaxDbTp ?? -120f;

    public IReadOnlyList<Parameter> Parameters { get; } = Array.Empty<Parameter>();

    public void Prepare(AudioFormat format)
    {
        var state = new MeterState(
            format.Channels < 1 ? 1 : format.Channels,
            format.SampleRate > 0 ? format.SampleRate : 44100);
        state.Loudness.Prepare(format);
        state.TruePeak.Prepare(format);
        Volatile.Write(ref _meterState, state);
    }

    public IAudioEffect Clone() => new SpectrumEffect { Enabled = Enabled };

    public void ResetAnalysis()
    {
        var state = Volatile.Read(ref _meterState);
        state?.Loudness.Reset();
        state?.TruePeak.Reset();
        _analyzer.Reset();
    }

    public void Process(Span<float> buffer)
    {
        var state = Volatile.Read(ref _meterState);
        if (state is null) return;
        var ch = state.Channels;
        var frames = buffer.Length / ch;
        for (var f = 0; f < frames; f++)
        {
            var l = ch > 0 ? buffer[f * ch] : 0f;
            var r = ch > 1 ? buffer[f * ch + 1] : l;
            _analyzer.ProcessFrame(l, r);
        }
        _analyzer.CommitBlock();
        state.Loudness.Process(buffer);
        state.TruePeak.Process(buffer);
        _scope.Tap(buffer, ch);
        _stereo.Tap(buffer, ch);
    }

    public int CaptureLatest(float[] dest) => _scope.CaptureLatest(dest);

    public int CaptureLatestStereo(float[] left, float[] right) => _stereo.CaptureLatest(left, right);

    private sealed class MeterState(int channels, int sampleRate)
    {
        public int Channels { get; } = channels;
        public int SampleRate { get; } = sampleRate;
        public LoudnessMeter Loudness { get; } = new();
        public TruePeakMeter TruePeak { get; } = new();
    }
}
