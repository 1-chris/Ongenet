using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// ITU-R BS.1770 Annex 2 style true-peak meter: 4× FIR oversampling then abs-max per channel.
/// Supports stereo and multichannel (all channels metered; MaxDbTp is the absolute held peak).
/// Allocation-free after <see cref="Prepare"/>. Can report peaks above sample peak (ISP).
/// </summary>
public sealed class TruePeakMeter
{
    private int _channels = 2;
    private FirOversampler[] _ups = Array.Empty<FirOversampler>();
    private float[] _monoScratch = Array.Empty<float>();
    private float[] _peak = Array.Empty<float>();
    private float[] _held = Array.Empty<float>();

    public float PeakLeft => _peak.Length > 0 ? _peak[0] : 0;
    public float PeakRight => _peak.Length > 1 ? _peak[1] : PeakLeft;
    public float PeakLeftDbTp => ToDbTp(PeakLeft);
    public float PeakRightDbTp => ToDbTp(PeakRight);
    public float MaxDbTp
    {
        get
        {
            var max = 0f;
            for (var i = 0; i < _held.Length; i++)
                if (_held[i] > max) max = _held[i];
            return ToDbTp(max);
        }
    }
    public float HeldPeakLeft => _held.Length > 0 ? _held[0] : 0;
    public float HeldPeakRight => _held.Length > 1 ? _held[1] : HeldPeakLeft;

    public void Prepare(AudioFormat format)
    {
        _channels = format.Channels < 1 ? 1 : format.Channels;
        if (_ups.Length != _channels)
        {
            _ups = new FirOversampler[_channels];
            for (var i = 0; i < _channels; i++)
                _ups[i] = new FirOversampler();
        }
        for (var i = 0; i < _channels; i++)
            _ups[i].Prepare(4, 8192);
        if (_peak.Length != _channels) _peak = new float[_channels];
        if (_held.Length != _channels) _held = new float[_channels];
        Reset();
    }

    public void Reset()
    {
        for (var i = 0; i < _ups.Length; i++)
            _ups[i].Reset();
        Array.Clear(_peak);
        Array.Clear(_held);
    }

    public void ResetHeld()
    {
        Array.Clear(_held);
    }

    public void Process(ReadOnlySpan<float> buffer)
    {
        var ch = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / ch;
        if (frames == 0 || _ups.Length < ch) return;
        if (_monoScratch.Length < frames) _monoScratch = new float[frames];

        for (var c = 0; c < ch; c++)
        {
            for (var f = 0; f < frames; f++)
                _monoScratch[f] = buffer[f * ch + c];
            var peak = _ups[c].PeakAfterUpsample(_monoScratch.AsSpan(0, frames));
            _peak[c] = peak;
            if (peak > _held[c]) _held[c] = peak;
        }
    }

    public static float ToDbTp(float linear) =>
        linear <= 1e-10f ? -120f : 20f * MathF.Log10(linear);
}
