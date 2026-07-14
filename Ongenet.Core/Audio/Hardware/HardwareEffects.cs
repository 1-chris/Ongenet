using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Hardware;

/// <summary>External hardware FX send: pass-through locally; routes to HW when supported.</summary>
public sealed class HwFxEffect : IAudioEffect
{
    public const string TypeId = "hw_fx";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;
    public int OutputChannel { get; set; } = 1;
    public double SendLevel { get; set; } = 1.0;

    public string Name => "HW FX";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("MIDI Channel", 1, 16, () => OutputChannel, v => OutputChannel = (int)v, "0"),
        new FloatParameter("Send", 0, 1, () => SendLevel, v => SendLevel = v)
    };

    public void Prepare(AudioFormat format) { }

    public void Process(Span<float> buffer)
    {
        // Graceful no-op: audio passes through unchanged until HW routing is wired.
        if (!HardwareAvailability.IsSupported) return;
        _ = OutputChannel;
        _ = SendLevel;
    }

    public IAudioEffect Clone() => new HwFxEffect
    {
        Enabled = Enabled, OutputChannel = OutputChannel, SendLevel = SendLevel
    };
}

/// <summary>CV input tap: exposes a normalised CV value; audio passes through unchanged.</summary>
public sealed class HwCvInEffect : IAudioEffect
{
    public const string TypeId = "hw_cv_in";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;
    public int Input { get; set; }
    public double Value { get; set; } = 0.5;

    public string Name => "HW CV In";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Input", 0, 7, () => Input, v => Input = (int)v, "0"),
        new FloatParameter("Value", 0, 1, () => Value, v => Value = v)
    };

    public void Prepare(AudioFormat format) { }

    public void Process(Span<float> buffer)
    {
        if (!HardwareAvailability.IsCvSupported) return;
        // Future: poll CV input and update Value from hardware.
    }

    public IAudioEffect Clone() => new HwCvInEffect
    {
        Enabled = Enabled, Input = Input, Value = Value
    };
}

/// <summary>CV output from an audio-sidechain envelope; pass-through when CV is unavailable.</summary>
public sealed class HwCvOutEffect : IAudioEffect
{
    public const string TypeId = "hw_cv_out";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;
    public int Output { get; set; }
    public double Amount { get; set; } = 1.0;

    public string Name => "HW CV Out";

    private int _channels = 2;
    private readonly EnvelopeFollower _follower = new();
    private double _sampleRate = 44100.0;
    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Output", 0, 7, () => Output, v => Output = (int)v, "0"),
        new FloatParameter("Amount", 0, 1, () => Amount, v => Amount = v)
    };

    public void Prepare(AudioFormat format)
    {
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _follower.Reset();
        _follower.SetTimes(1.0, 50.0, _sampleRate);
    }

    public void Process(Span<float> buffer)
    {
        var channels = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            float peak = 0;
            for (var c = 0; c < channels; c++)
            {
                var a = buffer[i + c];
                if (a < 0) a = -a;
                if (a > peak) peak = a;
            }

            _follower.Process(peak);
        }

        if (HardwareAvailability.IsCvSupported)
        {
            // Future: write CV from _follower.Value * Amount to Output.
            _ = Output;
            _ = Amount;
        }
    }

    public IAudioEffect Clone() => new HwCvOutEffect
    {
        Enabled = Enabled, Output = Output, Amount = Amount
    };
}

/// <summary>MIDI/Link clock output stub: pass-through audio; emits clock when supported.</summary>
public sealed class HwClockOutEffect : IAudioEffect
{
    public const string TypeId = "hw_clock_out";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;
    public int OutputChannel { get; set; } = 1;
    public double Division { get; set; } = 1.0;

    public string Name => "HW Clock Out";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("MIDI Channel", 1, 16, () => OutputChannel, v => OutputChannel = (int)v, "0"),
        new FloatParameter("Division", 0.25, 4, () => Division, v => Division = v, "0.##")
    };

    public void Prepare(AudioFormat format) { }

    public void Process(Span<float> buffer)
    {
        if (!HardwareAvailability.IsMidiOutputSupported) return;
        // Future: emit MIDI clock / Link pulses at host tempo.
        _ = OutputChannel;
        _ = Division;
    }

    public IAudioEffect Clone() => new HwClockOutEffect
    {
        Enabled = Enabled, OutputChannel = OutputChannel, Division = Division
    };
}
