using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Peak limiter with spectral threshold analysis, gain-reduction metering, and optional 2×/4×
/// FIR oversampling (upsample → peak detect + clamp → downsample) for true-peak / ISP control.
/// </summary>
public sealed class PeakLimiterEffect : IAudioEffect, IGainReductionSource
{
    public const string TypeId = "peak_limiter";

    string IAudioEffect.TypeId => TypeId;
    public string Name => "Peak Limiter";
    public bool Enabled { get; set; } = true;

    public double ThresholdDb { get; set; } = 0.0;
    public double CeilingDb { get; set; } = -0.3;
    public double ReleaseMs { get; set; } = 80.0;
    public bool SpectralLimiter { get; set; }
    public int MasteringPresetIndex { get; set; }
    /// <summary>0 = 1×, 1 = 2×, 2 = 4× oversampling of the peak detector / clamp path.</summary>
    public int OversampleIndex { get; set; } = 1;

    public double GainReductionDb { get; private set; }
    public float InputPeak { get; private set; }
    public float OutputPeak { get; private set; }

    private int _channels = 2;
    private double _sampleRate = 44100;
    private readonly EnvelopeFollower _follower = new();
    private readonly EnvelopeFollower _spectralFollower = new();
    private int _lastPresetIndex = -1;

    private FirOversampler[] _ups = Array.Empty<FirOversampler>();
    private FirOversampler[] _downs = Array.Empty<FirOversampler>();
    private FirOversampler[] _tpGuards = Array.Empty<FirOversampler>();
    private float[] _mono = Array.Empty<float>();
    private float[] _upInterleaved = Array.Empty<float>();
    private float[] _upMono = Array.Empty<float>();
    private float[] _dn = Array.Empty<float>();
    private int _preparedFactor = -1;
    private int _preparedMaxFrames;
    private bool _tpGuardPrepared;

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Preset",
            Array.ConvertAll(MasteringPresetBank.LimiterPresets, p => p.Name),
            () => MasteringPresetIndex, v => MasteringPresetIndex = v,
            Array.ConvertAll(MasteringPresetBank.LimiterPresets, p => p.Description)),
        new FloatParameter("Threshold", -24, 0, () => ThresholdDb, v => ThresholdDb = v, "0.#", "dB"),
        new FloatParameter("Ceiling", -24, 0, () => CeilingDb, v => CeilingDb = v, "0.#", "dB")
            { Group = "Delivery" },
        new FloatParameter("Release", 1, 500, () => ReleaseMs, v => ReleaseMs = v, "0", "ms", 2.0),
        new BoolParameter("Spectral", () => SpectralLimiter, v => SpectralLimiter = v)
            { Group = "Delivery" },
        new ChoiceParameter("Oversample", new[] { "1× (sample)", "2×", "4×" },
            () => OversampleIndex, v => OversampleIndex = v)
            { Group = "Delivery" }
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _follower.Reset();
        _spectralFollower.Reset();
        EnsureOversamplers(OversampleIndex switch { 2 => 4, 1 => 2, _ => 1 }, 4096);
        EnsureTruePeakGuards();
    }

    private void EnsureOversamplers(int factor, int maxFrames)
    {
        var channels = Math.Max(1, _channels);
        if (factor == _preparedFactor && maxFrames <= _preparedMaxFrames
            && _ups.Length == channels) return;
        _preparedFactor = factor;
        _preparedMaxFrames = maxFrames;
        if (_ups.Length != channels)
        {
            _ups = new FirOversampler[channels];
            _downs = new FirOversampler[channels];
            for (var c = 0; c < channels; c++)
            {
                _ups[c] = new FirOversampler();
                _downs[c] = new FirOversampler();
            }
        }
        for (var c = 0; c < channels; c++)
        {
            _ups[c].Prepare(factor, maxFrames);
            _downs[c].Prepare(factor, maxFrames);
        }
    }

    private void EnsureTruePeakGuards()
    {
        var channels = Math.Max(1, _channels);
        if (_tpGuardPrepared && _tpGuards.Length == channels) return;
        _tpGuards = new FirOversampler[channels];
        for (var c = 0; c < channels; c++)
        {
            _tpGuards[c] = new FirOversampler();
            _tpGuards[c].Prepare(4, 8192);
        }
        _tpGuardPrepared = true;
    }

    public IAudioEffect Clone() => new PeakLimiterEffect
    {
        Enabled = Enabled, ThresholdDb = ThresholdDb, CeilingDb = CeilingDb,
        ReleaseMs = ReleaseMs, SpectralLimiter = SpectralLimiter,
        MasteringPresetIndex = MasteringPresetIndex, OversampleIndex = OversampleIndex
    };

    public void Process(Span<float> buffer)
    {
        if (!Enabled) return;
        ApplyPresetIfChanged();
        var factor = OversampleIndex switch { 2 => 4, 1 => 2, _ => 1 };
        if (factor == 1)
        {
            ProcessRate(buffer, _sampleRate);
            var ch1 = _channels < 1 ? 1 : _channels;
            var frames1 = buffer.Length / ch1;
            EnforceTruePeakCeiling(buffer, frames1, ch1, (float)AudioMath.Db2Lin(CeilingDb));
            return;
        }

        var ch = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / ch;
        EnsureOversamplers(factor, Math.Max(frames, 64));
        if (_mono.Length < frames) _mono = new float[frames];
        if (_dn.Length < frames) _dn = new float[frames];
        var upFrames = frames * factor;
        var upInterleavedLen = upFrames * ch;
        if (_upInterleaved.Length < upInterleavedLen) _upInterleaved = new float[upInterleavedLen];
        if (_upMono.Length < upFrames) _upMono = new float[upFrames];

        // Upsample each channel into one interleaved high-rate block. Per-channel FIR state is
        // independent; mono scratch is reused so surround channel count does not multiply buffers.
        for (var c = 0; c < ch; c++)
        {
            for (var f = 0; f < frames; f++)
                _mono[f] = buffer[f * ch + c];
            _ups[c].Upsample(_mono.AsSpan(0, frames), _upMono.AsSpan(0, upFrames));
            for (var f = 0; f < upFrames; f++)
                _upInterleaved[f * ch + c] = _upMono[f];
        }

        ProcessRate(_upInterleaved.AsSpan(0, upInterleavedLen), _sampleRate * factor);

        var ceiling = (float)AudioMath.Db2Lin(CeilingDb);
        for (var c = 0; c < ch; c++)
        {
            for (var f = 0; f < upFrames; f++)
                _upMono[f] = _upInterleaved[f * ch + c];
            _downs[c].Downsample(_upMono.AsSpan(0, upFrames), _dn.AsSpan(0, frames));
            for (var f = 0; f < frames; f++)
                buffer[f * ch + c] = Math.Clamp(_dn[f], -ceiling, ceiling);
        }

        // ISP / FIR ring guard: downsample after oversampled limiting can still ring above the
        // ceiling; always enforce. At 4× the process path already paid FIR cost once; the guard
        // only runs PeakAfterUpsample (no down-filter) and early-outs when comfortably under.
        EnforceTruePeakCeiling(buffer, frames, ch, ceiling);
    }

    private void EnforceTruePeakCeiling(Span<float> buffer, int frames, int ch, float ceiling)
    {
        // Fast out: if sample peak is well below the ceiling, ISP cannot exceed it by much with
        // this half-band reconstruction — skip the FIR pass.
        float samplePeak = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            var a = MathF.Abs(buffer[i]);
            if (a > samplePeak) samplePeak = a;
        }
        if (samplePeak <= ceiling * 0.92f) return;

        EnsureTruePeakGuards();

        if (_mono.Length < frames) _mono = new float[frames];
        var peak = 0f;
        for (var c = 0; c < ch; c++)
        {
            for (var f = 0; f < frames; f++)
                _mono[f] = buffer[f * ch + c];
            var channelPeak = _tpGuards[c].PeakAfterUpsample(_mono.AsSpan(0, frames));
            if (channelPeak > peak) peak = channelPeak;
        }

        if (peak <= ceiling * 1.001f || peak < 1e-9f) return;
        var scale = ceiling / peak;
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] *= scale;
    }

    private void ProcessRate(Span<float> buffer, double sampleRate)
    {
        var ch = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / ch;
        _follower.SetTimes(0.5, ReleaseMs, sampleRate);
        _spectralFollower.SetTimes(2.0, ReleaseMs * 2.5, sampleRate);
        var inGain = (float)AudioMath.Db2Lin(-ThresholdDb);
        var ceiling = (float)AudioMath.Db2Lin(CeilingDb);
        var grPeak = 0.0;
        float inputPeak = 0, outputPeak = 0;

        for (var f = 0; f < frames; f++)
        {
            float peak = 0;
            for (var c = 0; c < ch; c++)
            {
                var a = MathF.Abs(buffer[f * ch + c] * inGain);
                inputPeak = Math.Max(inputPeak, a);
                if (a > peak) peak = a;
            }

            if (SpectralLimiter)
            {
                var slow = (float)_spectralFollower.Process(peak);
                if (slow > peak) peak = slow;
            }

            var env = (float)_follower.Process(peak);
            var grDb = env > 1e-9 ? 20.0 * Math.Log10(ceiling / env) : 0.0;
            if (grDb < grPeak) grPeak = grDb;
            var gain = env > 1e-9f ? ceiling / env : 1f;

            for (var c = 0; c < ch; c++)
            {
                var i = f * ch + c;
                var x = buffer[i] * inGain * gain;
                buffer[i] = Math.Clamp(x, -ceiling, ceiling);
                outputPeak = Math.Max(outputPeak, Math.Abs(buffer[i]));
            }
        }

        GainReductionDb = grPeak;
        InputPeak = inputPeak;
        OutputPeak = outputPeak;
    }

    private void ApplyPresetIfChanged()
    {
        if (MasteringPresetIndex == _lastPresetIndex) return;
        _lastPresetIndex = MasteringPresetIndex;
        var preset = MasteringPresetBank.GetLimiter(MasteringPresetIndex);
        ThresholdDb = preset.ThresholdDb;
        CeilingDb = preset.CeilingDb;
        ReleaseMs = preset.ReleaseMs;
        SpectralLimiter = preset.Spectral;
    }
}
