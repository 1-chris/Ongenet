using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Modules;

/// <summary>
/// Wraps an existing <see cref="IAudioEffect"/> (chorus, phaser, bitcrusher, …) as a rack module,
/// reusing its DSP and parameter list verbatim. A curve assigned to the module drives one "headline"
/// parameter (typically the effect's wet mix) so it can be faded/automated over a gesture.
/// </summary>
public sealed class EffectBackedModule : FxModule
{
    private readonly IAudioEffect _fx;
    private readonly Func<IAudioEffect, double> _getHeadline;
    private readonly Action<IAudioEffect, double> _setHeadline;
    private AudioFormat _format;

    public EffectBackedModule(string id, string name, IAudioEffect fx,
        Func<IAudioEffect, double> getHeadline, Action<IAudioEffect, double> setHeadline)
    {
        Id = id;
        Name = name;
        _fx = fx;
        _getHeadline = getHeadline;
        _setHeadline = setHeadline;
    }

    public override string Id { get; }
    public override string Name { get; }
    public override IReadOnlyList<Parameter> Parameters => _fx.Parameters;

    public override void Prepare(AudioFormat format) { _format = format; _fx.Prepare(format); }
    public override void Reset() => _fx.Prepare(_format);

    public override void Process(Span<float> buffer)
    {
        _setHeadline(_fx, Amount(_getHeadline(_fx)));
        _fx.Process(buffer);
    }

    public override FxModule Clone()
        => new EffectBackedModule(Id, Name, _fx.Clone(), _getHeadline, _setHeadline) { Enabled = Enabled };
}

/// <summary>A varispeed tape-stop module (per-channel <see cref="TapeStopProcessor"/>). The curve (or the
/// static "Stop" amount) drives 0 = real time → 1 = halted.</summary>
public sealed class TapeStopModule : FxModule
{
    public const string ModuleId = "tapestop";

    public double Stop { get; set; }

    private int _channels = 2;
    private TapeStopProcessor[] _proc = Array.Empty<TapeStopProcessor>();
    private IReadOnlyList<Parameter>? _parameters;

    public override string Id => ModuleId;
    public override string Name => "Tape Stop";

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Stop", 0.0, 1.0, () => Stop, v => Stop = v, "0%", "", 1.0)
    };

    public override void Prepare(AudioFormat format)
    {
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _proc = new TapeStopProcessor[_channels];
        for (var c = 0; c < _channels; c++) { _proc[c] = new TapeStopProcessor(); _proc[c].Prepare(format.SampleRate); }
    }

    public override void Reset() { foreach (var p in _proc) p.Reset(); }

    public override void Process(Span<float> buffer)
    {
        if (_proc.Length == 0) return;
        var stop = Amount(Stop);
        var channels = _channels;
        var frames = buffer.Length / channels;
        for (var f = 0; f < frames; f++)
        {
            var i = f * channels;
            for (var c = 0; c < channels; c++) buffer[i + c] = _proc[c].Process(buffer[i + c], stop);
        }
    }

    public override FxModule Clone() => new TapeStopModule { Enabled = Enabled, Stop = Stop };
}

/// <summary>A stereo feedback comb (metallic resonance / "zaag") built on <see cref="CombFilter"/>.
/// The curve drives the wet mix.</summary>
public sealed class CombModule : FxModule
{
    public const string ModuleId = "comb";

    public double DelayMs { get; set; } = 8.0;
    public double Feedback { get; set; } = 0.7;
    public double Stereo { get; set; } = 0.3;
    public double Mix { get; set; } = 0.5;

    private readonly CombFilter _comb = new();
    private int _channels = 2;
    private int _sampleRate = 44100;
    private IReadOnlyList<Parameter>? _parameters;

    public override string Id => ModuleId;
    public override string Name => "Comb";

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Delay", 0.5, 30.0, () => DelayMs, v => DelayMs = v, "0.0", "ms", 2.0),
        new FloatParameter("Feedback", 0.0, 0.9, () => Feedback, v => Feedback = v),
        new FloatParameter("Stereo", 0.0, 0.5, () => Stereo, v => Stereo = v),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v)
    };

    public override void Prepare(AudioFormat format)
    {
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100;
        _comb.Reset();
    }

    public override void Reset() => _comb.Reset();

    public override void Process(Span<float> buffer)
    {
        var channels = _channels;
        _comb.Configure(DelayMs, Stereo, Feedback, Amount(Mix), _sampleRate);
        var frames = buffer.Length / channels;
        for (var f = 0; f < frames; f++)
        {
            var i = f * channels;
            var l = buffer[i];
            var r = channels >= 2 ? buffer[i + 1] : l;
            _comb.Process(l, r, out var ol, out var or);
            buffer[i] = ol;
            if (channels >= 2) buffer[i + 1] = or;
        }
    }

    public override FxModule Clone()
        => new CombModule { Enabled = Enabled, DelayMs = DelayMs, Feedback = Feedback, Stereo = Stereo, Mix = Mix };
}

/// <summary>A resonant low-pass (RBJ biquad). The curve sweeps the cutoff (mapped 0..1 → 20 Hz..20 kHz,
/// logarithmic); without a curve the static cutoff applies.</summary>
public sealed class LowPassModule : FxModule
{
    public const string ModuleId = "lowpass";

    public double Cutoff { get; set; } = 8000.0;
    public double Resonance { get; set; } = 0.9;

    private Biquad[] _bq = Array.Empty<Biquad>();
    private int _channels = 2;
    private int _sampleRate = 44100;
    private IReadOnlyList<Parameter>? _parameters;

    public override string Id => ModuleId;
    public override string Name => "Low-Pass";

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Cutoff", 20.0, 20000.0, () => Cutoff, v => Cutoff = v, "0", "Hz", 3.0),
        new FloatParameter("Resonance", 0.3, 12.0, () => Resonance, v => Resonance = v, "0.0", "", 2.0)
    };

    public override void Prepare(AudioFormat format)
    {
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100;
        _bq = new Biquad[_channels];
    }

    public override void Reset() { for (var c = 0; c < _bq.Length; c++) _bq[c].Reset(); }

    public override void Process(Span<float> buffer)
    {
        if (_bq.Length == 0) return;
        var channels = _channels;
        // A curve maps 0..1 → 20 Hz..20 kHz (log); otherwise use the static cutoff knob.
        var cutoff = ModulationOverride is { } m ? 20.0 * Math.Pow(1000.0, Math.Clamp(m, 0, 1)) : Cutoff;
        var coeffs = BiquadCoefficients.Compute(FilterMode.LowPass, cutoff, Resonance, _sampleRate);
        var frames = buffer.Length / channels;
        for (var f = 0; f < frames; f++)
        {
            var i = f * channels;
            for (var c = 0; c < channels; c++)
                buffer[i + c] = (float)_bq[c].Process(coeffs, buffer[i + c]);
        }
    }

    public override FxModule Clone()
        => new LowPassModule { Enabled = Enabled, Cutoff = Cutoff, Resonance = Resonance };
}
