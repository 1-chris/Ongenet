using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Pass-through combined analyser tap: spectrum, waveform, and peak meters (Wave Candy-style panel).
/// Audio is unchanged; latest analysis is exposed via <see cref="ISpectrumSource"/>,
/// <see cref="IWaveformSource"/>, and <see cref="IAudioAnalyzerSource"/>.
/// </summary>
public sealed class SpectrumEffect : IAudioEffect, ISpectrumSource, IWaveformSource, IAudioAnalyzerSource
{
    public const string TypeId = "spectrum";

    string IAudioEffect.TypeId => TypeId;

    private readonly SpectrumScope _scope = new();
    private readonly AudioAnalyzer _analyzer = new();
    private int _channels = 2;
    private int _sampleRate = 44100;

    public bool Enabled { get; set; } = true;

    public string Name => "Spectrum";

    public int SampleRate => _sampleRate;

    public float PeakLeft => _analyzer.PeakLeft;
    public float PeakRight => _analyzer.PeakRight;
    public float Rms => _analyzer.Rms;
    public float Correlation => _analyzer.Correlation;
    public float PhaseDegrees => _analyzer.PhaseDegrees;

    public IReadOnlyList<Parameter> Parameters { get; } = Array.Empty<Parameter>();

    public void Prepare(AudioFormat format)
    {
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100;
    }

    public IAudioEffect Clone() => new SpectrumEffect { Enabled = Enabled };

    public void Process(Span<float> buffer)
    {
        var ch = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / ch;
        for (var f = 0; f < frames; f++)
        {
            var l = ch > 0 ? buffer[f * ch] : 0f;
            var r = ch > 1 ? buffer[f * ch + 1] : l;
            _analyzer.ProcessFrame(l, r);
        }
        _analyzer.CommitBlock();
        _scope.Tap(buffer, _channels);
    }

    public int CaptureLatest(float[] dest) => _scope.CaptureLatest(dest);
}
