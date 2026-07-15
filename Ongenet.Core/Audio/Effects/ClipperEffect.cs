using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A transparent mastering soft clipper / saturator. Sitting just before the brickwall limiter, it
/// shaves the absolute microscopic peaks of the waveform cleanly (a smooth tanh curve that stays
/// near-linear for the bulk of the signal and asymptotes toward the ceiling), taming sharp transient
/// drums so the final limiter has far less work to do — letting the track sit louder without pumping.
/// Drive is gentle (1–2 dB); the ceiling keeps a small safety margin. Optional 2×/4× FIR oversampling
/// processes the nonlinearity at the high rate for improved ISP control into the following limiter.
/// </summary>
public sealed class ClipperEffect : IAudioEffect
{
    public const string TypeId = "clipper";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    /// <summary>Input drive (dB) pushed into the soft-clip curve.</summary>
    public double DriveDb { get; set; } = 1.5;

    /// <summary>The soft ceiling (dBFS) the waveform asymptotes toward.</summary>
    public double CeilingDb { get; set; } = -0.3;

    /// <summary>Oversampling factor: 0 = 1× (sample-peak), 1 = 2×, 2 = 4×.</summary>
    public int OversampleIndex { get; set; } = 1;

    public string Name => "Clipper";

    private int _channels = 2;
    private FirOversampler[] _ups = Array.Empty<FirOversampler>();
    private FirOversampler[] _downs = Array.Empty<FirOversampler>();
    private float[] _mono = Array.Empty<float>();
    private float[] _up = Array.Empty<float>();
    private float[] _dn = Array.Empty<float>();
    private int _preparedFactor = -1;
    private int _preparedMaxFrames;
    private readonly float[] _recent = new float[128];
    private int _recentWrite;
    public float InputPeak { get; private set; }
    public float OutputPeak { get; private set; }

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Drive", 0.0, 12.0, () => DriveDb, v => DriveDb = v, "0.#", "dB"),
        new FloatParameter("Ceiling", -6.0, 0.0, () => CeilingDb, v => CeilingDb = v, "0.#", "dB"),
        new ChoiceParameter("Oversample", new[] { "1× (sample)", "2×", "4×" },
            () => OversampleIndex, v => OversampleIndex = v)
    };

    public void Prepare(AudioFormat format)
    {
        _channels = format.Channels < 1 ? 1 : format.Channels;
        EnsureOversamplers(OversampleIndex switch { 2 => 4, 1 => 2, _ => 1 }, 4096);
    }

    private void EnsureOversamplers(int factor, int maxFrames)
    {
        if (factor == _preparedFactor && maxFrames <= _preparedMaxFrames
            && _ups.Length == Math.Max(1, _channels)) return;
        _preparedFactor = factor;
        _preparedMaxFrames = maxFrames;
        var channels = Math.Max(1, _channels);
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

    public IAudioEffect Clone() => new ClipperEffect
    {
        Enabled = Enabled, DriveDb = DriveDb, CeilingDb = CeilingDb, OversampleIndex = OversampleIndex
    };

    public void Process(Span<float> buffer)
    {
        if (!Enabled) return;
        float inputPeak = 0;
        for (var i = 0; i < buffer.Length; i++) inputPeak = Math.Max(inputPeak, Math.Abs(buffer[i]));
        InputPeak = inputPeak;
        var drive = (float)AudioMath.Db2Lin(DriveDb);
        var ceiling = (float)AudioMath.Db2Lin(CeilingDb);
        if (ceiling <= 1e-6f) ceiling = 1e-6f;

        var factor = OversampleIndex switch { 2 => 4, 1 => 2, _ => 1 };
        if (factor == 1)
        {
            for (var i = 0; i < buffer.Length; i++)
                buffer[i] = SoftClip(buffer[i], drive, ceiling);
            CaptureOutput(buffer);
            return;
        }

        var ch = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / ch;
        EnsureOversamplers(factor, Math.Max(frames, 64));
        if (_mono.Length < frames) _mono = new float[frames];
        if (_dn.Length < frames) _dn = new float[frames];
        var upLen = frames * factor;
        if (_up.Length < upLen) _up = new float[upLen];

        // Process every channel independently. Scratch arrays are shared sequentially while each
        // channel retains independent FIR history in its oversampler pair.
        for (var c = 0; c < ch; c++)
        {
            for (var f = 0; f < frames; f++)
                _mono[f] = buffer[f * ch + c];
            _ups[c].Upsample(_mono.AsSpan(0, frames), _up.AsSpan(0, upLen));
            for (var i = 0; i < upLen; i++)
                _up[i] = SoftClip(_up[i], drive, ceiling);
            _downs[c].Downsample(_up.AsSpan(0, upLen), _dn.AsSpan(0, frames));
            for (var f = 0; f < frames; f++)
                buffer[f * ch + c] = SoftClip(_dn[f], 1f, ceiling); // re-enforce after FIR ring
        }
        CaptureOutput(buffer);
    }

    public int CaptureRecent(float[] destination)
    {
        var count = Math.Min(destination.Length, _recent.Length);
        var write = _recentWrite;
        for (var i = 0; i < count; i++)
            destination[i] = _recent[(write - count + i + _recent.Length) % _recent.Length];
        return count;
    }

    private void CaptureOutput(ReadOnlySpan<float> buffer)
    {
        float peak = 0;
        var step = Math.Max(1, buffer.Length / 32);
        for (var i = 0; i < buffer.Length; i++)
        {
            peak = Math.Max(peak, Math.Abs(buffer[i]));
            if (i % step == 0)
            {
                _recent[_recentWrite] = buffer[i];
                _recentWrite = (_recentWrite + 1) % _recent.Length;
            }
        }
        OutputPeak = peak;
    }

    private static float SoftClip(float x, float drive, float ceiling)
    {
        var z = x * drive / ceiling;
        return (float)Math.Tanh(z) * ceiling;
    }
}
