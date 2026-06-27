using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A pass-through "scope" effect: it never alters the audio, it only taps it so a 3D waveform
/// visualization can display the signal flowing through this point in the chain. The visual itself lives
/// in the UI (a reusable Engine3D control); this effect just exposes the captured waveform via
/// <see cref="IWaveformSource"/>. A demo of the engine's GPU 3D controls.
/// </summary>
public sealed class WaveformVisualizerEffect : IAudioEffect, IWaveformSource
{
    public const string TypeId = "waveform-visualizer";

    private readonly SpectrumScope _scope = new();
    private int _channels = 2;
    private int _sampleRate = 44100;

    string IAudioEffect.TypeId => TypeId;
    public string Name => "3D Scope";
    public bool Enabled { get; set; } = true;

    // No audio parameters - the effect is purely a visual tap and must not modify the signal.
    public IReadOnlyList<Parameter> Parameters { get; } = Array.Empty<Parameter>();

    public int SampleRate => _sampleRate;

    public void Prepare(AudioFormat format)
    {
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100;
    }

    public void Process(Span<float> buffer)
    {
        // Pass-through: tap the signal for the display, leave the audio untouched.
        _scope.Tap(buffer, _channels);
    }

    public int CaptureLatest(float[] dest) => _scope.CaptureLatest(dest);

    public IAudioEffect Clone() => new WaveformVisualizerEffect { Enabled = Enabled };
}
