using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Modulation;

/// <summary>Builds generic <see cref="Parameter"/> lists for modulators.</summary>
public static class ModulatorParameterFactory
{
    private static readonly string[] Waves = { "Sine", "Triangle", "Saw", "Square" };
    private static readonly string[] MathOps = { "Add", "Subtract", "Multiply", "Divide" };
    private static readonly string[] ExprSources = { "Velocity", "Timbre", "Pressure" };
    private static readonly string[] GlobalSources = { "Beat", "Tempo" };

    public static IReadOnlyList<Parameter> Get(IModulator mod) => mod switch
    {
        FourStageModulator m => new Parameter[]
        {
            new FloatParameter("Attack", 0, 2, () => m.Attack, v => m.Attack = v, "0.###", "s"),
            new FloatParameter("Hold", 0, 2, () => m.Hold, v => m.Hold = v, "0.###", "s"),
            new FloatParameter("Decay", 0, 4, () => m.Decay, v => m.Decay = v, "0.###", "s"),
            new FloatParameter("Curve", 0, 1, () => m.Curve, v => m.Curve = v),
            new FloatParameter("Rate", 0.0625, 8, () => m.Rate, v => m.Rate = v, "0.###", "beats"),
            new BoolParameter("Tempo Sync", () => m.TempoSync, v => m.TempoSync = v)
        },
        AdsrModulator m => new Parameter[]
        {
            new FloatParameter("Attack", 0, 2, () => m.Attack, v => m.Attack = v, "0.###", "s"),
            new FloatParameter("Decay", 0, 4, () => m.Decay, v => m.Decay = v, "0.###", "s"),
            new FloatParameter("Sustain", 0, 1, () => m.Sustain, v => m.Sustain = v),
            new FloatParameter("Release", 0, 4, () => m.Release, v => m.Release = v, "0.###", "s"),
            new FloatParameter("Cycle", 0.25, 16, () => m.CycleBeats, v => m.CycleBeats = v, "0.##", "beats")
        },
        AhdOnReleaseModulator m => new Parameter[]
        {
            new FloatParameter("Attack", 0, 1, () => m.Attack, v => m.Attack = v, "0.###", "s"),
            new FloatParameter("Hold", 0, 2, () => m.Hold, v => m.Hold = v, "0.###", "s"),
            new FloatParameter("Decay", 0, 4, () => m.Decay, v => m.Decay = v, "0.###", "s")
        },
        AhdsrModulator m => new Parameter[]
        {
            new FloatParameter("Attack", 0, 2, () => m.Attack, v => m.Attack = v, "0.###", "s"),
            new FloatParameter("Hold", 0, 2, () => m.Hold, v => m.Hold = v, "0.###", "s"),
            new FloatParameter("Decay", 0, 4, () => m.Decay, v => m.Decay = v, "0.###", "s"),
            new FloatParameter("Sustain", 0, 1, () => m.Sustain, v => m.Sustain = v),
            new FloatParameter("Release", 0, 4, () => m.Release, v => m.Release = v, "0.###", "s")
        },
        CurvesModulator m => new Parameter[]
        {
            new FloatParameter("Delay", 0, 2, () => m.Delay, v => m.Delay = v, "0.###", "s"),
            new FloatParameter("Attack", 0, 2, () => m.Attack, v => m.Attack = v, "0.###", "s"),
            new FloatParameter("Hold", 0, 2, () => m.Hold, v => m.Hold = v, "0.###", "s"),
            new FloatParameter("Decay", 0, 4, () => m.Decay, v => m.Decay = v, "0.###", "s"),
            new FloatParameter("Curve", 0, 1, () => m.Curve, v => m.Curve = v)
        },
        EnvelopeFollowerModulator m => new Parameter[]
        {
            new FloatParameter("Attack", 0.001, 1, () => m.Attack, v => m.Attack = v, "0.###", "s"),
            new FloatParameter("Release", 0.001, 2, () => m.Release, v => m.Release = v, "0.###", "s")
        },
        RampModulator m => new Parameter[]
        {
            new FloatParameter("Period", 0.25, 32, () => m.PeriodBeats, v => m.PeriodBeats = v, "0.##", "beats"),
            new BoolParameter("Reverse", () => m.Reverse, v => m.Reverse = v)
        },
        SegmentsModulator m => new Parameter[]
        {
            new FloatParameter("Rate", 0.0625, 8, () => m.Rate, v => m.Rate = v, "0.###", "beats"),
            new BoolParameter("Tempo Sync", () => m.TempoSync, v => m.TempoSync = v)
        },
        LfoModulator m => new Parameter[]
        {
            new FloatParameter("Rate", 0.0625, 8, () => m.Rate, v => m.Rate = v, "0.###", "beats"),
            new BoolParameter("Tempo Sync", () => m.TempoSync, v => m.TempoSync = v),
            new ChoiceParameter("Wave", Waves, () => (int)m.Wave, i => m.Wave = (LfoWave)i),
            new FloatParameter("Phase", 0, 1, () => m.PhaseOffset, v => m.PhaseOffset = v)
        },
        ClassicLfoModulator m => new Parameter[]
        {
            new FloatParameter("Rate", 0.01, 20, () => m.Rate, v => m.Rate = v, "0.##", "Hz"),
            new ChoiceParameter("Wave", Waves, () => (int)m.Wave, i => m.Wave = (LfoWave)i)
        },
        BeatLfoModulator m => new Parameter[]
        {
            new FloatParameter("Rate", 0.0625, 8, () => m.RateBeats, v => m.RateBeats = v, "0.###", "beats"),
            new ChoiceParameter("Wave", Waves, () => (int)m.Wave, i => m.Wave = (LfoWave)i),
            new FloatParameter("Shuffle", 0, 1, () => m.Shuffle, v => m.Shuffle = v)
        },
        WavetableLfoModulator m => new Parameter[]
        {
            new FloatParameter("Rate", 0.0625, 8, () => m.Rate, v => m.Rate = v, "0.###", "beats"),
            new BoolParameter("Tempo Sync", () => m.TempoSync, v => m.TempoSync = v),
            new FloatParameter("Shape", 0, 3, () => m.Shape, v => m.Shape = (int)v, "0")
        },
        RandomModulator m => new Parameter[]
        {
            new FloatParameter("Rate", 0.0625, 16, () => m.Rate, v => m.Rate = v, "0.###", "beats"),
            new BoolParameter("Tempo Sync", () => m.TempoSync, v => m.TempoSync = v)
        },
        SampleHoldModulator m => new Parameter[]
        {
            new FloatParameter("Rate", 0.0625, 16, () => m.Rate, v => m.Rate = v, "0.###", "beats"),
            new BoolParameter("Tempo Sync", () => m.TempoSync, v => m.TempoSync = v)
        },
        StepsModulator m => new Parameter[]
        {
            new FloatParameter("Steps", 1, 32, () => m.StepCount, v => m.StepCount = (int)v, "0"),
            new FloatParameter("Rate", 0.03125, 4, () => m.RateBeats, v => m.RateBeats = v, "0.###", "beats")
        },
        ButtonModulator m => new Parameter[]
        {
            new BoolParameter("Pressed", () => m.Pressed, v => m.Pressed = v)
        },
        ButtonsModulator m => new Parameter[]
        {
            new FloatParameter("Active", 0, 3, () => m.Active, v => m.Active = (int)v, "0")
        },
        MacroModulator m => new Parameter[]
        {
            new FloatParameter("Value", 0, 1, () => m.Value, v => m.Value = v)
        },
        Macro4Modulator m => new Parameter[]
        {
            new FloatParameter("M1", 0, 1, () => m.M1, v => m.M1 = v),
            new FloatParameter("M2", 0, 1, () => m.M2, v => m.M2 = v),
            new FloatParameter("M3", 0, 1, () => m.M3, v => m.M3 = v),
            new FloatParameter("M4", 0, 1, () => m.M4, v => m.M4 = v),
            new FloatParameter("Select", 0, 3, () => m.Select, v => m.Select = (int)v, "0")
        },
        ExpressionsModulator m => new Parameter[]
        {
            new ChoiceParameter("Source", ExprSources, () => m.Source, v => m.Source = v),
            new FloatParameter("Velocity", 0, 1, () => m.Velocity, v => m.Velocity = v),
            new FloatParameter("Timbre", 0, 1, () => m.Timbre, v => m.Timbre = v),
            new FloatParameter("Pressure", 0, 1, () => m.Pressure, v => m.Pressure = v)
        },
        VoiceControlModulator m => new Parameter[]
        {
            new FloatParameter("Voice", 0, 7, () => m.VoiceIndex, v => m.VoiceIndex = (int)v, "0"),
            new FloatParameter("Count", 1, 16, () => m.VoiceCount, v => m.VoiceCount = (int)v, "0")
        },
        VibratoModulator m => new Parameter[]
        {
            new FloatParameter("Rate", 0.1, 20, () => m.Rate, v => m.Rate = v, "0.##", "Hz"),
            new FloatParameter("Depth", 0, 1, () => m.Depth, v => m.Depth = v)
        },
        StackSpreadModulator m => new Parameter[]
        {
            new FloatParameter("Spread", 0, 1, () => m.Spread, v => m.Spread = v),
            new FloatParameter("Rate", 0.01, 4, () => m.Rate, v => m.Rate = v, "0.##", "Hz")
        },
        XyModulator m => new Parameter[]
        {
            new FloatParameter("X", 0, 1, () => m.X, v => m.X = v),
            new FloatParameter("Y", 0, 1, () => m.Y, v => m.Y = v)
        },
        GlobalsModulator m => new Parameter[]
        {
            new ChoiceParameter("Source", GlobalSources, () => m.Source, v => m.Source = v)
        },
        AudioRateModulator m => new Parameter[]
        {
            new FloatParameter("Rate", 20, 2000, () => m.Rate, v => m.Rate = v, "0", "Hz")
        },
        AudioSidechainModulator m => new Parameter[]
        {
            new FloatParameter("Amount", 0, 1, () => m.Amount, v => m.Amount = v)
        },
        NoteSidechainModulator m => new Parameter[]
        {
            new FloatParameter("Decay", 0.05, 4, () => m.Decay, v => m.Decay = v, "0.##", "beats")
        },
        NoteCounterModulator m => new Parameter[]
        {
            new FloatParameter("Max", 1, 64, () => m.Max, v => m.Max = (int)v, "0")
        },
        KeytrackPlusModulator m => new Parameter[]
        {
            new FloatParameter("Root", 0, 127, () => m.Root, v => m.Root = (int)v, "0"),
            new FloatParameter("Range", 1, 48, () => m.Range, v => m.Range = (int)v, "0", "st"),
            new FloatParameter("Note", 0, 127, () => m.Note, v => m.Note = (int)v, "0")
        },
        Pitch12Modulator m => new Parameter[]
        {
            new FloatParameter("Note", 0, 127, () => m.Note, v => m.Note = (int)v, "0")
        },
        RelativeKeytrackModulator m => new Parameter[]
        {
            new FloatParameter("Center", 0, 127, () => m.Center, v => m.Center = (int)v, "0"),
            new FloatParameter("Note", 0, 127, () => m.Note, v => m.Note = (int)v, "0"),
            new FloatParameter("Range", 1, 24, () => m.Range, v => m.Range = v, "0", "st")
        },
        MidiModulator m => new Parameter[]
        {
            new FloatParameter("CC", 0, 127, () => m.Cc, v => m.Cc = (int)v, "0"),
            new FloatParameter("Value", 0, 1, () => m.Value, v => m.Value = v)
        },
        HwCvInModulator m => new Parameter[]
        {
            new FloatParameter("Input", 0, 7, () => m.Input, v => m.Input = (int)v, "0"),
            new FloatParameter("Value", 0, 1, () => m.Value, v => m.Value = v)
        },
        Channel16Modulator m => new Parameter[]
        {
            new FloatParameter("Channel", 1, 16, () => m.Channel, v => m.Channel = (int)v, "0")
        },
        MathModulator m => new Parameter[]
        {
            new FloatParameter("A", 0, 1, () => m.A, v => m.A = v),
            new FloatParameter("B", 0, 1, () => m.B, v => m.B = v),
            new ChoiceParameter("Op", MathOps, () => m.Op, v => m.Op = v)
        },
        MixModulator m => new Parameter[]
        {
            new FloatParameter("A", 0, 1, () => m.A, v => m.A = v),
            new FloatParameter("B", 0, 1, () => m.B, v => m.B = v),
            new FloatParameter("Mix", 0, 1, () => m.Crossfade, v => m.Crossfade = v)
        },
        PolynomModulator m => new Parameter[]
        {
            new FloatParameter("Input", 0, 1, () => m.Input, v => m.Input = v),
            new FloatParameter("A", -2, 2, () => m.A, v => m.A = v),
            new FloatParameter("B", -2, 2, () => m.B, v => m.B = v),
            new FloatParameter("C", -2, 2, () => m.C, v => m.C = v)
        },
        QuantizeModulator m => new Parameter[]
        {
            new FloatParameter("Input", 0, 1, () => m.Input, v => m.Input = v),
            new FloatParameter("Steps", 1, 32, () => m.Steps, v => m.Steps = (int)v, "0")
        },
        Select4Modulator m => new Parameter[]
        {
            new FloatParameter("V0", 0, 1, () => m.V0, v => m.V0 = v),
            new FloatParameter("V1", 0, 1, () => m.V1, v => m.V1 = v),
            new FloatParameter("V2", 0, 1, () => m.V2, v => m.V2 = v),
            new FloatParameter("V3", 0, 1, () => m.V3, v => m.V3 = v),
            new FloatParameter("Select", 0, 3, () => m.Select, v => m.Select = (int)v, "0")
        },
        Vector4Modulator m => new Parameter[]
        {
            new FloatParameter("X", 0, 1, () => m.X, v => m.X = v),
            new FloatParameter("Y", 0, 1, () => m.Y, v => m.Y = v),
            new FloatParameter("Z0", 0, 1, () => m.Z0, v => m.Z0 = v),
            new FloatParameter("Z1", 0, 1, () => m.Z1, v => m.Z1 = v)
        },
        Vector8Modulator m => new Parameter[]
        {
            new FloatParameter("X", 0, 1, () => m.X, v => m.X = v),
            new FloatParameter("Y", 0, 1, () => m.Y, v => m.Y = v)
        },
        ParSeq8Modulator m => new Parameter[]
        {
            new FloatParameter("Rate", 0.03125, 4, () => m.RateBeats, v => m.RateBeats = v, "0.###", "beats")
        },
        _ => Array.Empty<Parameter>()
    };
}
