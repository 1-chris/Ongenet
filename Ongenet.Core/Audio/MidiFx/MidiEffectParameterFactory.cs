using System;
using Ongenet.Core.Audio.Hardware;
using System.Collections.Generic;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.MidiFx;

/// <summary>Builds generic <see cref="Parameter"/> lists for MIDI effects (UI + persistence).</summary>
public static class MidiEffectParameterFactory
{
    private static readonly string[] ArpPatterns = { "Up", "Down", "Up/Down", "Random" };

    public static IReadOnlyList<Parameter> Get(IMidiEffect fx) => fx switch
    {
        ScaleMidiEffect s => new Parameter[]
        {
            new FloatParameter("Root", 0, 11, () => s.Root, v => s.Root = (int)v, "0"),
            new BoolParameter("Minor", () => s.Minor, v => s.Minor = v)
        },
        QuantizeMidiEffect q => new Parameter[]
        {
            new FloatParameter("Strength", 0, 1, () => q.Strength, v => q.Strength = (float)v),
            new FloatParameter("Root", 0, 11, () => q.Root, v => q.Root = (int)v, "0"),
            new BoolParameter("Minor", () => q.Minor, v => q.Minor = v)
        },
        ChordMidiEffect c => new Parameter[]
        {
            new FloatParameter("Intervals", 0, 127, () => 0, _ => { }, "0") // custom state via intervals text in UI
        },
        HarmonizeMidiEffect h => new Parameter[]
        {
            new FloatParameter("Interval", -24, 24, () => h.Interval, v => h.Interval = (int)v, "0", "st"),
            new FloatParameter("Root", 0, 11, () => h.Root, v => h.Root = (int)v, "0"),
            new BoolParameter("Minor", () => h.Minor, v => h.Minor = v)
        },
        ArpMidiEffect a => new Parameter[]
        {
            new FloatParameter("Rate", 0.0625, 4, () => a.RateBeats, v => a.RateBeats = v, "0.###", "beats"),
            new FloatParameter("Gate", 0.05, 1, () => a.Gate, v => a.Gate = v),
            new FloatParameter("Octaves", 1, 4, () => a.OctaveRange, v => a.OctaveRange = (int)v, "0"),
            new ChoiceParameter("Pattern", ArpPatterns, () => a.Pattern, v => a.Pattern = v)
        },
        NoteEchoMidiEffect e => new Parameter[]
        {
            new FloatParameter("Delay", 0.0625, 8, () => e.DelayBeats, v => e.DelayBeats = v, "0.##", "beats"),
            new FloatParameter("Feedback", 0, 1, () => e.Feedback, v => e.Feedback = (float)v),
            new FloatParameter("Max Echoes", 1, 16, () => e.MaxEchoes, v => e.MaxEchoes = (int)v, "0")
        },
        RandomMidiEffect r => new Parameter[]
        {
            new FloatParameter("Probability", 0, 1, () => r.Probability, v => r.Probability = (float)v),
            new FloatParameter("Pitch Range", 0, 12, () => r.PitchRange, v => r.PitchRange = (int)v, "0", "st"),
            new FloatParameter("Velocity Jitter", 0, 1, () => r.VelocityJitter, v => r.VelocityJitter = (float)v)
        },
        HumanizeMidiEffect h => new Parameter[]
        {
            new FloatParameter("Timing", 0, 50, () => h.TimingMs, v => h.TimingMs = (float)v, "0", "ms"),
            new FloatParameter("Velocity", 0, 1, () => h.VelocityAmount, v => h.VelocityAmount = (float)v)
        },
        NoteTransposeMidiEffect t => new Parameter[]
        {
            new FloatParameter("Semitones", -48, 48, () => t.Semitones, v => t.Semitones = (int)v, "0", "st")
        },
        NoteDelayMidiEffect d => new Parameter[]
        {
            new FloatParameter("Delay", 0, 4, () => d.DelayBeats, v => d.DelayBeats = v, "0.###", "beats")
        },
        NoteLengthMidiEffect l => new Parameter[]
        {
            new FloatParameter("Length", 0.01, 4, () => l.LengthBeats, v => l.LengthBeats = v, "0.###", "beats"),
            new BoolParameter("Fixed", () => l.FixedLength, v => l.FixedLength = v)
        },
        NoteRepeatsMidiEffect r => new Parameter[]
        {
            new FloatParameter("Repeats", 1, 16, () => r.Repeats, v => r.Repeats = (int)v, "0"),
            new FloatParameter("Rate", 0.03125, 1, () => r.RateBeats, v => r.RateBeats = v, "0.###", "beats")
        },
        VelocityCurveMidiEffect vc => new Parameter[]
        {
            new FloatParameter("Curve", 0.1, 4, () => vc.Curve, val => vc.Curve = (float)val, skew: 2.0),
            new FloatParameter("Gain", 0, 2, () => vc.Gain, val => vc.Gain = (float)val)
        },
        KeyFilterMidiEffect k => new Parameter[]
        {
            new FloatParameter("Root", 0, 11, () => k.Root, v => k.Root = (int)v, "0"),
            new BoolParameter("Minor", () => k.Minor, v => k.Minor = v)
        },
        NoteFilterMidiEffect f => new Parameter[]
        {
            new FloatParameter("Low", 0, 127, () => f.LowNote, v => f.LowNote = (int)v, "0"),
            new FloatParameter("High", 0, 127, () => f.HighNote, v => f.HighNote = (int)v, "0")
        },
        ChannelFilterMidiEffect c => new Parameter[]
        {
            new FloatParameter("Channel", 0, 16, () => c.Channel, v => c.Channel = (int)v, "0")
        },
        ChannelMapMidiEffect m => new Parameter[]
        {
            new FloatParameter("Source", 0, 16, () => m.SourceChannel, v => m.SourceChannel = (int)v, "0"),
            new FloatParameter("Dest", 1, 16, () => m.DestChannel, v => m.DestChannel = (int)v, "0")
        },
        BendMidiEffect b => new Parameter[]
        {
            new FloatParameter("Semitones", 0, 12, () => b.Semitones, v => b.Semitones = (int)v, "0", "st")
        },
        MicroPitchMidiEffect m => new Parameter[]
        {
            new FloatParameter("Cents", -100, 100, () => m.Cents, v => m.Cents = (float)v, "0")
        },
        StrumMidiEffect s => new Parameter[]
        {
            new FloatParameter("Spread", 0, 0.5, () => s.SpreadBeats, v => s.SpreadBeats = v, "0.###", "beats")
        },
        LatchMidiEffect => Array.Empty<Parameter>(),
        MultiNoteMidiEffect => Array.Empty<Parameter>(),
        NoteGridMidiEffect g => new Parameter[]
        {
            new FloatParameter("Grid", 0.0625, 1, () => g.GridBeats, v => g.GridBeats = v, "0.###", "beats")
        },
        StepwiseMidiEffect s => new Parameter[]
        {
            new FloatParameter("Steps", 1, 32, () => s.Steps, v => s.Steps = (int)v, "0"),
            new FloatParameter("Step Rate", 0.03125, 1, () => s.StepBeats, v => s.StepBeats = v, "0.###", "beats")
        },
        DribbleMidiEffect d => new Parameter[]
        {
            new FloatParameter("Rate", 0.03125, 0.5, () => d.RateBeats, v => d.RateBeats = v, "0.####", "beats"),
            new FloatParameter("Decay", 0, 1, () => d.Decay, v => d.Decay = (float)v),
            new FloatParameter("Max Hits", 1, 16, () => d.MaxHits, v => d.MaxHits = (int)v, "0")
        },
        RicochetMidiEffect r => new Parameter[]
        {
            new FloatParameter("Rate", 0.03125, 1, () => r.RateBeats, v => r.RateBeats = v, "0.###", "beats"),
            new FloatParameter("Bounces", 1, 16, () => r.Bounces, v => r.Bounces = (int)v, "0"),
            new FloatParameter("Pitch Step", -12, 12, () => r.PitchStep, v => r.PitchStep = (int)v, "0", "st")
        },
        TransposeMapMidiEffect => Array.Empty<Parameter>(),
        MidiCcMidiEffect cc => new Parameter[]
        {
            new FloatParameter("Channel", 1, 16, () => cc.Channel, v => cc.Channel = (int)v, "0"),
            new FloatParameter("Controller", 0, 127, () => cc.Controller, v => cc.Controller = (int)v, "0"),
            new FloatParameter("Value", 0, 127, () => cc.Value, v => cc.Value = (int)v, "0"),
            new BoolParameter("Send on Note", () => cc.SendOnNote, v => cc.SendOnNote = v)
        },
        MidiProgramChangeMidiEffect pc => new Parameter[]
        {
            new FloatParameter("Channel", 1, 16, () => pc.Channel, v => pc.Channel = (int)v, "0"),
            new FloatParameter("Program", 0, 127, () => pc.Program, v => pc.Program = (int)v, "0"),
            new BoolParameter("Song Select", () => pc.UseSongSelect, v => pc.UseSongSelect = v)
        },
        MidiSongSelectMidiEffect ss => new Parameter[]
        {
            new FloatParameter("Song", 0, 127, () => ss.SongNumber, v => ss.SongNumber = (int)v, "0"),
            new BoolParameter("Send on Load", () => ss.SendOnLoad, v => ss.SendOnLoad = v),
            new BoolParameter("Send", () => ss.ManualSend, v => ss.ManualSend = v)
        },
        _ => Array.Empty<Parameter>()
    };
}
