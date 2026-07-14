using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Modulation;

/// <summary>Shared evaluation helpers for registry modulators.</summary>
internal static class ModulatorEval
{
    public static double Clamp01(double v) => Math.Clamp(v, 0, 1);

    public static double RateHz(double rate, bool tempoSync, double bpm)
    {
        if (tempoSync && bpm > 0) return bpm / 60.0 * Math.Max(1e-6, rate);
        return Math.Max(0, rate);
    }

    public static double Phase(double timeSec, double rateHz) =>
        rateHz > 0 ? timeSec * rateHz - Math.Floor(timeSec * rateHz) : 0;

    public static double LfoUnipolar(LfoWave wave, double phase) => (Lfo.Evaluate(wave, phase) + 1.0) * 0.5;

    public static double EnvelopeFollower(Track track, Guid slotId,
        double attackSec, double releaseSec, Dictionary<Guid, float> state)
    {
        if (track is null) return 0;
        var level = state.GetValueOrDefault(slotId);
        var input = Math.Clamp(track.MeterLevel, 0f, 1f);
        var atk = Math.Max(1e-4, attackSec);
        var rel = Math.Max(1e-4, releaseSec);
        const double blockSec = 512.0 / 48000.0;
        var coeff = input > level
            ? 1.0 - Math.Exp(-blockSec / atk)
            : 1.0 - Math.Exp(-blockSec / rel);
        level = (float)(level + (input - level) * coeff);
        state[slotId] = level;
        return level;
    }

    public static uint HashSeed(double t, uint seed)
    {
        var x = (uint)(t * 1000.0) ^ seed;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        return x;
    }

    public static double Hash01(double t, uint seed) => HashSeed(t, seed) / (double)uint.MaxValue;

    public static double SampleHold(double timeSec, double rateHz, uint seed)
    {
        if (rateHz <= 0) return 0.5;
        var idx = Math.Floor(timeSec * rateHz);
        return Hash01(idx, seed);
    }

    public static double StepIndex(double beat, double stepsPerBeat, int stepCount)
    {
        if (stepCount <= 0) return 0;
        var idx = (int)Math.Floor(beat * stepsPerBeat) % stepCount;
        if (idx < 0) idx += stepCount;
        return idx / (double)(stepCount - 1 <= 0 ? 1 : stepCount - 1);
    }
}

public abstract class ModulatorBase : IModulator
{
    public bool Enabled { get; set; } = true;
    public abstract string Name { get; }
    public abstract string TypeId { get; }
    public abstract IReadOnlyList<Parameter> Parameters { get; }
    public abstract double Evaluate(ModulatorContext ctx);
    public abstract IModulator Clone();
}

// ── Envelopes ────────────────────────────────────────────────────────────────

public sealed class FourStageModulator : ModulatorBase
{
    public const string ModId = "mod.4stage";
    public override string TypeId => ModId;
    public override string Name => "4-Stage";
    public double Attack { get; set; } = 0.01;
    public double Hold { get; set; } = 0.05;
    public double Decay { get; set; } = 0.3;
    public double Curve { get; set; } = 0.5;
    public double Rate { get; set; } = 0.25;
    public bool TempoSync { get; set; } = true;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var hz = ModulatorEval.RateHz(Rate, TempoSync, ctx.Bpm);
        var period = hz > 0 ? 1.0 / hz : 1.0;
        var t = ctx.TimeSec % period;
        var env = new CurveEnvelope(0, Attack, Hold, Decay, Curve);
        return ModulatorEval.Clamp01(env.Evaluate(t));
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class AdsrModulator : ModulatorBase
{
    public const string ModId = "mod.adsr";
    public override string TypeId => ModId;
    public override string Name => "ADSR";
    public double Attack { get; set; } = 0.01;
    public double Decay { get; set; } = 0.2;
    public double Sustain { get; set; } = 0.7;
    public double Release { get; set; } = 0.3;
    public double CycleBeats { get; set; } = 4;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var cycleSec = CycleBeats * 60.0 / (ctx.Bpm > 0 ? ctx.Bpm : 120.0);
        var t = ctx.TimeSec % Math.Max(0.01, cycleSec);
        var env = new CurveEnvelope(0, Attack, 0, Decay, 0.6);
        var a = ModulatorEval.Clamp01(env.Evaluate(t));
        if (t > Attack + Decay) a = Sustain;
        if (t > cycleSec - Release) a *= (cycleSec - t) / Math.Max(1e-4, Release);
        return ModulatorEval.Clamp01(a);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class AhdOnReleaseModulator : ModulatorBase
{
    public const string ModId = "mod.ahd_release";
    public override string TypeId => ModId;
    public override string Name => "AHD on Release";
    public double Attack { get; set; } = 0.005;
    public double Hold { get; set; } = 0.1;
    public double Decay { get; set; } = 0.4;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var beatPhase = ctx.Beat - Math.Floor(ctx.Beat / 4) * 4;
        var env = new CurveEnvelope(0, Attack, Hold, Decay, 0.65);
        return ModulatorEval.Clamp01(env.Evaluate(beatPhase * 0.5));
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class AhdsrModulator : ModulatorBase
{
    public const string ModId = "mod.ahdsr";
    public override string TypeId => ModId;
    public override string Name => "AHDSR";
    public double Attack { get; set; } = 0.01;
    public double Hold { get; set; } = 0.05;
    public double Decay { get; set; } = 0.2;
    public double Sustain { get; set; } = 0.6;
    public double Release { get; set; } = 0.25;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var env = new DahdsrEnvelope
        {
            AttackSeconds = Attack, HoldSeconds = Hold, DecaySeconds = Decay,
            SustainLevel = Sustain, ReleaseSeconds = Release
        };
        env.SetSampleRate(48000);
        var beatPhase = ctx.Beat % 4.0;
        if (beatPhase < 0.05) env.Gate();
        else if (beatPhase > 3.5) env.Release();
        var samples = (int)(ctx.TimeSec * 48000) % 512;
        for (var i = 0; i < samples; i++) env.Process();
        return ModulatorEval.Clamp01(env.Level);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class CurvesModulator : ModulatorBase
{
    public const string ModId = "mod.curves";
    public override string TypeId => ModId;
    public override string Name => "Curves";
    public double Delay { get; set; }
    public double Attack { get; set; } = 0.01;
    public double Hold { get; set; }
    public double Decay { get; set; } = 0.5;
    public double Curve { get; set; } = 0.7;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var env = new CurveEnvelope(Delay, Attack, Hold, Decay, Curve);
        var t = ctx.TimeSec % Math.Max(0.01, env.TotalSeconds + 0.5);
        return ModulatorEval.Clamp01(env.Evaluate(t));
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class EnvelopeFollowerModulator : ModulatorBase
{
    public const string ModId = "mod.envelope_follower";
    public override string TypeId => ModId;
    public override string Name => "Envelope Follower";
    public double Attack { get; set; } = 0.01;
    public double Release { get; set; } = 0.2;

    internal static readonly Dictionary<Guid, float> State = new();

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx) =>
        ModulatorEval.EnvelopeFollower(ctx.Track, ctx.SlotId, Attack, Release, State);
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class RampModulator : ModulatorBase
{
    public const string ModId = "mod.ramp";
    public override string TypeId => ModId;
    public override string Name => "Ramp";
    public double PeriodBeats { get; set; } = 4;
    public bool Reverse { get; set; }

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var phase = ctx.Beat / Math.Max(1e-4, PeriodBeats);
        phase -= Math.Floor(phase);
        if (Reverse) phase = 1.0 - phase;
        return ModulatorEval.Clamp01(phase);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class SegmentsModulator : ModulatorBase
{
    public const string ModId = "mod.segments";
    public override string TypeId => ModId;
    public override string Name => "Segments";
    public double[] Levels { get; set; } = { 0, 0.5, 1, 0.5, 0 };
    public double Rate { get; set; } = 1;
    public bool TempoSync { get; set; } = true;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var levels = Levels;
        if (levels.Length == 0) return 0;
        var hz = ModulatorEval.RateHz(Rate, TempoSync, ctx.Bpm);
        var phase = ModulatorEval.Phase(ctx.TimeSec, hz);
        var idx = (int)(phase * levels.Length) % levels.Length;
        return ModulatorEval.Clamp01(levels[idx]);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

// ── LFOs ─────────────────────────────────────────────────────────────────────

public sealed class LfoModulator : ModulatorBase
{
    public const string ModId = "mod.lfo";
    public override string TypeId => ModId;
    public override string Name => "LFO";
    public double Rate { get; set; } = 0.25;
    public bool TempoSync { get; set; } = true;
    public LfoWave Wave { get; set; } = LfoWave.Sine;
    public double PhaseOffset { get; set; }

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var hz = ModulatorEval.RateHz(Rate, TempoSync, ctx.Bpm);
        var phase = ModulatorEval.Phase(ctx.TimeSec, hz) + PhaseOffset;
        return ModulatorEval.LfoUnipolar(Wave, phase);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class ClassicLfoModulator : ModulatorBase
{
    public const string ModId = "mod.classic_lfo";
    public override string TypeId => ModId;
    public override string Name => "Classic LFO";
    public double Rate { get; set; } = 1;
    public LfoWave Wave { get; set; } = LfoWave.Triangle;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var phase = ModulatorEval.Phase(ctx.TimeSec, Rate);
        return ModulatorEval.Clamp01((Lfo.Evaluate(Wave, phase) + 1.0) * 0.5);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class BeatLfoModulator : ModulatorBase
{
    public const string ModId = "mod.beat_lfo";
    public override string TypeId => ModId;
    public override string Name => "Beat LFO";
    public double RateBeats { get; set; } = 1;
    public LfoWave Wave { get; set; } = LfoWave.Sine;
    public double Shuffle { get; set; }

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var phase = ctx.Beat / Math.Max(1e-4, RateBeats);
        phase -= Math.Floor(phase);
        phase = (phase + Shuffle) % 1.0;
        return ModulatorEval.LfoUnipolar(Wave, phase);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class WavetableLfoModulator : ModulatorBase
{
    public const string ModId = "mod.wavetable_lfo";
    public override string TypeId => ModId;
    public override string Name => "Wavetable LFO";
    public double Rate { get; set; } = 0.5;
    public bool TempoSync { get; set; } = true;
    public int Shape { get; set; }

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var hz = ModulatorEval.RateHz(Rate, TempoSync, ctx.Bpm);
        var phase = ModulatorEval.Phase(ctx.TimeSec, hz);
        var wave = (LfoWave)(Shape % 4);
        var wt = ModulatorEval.LfoUnipolar(wave, phase);
        wt = wt * 0.7 + ModulatorEval.LfoUnipolar(LfoWave.Square, phase * 2) * 0.3;
        return ModulatorEval.Clamp01(wt);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class RandomModulator : ModulatorBase
{
    public const string ModId = "mod.random";
    public override string TypeId => ModId;
    public override string Name => "Random";
    public double Rate { get; set; } = 2;
    public bool TempoSync { get; set; }

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var hz = ModulatorEval.RateHz(Rate, TempoSync, ctx.Bpm);
        return ModulatorEval.SampleHold(ctx.TimeSec, hz, 0xA5A5u);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class SampleHoldModulator : ModulatorBase
{
    public const string ModId = "mod.sample_hold";
    public override string TypeId => ModId;
    public override string Name => "Sample and Hold";
    public double Rate { get; set; } = 4;
    public bool TempoSync { get; set; } = true;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var hz = ModulatorEval.RateHz(Rate, TempoSync, ctx.Bpm);
        return ModulatorEval.SampleHold(ctx.TimeSec, hz, 0x5151u);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class StepsModulator : ModulatorBase
{
    public const string ModId = "mod.steps";
    public override string TypeId => ModId;
    public override string Name => "Steps";
    public int StepCount { get; set; } = 8;
    public double RateBeats { get; set; } = 0.25;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx) =>
        ModulatorEval.StepIndex(ctx.Beat, 1.0 / Math.Max(1e-4, RateBeats), StepCount);
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

// ── Performance ──────────────────────────────────────────────────────────────

public sealed class ButtonModulator : ModulatorBase
{
    public const string ModId = "mod.button";
    public override string TypeId => ModId;
    public override string Name => "Button";
    public bool Pressed { get; set; }

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx) => Pressed ? 1 : 0;
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class ButtonsModulator : ModulatorBase
{
    public const string ModId = "mod.buttons";
    public override string TypeId => ModId;
    public override string Name => "Buttons";
    public int Active { get; set; }
    public double[] Values { get; set; } = { 0, 0.33, 0.66, 1 };

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var idx = Math.Clamp(Active, 0, Values.Length - 1);
        return ModulatorEval.Clamp01(Values[idx]);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class MacroModulator : ModulatorBase
{
    public const string ModId = "mod.macro";
    public override string TypeId => ModId;
    public override string Name => "Macro";
    public double Value { get; set; } = 0.5;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx) => ModulatorEval.Clamp01(Value);
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class Macro4Modulator : ModulatorBase
{
    public const string ModId = "mod.macro_4";
    public override string TypeId => ModId;
    public override string Name => "Macro-4";
    public double M1 { get; set; } = 0.25;
    public double M2 { get; set; } = 0.5;
    public double M3 { get; set; } = 0.75;
    public double M4 { get; set; } = 1;
    public int Select { get; set; }

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var vals = new[] { M1, M2, M3, M4 };
        return ModulatorEval.Clamp01(vals[Math.Clamp(Select, 0, 3)]);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class ExpressionsModulator : ModulatorBase
{
    public const string ModId = "mod.expressions";
    public override string TypeId => ModId;
    public override string Name => "Expressions";
    public double Velocity { get; set; } = 0.8;
    public double Timbre { get; set; } = 0.5;
    public double Pressure { get; set; } = 0.5;
    public int Source { get; set; }

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx) => Source switch
    {
        1 => ModulatorEval.Clamp01(Timbre),
        2 => ModulatorEval.Clamp01(Pressure),
        _ => ModulatorEval.Clamp01(Velocity)
    };
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class VoiceControlModulator : ModulatorBase
{
    public const string ModId = "mod.voice_control";
    public override string TypeId => ModId;
    public override string Name => "Voice Control";
    public int VoiceIndex { get; set; }
    public int VoiceCount { get; set; } = 8;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var count = Math.Max(1, VoiceCount);
        return ModulatorEval.Clamp01(VoiceIndex / (double)(count - 1 <= 0 ? 1 : count - 1));
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class VibratoModulator : ModulatorBase
{
    public const string ModId = "mod.vibrato";
    public override string TypeId => ModId;
    public override string Name => "Vibrato";
    public double Rate { get; set; } = 5;
    public double Depth { get; set; } = 0.5;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var phase = ModulatorEval.Phase(ctx.TimeSec, Rate);
        var v = ModulatorEval.LfoUnipolar(LfoWave.Sine, phase);
        return ModulatorEval.Clamp01(0.5 + (v - 0.5) * Depth);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class StackSpreadModulator : ModulatorBase
{
    public const string ModId = "mod.stack_spread";
    public override string TypeId => ModId;
    public override string Name => "Stack Spread";
    public double Spread { get; set; } = 0.5;
    public double Rate { get; set; } = 0.25;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var phase = ModulatorEval.Phase(ctx.TimeSec, Rate);
        return ModulatorEval.Clamp01(phase * Spread + (1 - Spread) * 0.5);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class XyModulator : ModulatorBase
{
    public const string ModId = "mod.xy";
    public override string TypeId => ModId;
    public override string Name => "XY";
    public double X { get; set; } = 0.5;
    public double Y { get; set; } = 0.5;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx) => ModulatorEval.Clamp01(X * Y);
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class GlobalsModulator : ModulatorBase
{
    public const string ModId = "mod.globals";
    public override string TypeId => ModId;
    public override string Name => "Globals";
    public int Source { get; set; }

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx) => Source switch
    {
        1 => ModulatorEval.Clamp01(ctx.Bpm / 200.0),
        _ => ModulatorEval.Clamp01(ctx.Beat % 16 / 16.0)
    };
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

// ── Analysis ─────────────────────────────────────────────────────────────────

public sealed class AudioRateModulator : ModulatorBase
{
    public const string ModId = "mod.audio_rate";
    public override string TypeId => ModId;
    public override string Name => "Audio Rate";
    public double Rate { get; set; } = 440;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var phase = ModulatorEval.Phase(ctx.TimeSec, Rate);
        return ModulatorEval.LfoUnipolar(LfoWave.Sine, phase);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class AudioSidechainModulator : ModulatorBase
{
    public const string ModId = "mod.audio_sidechain";
    public override string TypeId => ModId;
    public override string Name => "Audio Sidechain";
    public double Amount { get; set; } = 1;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx) =>
        ModulatorEval.Clamp01(ctx.Track?.MeterLevel ?? 0) * Amount;
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class NoteSidechainModulator : ModulatorBase
{
    public const string ModId = "mod.note_sidechain";
    public override string TypeId => ModId;
    public override string Name => "Note Sidechain";
    public double Decay { get; set; } = 0.5;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var beatFrac = ctx.Beat - Math.Floor(ctx.Beat);
        return ModulatorEval.Clamp01(1.0 - beatFrac / Math.Max(1e-4, Decay));
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class NoteCounterModulator : ModulatorBase
{
    public const string ModId = "mod.note_counter";
    public override string TypeId => ModId;
    public override string Name => "Note Counter";
    public int Max { get; set; } = 16;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var count = Math.Max(1, Max);
        var n = (int)Math.Floor(ctx.Beat) % count;
        return n / (double)(count - 1 <= 0 ? 1 : count - 1);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

// ── Pitch / key ──────────────────────────────────────────────────────────────

public sealed class KeytrackPlusModulator : ModulatorBase
{
    public const string ModId = "mod.keytrack_plus";
    public override string TypeId => ModId;
    public override string Name => "Keytrack+";
    public int Root { get; set; } = 60;
    public int Range { get; set; } = 24;
    public int Note { get; set; } = 60;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var r = Math.Max(1, Range);
        return ModulatorEval.Clamp01((Note - Root + r * 0.5) / r);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class Pitch12Modulator : ModulatorBase
{
    public const string ModId = "mod.pitch_12";
    public override string TypeId => ModId;
    public override string Name => "Pitch-12";
    public int Note { get; set; } = 60;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var pitchClass = ((Note % 12) + 12) % 12;
        return pitchClass / 11.0;
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class RelativeKeytrackModulator : ModulatorBase
{
    public const string ModId = "mod.relative_keytrack";
    public override string TypeId => ModId;
    public override string Name => "Relative Keytrack";
    public int Center { get; set; } = 60;
    public int Note { get; set; } = 60;
    public double Range { get; set; } = 12;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var r = Math.Max(1, Range);
        return ModulatorEval.Clamp01(0.5 + (Note - Center) / (2 * r));
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class MidiModulator : ModulatorBase
{
    public const string ModId = "mod.midi";
    public override string TypeId => ModId;
    public override string Name => "MIDI";
    public int Cc { get; set; } = 1;
    public double Value { get; set; } = 0.5;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx) => ModulatorEval.Clamp01(Value);
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class HwCvInModulator : ModulatorBase
{
    public const string ModId = "mod.hw_cv_in";
    public override string TypeId => ModId;
    public override string Name => "HW CV In";
    public int Input { get; set; }
    public double Value { get; set; } = 0.5;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx) => ModulatorEval.Clamp01(Value);
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class Channel16Modulator : ModulatorBase
{
    public const string ModId = "mod.channel_16";
    public override string TypeId => ModId;
    public override string Name => "Channel-16";
    public int Channel { get; set; } = 1;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx) =>
        ModulatorEval.Clamp01((Channel - 1) / 15.0);
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

// ── Logic / math ─────────────────────────────────────────────────────────────

public sealed class MathModulator : ModulatorBase
{
    public const string ModId = "mod.math";
    public override string TypeId => ModId;
    public override string Name => "Math";
    public double A { get; set; } = 0.5;
    public double B { get; set; } = 0.5;
    public int Op { get; set; }

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx) => Op switch
    {
        1 => ModulatorEval.Clamp01(A - B),
        2 => ModulatorEval.Clamp01(A * B),
        3 => ModulatorEval.Clamp01(A / Math.Max(1e-6, B)),
        _ => ModulatorEval.Clamp01(A + B)
    };
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class MixModulator : ModulatorBase
{
    public const string ModId = "mod.mix";
    public override string TypeId => ModId;
    public override string Name => "Mix";
    public double A { get; set; } = 0;
    public double B { get; set; } = 1;
    public double Crossfade { get; set; } = 0.5;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx) =>
        ModulatorEval.Clamp01(A * (1 - Crossfade) + B * Crossfade);
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class PolynomModulator : ModulatorBase
{
    public const string ModId = "mod.polynom";
    public override string TypeId => ModId;
    public override string Name => "Polynom";
    public double Input { get; set; } = 0.5;
    public double A { get; set; } = 1;
    public double B { get; set; }
    public double C { get; set; }

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var x = Input;
        return ModulatorEval.Clamp01(A * x * x + B * x + C);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class QuantizeModulator : ModulatorBase
{
    public const string ModId = "mod.quantize";
    public override string TypeId => ModId;
    public override string Name => "Quantize";
    public double Input { get; set; } = 0.5;
    public int Steps { get; set; } = 8;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var steps = Math.Max(1, Steps);
        return Math.Round(Input * steps) / steps;
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class Select4Modulator : ModulatorBase
{
    public const string ModId = "mod.select_4";
    public override string TypeId => ModId;
    public override string Name => "Select-4";
    public double V0 { get; set; }
    public double V1 { get; set; } = 0.33;
    public double V2 { get; set; } = 0.66;
    public double V3 { get; set; } = 1;
    public int Select { get; set; }

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var vals = new[] { V0, V1, V2, V3 };
        return ModulatorEval.Clamp01(vals[Math.Clamp(Select, 0, 3)]);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class Vector4Modulator : ModulatorBase
{
    public const string ModId = "mod.vector_4";
    public override string TypeId => ModId;
    public override string Name => "Vector-4";
    public double X { get; set; } = 0.5;
    public double Y { get; set; } = 0.5;
    public double Z0 { get; set; } = 0.25;
    public double Z1 { get; set; } = 0.75;

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx) =>
        ModulatorEval.Clamp01(X * Y * Z0 + (1 - X) * (1 - Y) * Z1);
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class Vector8Modulator : ModulatorBase
{
    public const string ModId = "mod.vector_8";
    public override string TypeId => ModId;
    public override string Name => "Vector-8";
    public double X { get; set; } = 0.5;
    public double Y { get; set; } = 0.5;
    public double[] Corners { get; set; } = { 0, 0.14, 0.28, 0.42, 0.57, 0.71, 0.85, 1 };

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var c = Corners;
        if (c.Length == 0) return 0;
        var idx = (int)(ModulatorEval.Clamp01(X) * (c.Length - 1));
        var baseVal = c[idx];
        return ModulatorEval.Clamp01(baseVal * Y + (1 - Y) * (1 - baseVal));
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}

public sealed class ParSeq8Modulator : ModulatorBase
{
    public const string ModId = "mod.parseq_8";
    public override string TypeId => ModId;
    public override string Name => "ParSeq-8";
    public double RateBeats { get; set; } = 0.25;
    public double[] Steps { get; set; } = { 1, 0.8, 0.6, 0.4, 0.2, 0.4, 0.6, 0.8 };

    public override IReadOnlyList<Parameter> Parameters => ModulatorParameterFactory.Get(this);
    public override double Evaluate(ModulatorContext ctx)
    {
        var steps = Steps;
        if (steps.Length == 0) return 0;
        var idx = (int)Math.Floor(ctx.Beat / Math.Max(1e-4, RateBeats)) % steps.Length;
        if (idx < 0) idx += steps.Length;
        return ModulatorEval.Clamp01(steps[idx]);
    }
    public override IModulator Clone() => ModulatorCloner.Clone(this);
}
