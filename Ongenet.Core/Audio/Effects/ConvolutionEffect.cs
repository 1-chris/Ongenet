using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// FFT convolution reverb via <see cref="ConvolutionReverb"/>. Loads an external mono impulse when
/// provided (<see cref="IImpulseHost"/>); otherwise synthesises a decaying noise burst from Decay/Size.
/// </summary>
public sealed class ConvolutionEffect : IAudioEffect, IImpulseHost, ISampleHost
{
    public const string TypeId = "convolution";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double DecaySeconds { get; set; } = 1.5;
    public double Mix { get; set; } = 0.3;
    public double Size { get; set; } = 0.6;
    /// <summary>0 = procedural default; 1+ selects a factory IR preset from <see cref="ConvolutionIrBank"/>.</summary>
    public int FactoryIrIndex { get; set; }

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private ConvolutionReverb _reverb = new();
    private float[] _scratch = Array.Empty<float>();
    private bool _hasUserImpulse;
    private double _lastDecay = double.NaN, _lastSize = double.NaN, _lastSr = double.NaN;
    private int _lastIr = -1;

    public string Name => "Convolution";

    public string? ImpulseName { get; private set; }
    public AudioSampleBuffer? CurrentImpulse { get; private set; }

    string? ISampleHost.SampleName => ImpulseName;
    AudioSampleBuffer? ISampleHost.CurrentSample => CurrentImpulse;
    void ISampleHost.LoadSample(AudioSampleBuffer sample, string name) => LoadImpulse(sample, name);

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= BuildParameters();

    private IReadOnlyList<Parameter> BuildParameters()
    {
        var irNames = new string[ConvolutionIrBank.PresetNames.Length + 1];
        irNames[0] = "Synthetic";
        Array.Copy(ConvolutionIrBank.PresetNames, 0, irNames, 1, ConvolutionIrBank.PresetNames.Length);
        return new Parameter[]
        {
            new ChoiceParameter("Factory IR", irNames, () => FactoryIrIndex, v => FactoryIrIndex = v),
            new FloatParameter("Decay", 0.1, 4.0, () => DecaySeconds, v => DecaySeconds = v, "0.##", "s"),
            new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v),
            new FloatParameter("Size", 0.0, 1.0, () => Size, v => Size = v)
        };
    }

    public void Prepare(AudioFormat format)
    {
        var sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        var channels = format.Channels < 1 ? 1 : format.Channels;
        var scratch = new float[4096 * 2];
        var reverb = new ConvolutionReverb();
        if (_hasUserImpulse && CurrentImpulse is { } buf)
            reverb.LoadImpulse(ExtractMonoIr(buf));
        else
            ConfigureSynthetic(reverb, sampleRate);

        reverb.Reset();

        // Publish fully-built state with single assignments — RebuildTracks can call Prepare from the UI
        // thread while Process runs on the audio worker pool (e.g. after "Render clip to new track").
        _sampleRate = sampleRate;
        _channels = channels;
        _scratch = scratch;
        _reverb = reverb;
        _lastDecay = double.NaN;
    }

    public void LoadImpulse(AudioSampleBuffer impulse, string name)
    {
        CurrentImpulse = impulse;
        ImpulseName = name;
        _hasUserImpulse = true;
        _reverb.LoadImpulse(ExtractMonoIr(impulse));
    }

    public IAudioEffect Clone()
    {
        var c = new ConvolutionEffect
        {
            Enabled = Enabled, DecaySeconds = DecaySeconds, Mix = Mix, Size = Size,
            FactoryIrIndex = FactoryIrIndex,
            ImpulseName = ImpulseName, _hasUserImpulse = _hasUserImpulse
        };
        if (CurrentImpulse is { } buf)
            c.LoadImpulse(buf, ImpulseName ?? "impulse");
        return c;
    }

    public void Process(Span<float> buffer)
    {
        var reverb = _reverb;
        var scratch = _scratch;
        if (scratch.Length == 0) return;

        if (!_hasUserImpulse &&
            (DecaySeconds != _lastDecay || Size != _lastSize || _sampleRate != _lastSr || FactoryIrIndex != _lastIr))
        {
            ConfigureSynthetic(reverb, _sampleRate);
            _lastDecay = DecaySeconds;
            _lastSize = Size;
            _lastSr = _sampleRate;
            _lastIr = FactoryIrIndex;
        }

        reverb.Mix = (float)Math.Clamp(Mix, 0, 1);

        var channels = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / channels;
        if (frames <= 0) return;

        if (channels >= 2)
        {
            for (var frame = 0; frame < frames; frame++)
            {
                var i = frame * channels;
                scratch[frame * 2] = buffer[i];
                scratch[frame * 2 + 1] = buffer[i + 1];
            }

            reverb.Process(scratch, frames);

            for (var frame = 0; frame < frames; frame++)
            {
                var i = frame * channels;
                var si = frame * 2;
                buffer[i] = scratch[si];
                buffer[i + 1] = scratch[si + 1];
                for (var c = 2; c < channels; c++) buffer[i + c] = 0.5f * (scratch[si] + scratch[si + 1]);
            }
        }
        else
        {
            for (var frame = 0; frame < frames; frame++)
            {
                var sample = buffer[frame];
                scratch[frame * 2] = sample;
                scratch[frame * 2 + 1] = sample;
            }

            reverb.Process(scratch, frames);

            for (var frame = 0; frame < frames; frame++)
                buffer[frame] = scratch[frame * 2];
        }
    }

    private void ConfigureSynthetic(ConvolutionReverb reverb, double sampleRate)
    {
        if (FactoryIrIndex > 0 && !_hasUserImpulse)
        {
            var ir = ConvolutionIrBank.BuildSyntheticIr(sampleRate, FactoryIrIndex - 1, DecaySeconds, Size);
            reverb.LoadImpulse(ir);
            return;
        }

        var decay = Math.Clamp(DecaySeconds, 0.1, 4.0);
        var size = Math.Clamp(Size, 0.0, 1.0);
        var length = decay * (0.35 + 0.65 * size);
        reverb.Configure((int)sampleRate, length);
    }

    private void ConfigureSynthetic() => ConfigureSynthetic(_reverb, _sampleRate);

    private static float[] ExtractMonoIr(AudioSampleBuffer buffer)
    {
        var ch = buffer.Channels < 1 ? 1 : buffer.Channels;
        var frames = buffer.FrameCount;
        if (frames <= 0) return new float[1];

        var src = buffer.Samples;
        var mono = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            double sum = 0;
            for (var c = 0; c < ch; c++)
                sum += src[f * ch + c];
            mono[f] = (float)(sum / ch);
        }

        return mono;
    }
}
