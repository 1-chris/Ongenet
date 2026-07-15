using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>2D oscilloscope pass-through visualizer.</summary>
public sealed class OscilloscopeEffect : IAudioEffect, IWaveformSource, IAnalyserOnlyEffect
{
    public const string TypeId = "oscilloscope";

    private readonly SpectrumScope _scope = new();
    private int _channels = 2;
    private int _sampleRate = 44100;

    string IAudioEffect.TypeId => TypeId;
    public string Name => "Oscilloscope";
    public bool Enabled { get; set; } = true;

    public IReadOnlyList<Parameter> Parameters { get; } = Array.Empty<Parameter>();
    public int SampleRate => _sampleRate;

    public void Prepare(AudioFormat format)
    {
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100;
    }

    public void Process(Span<float> buffer) => _scope.Tap(buffer, _channels);

    public int CaptureLatest(float[] dest) => _scope.CaptureLatest(dest);

    public IAudioEffect Clone() => new OscilloscopeEffect { Enabled = Enabled };
}
