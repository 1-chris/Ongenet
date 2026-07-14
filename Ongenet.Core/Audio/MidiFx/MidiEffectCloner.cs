using System;
using Ongenet.Core.Audio.Hardware;

namespace Ongenet.Core.Audio.MidiFx;

/// <summary>Deep-copies MIDI effect state for undo, clone, and persistence roundtrips.</summary>
public static class MidiEffectCloner
{
    public static IMidiEffect Clone(IMidiEffect source, IMidiEffectRegistry registry)
    {
        var copy = registry.Create(source.TypeId);
        copy.Enabled = source.Enabled;
        CopyState(source, copy);
        return copy;
    }

    public static void CopyState(IMidiEffect from, IMidiEffect to)
    {
        switch (from)
        {
            case ScaleMidiEffect s when to is ScaleMidiEffect d:
                d.Root = s.Root; d.Minor = s.Minor; break;
            case QuantizeMidiEffect s when to is QuantizeMidiEffect d:
                d.Strength = s.Strength; d.Root = s.Root; d.Minor = s.Minor; break;
            case ChordMidiEffect s when to is ChordMidiEffect d:
                d.Intervals = (int[])s.Intervals.Clone(); break;
            case HarmonizeMidiEffect s when to is HarmonizeMidiEffect d:
                d.Interval = s.Interval; d.Root = s.Root; d.Minor = s.Minor; break;
            case ArpMidiEffect s when to is ArpMidiEffect d:
                d.RateBeats = s.RateBeats; d.Gate = s.Gate; d.OctaveRange = s.OctaveRange; d.Pattern = s.Pattern; break;
            case NoteEchoMidiEffect s when to is NoteEchoMidiEffect d:
                d.DelayBeats = s.DelayBeats; d.Feedback = s.Feedback; d.MaxEchoes = s.MaxEchoes; break;
            case RandomMidiEffect s when to is RandomMidiEffect d:
                d.Probability = s.Probability; d.PitchRange = s.PitchRange; d.VelocityJitter = s.VelocityJitter; break;
            case HumanizeMidiEffect s when to is HumanizeMidiEffect d:
                d.TimingMs = s.TimingMs; d.VelocityAmount = s.VelocityAmount; break;
            case NoteTransposeMidiEffect s when to is NoteTransposeMidiEffect d:
                d.Semitones = s.Semitones; break;
            case NoteDelayMidiEffect s when to is NoteDelayMidiEffect d:
                d.DelayBeats = s.DelayBeats; break;
            case NoteLengthMidiEffect s when to is NoteLengthMidiEffect d:
                d.LengthBeats = s.LengthBeats; d.FixedLength = s.FixedLength; break;
            case NoteRepeatsMidiEffect s when to is NoteRepeatsMidiEffect d:
                d.Repeats = s.Repeats; d.RateBeats = s.RateBeats; break;
            case VelocityCurveMidiEffect s when to is VelocityCurveMidiEffect d:
                d.Curve = s.Curve; d.Gain = s.Gain; break;
            case KeyFilterMidiEffect s when to is KeyFilterMidiEffect d:
                d.Root = s.Root; d.Minor = s.Minor; break;
            case NoteFilterMidiEffect s when to is NoteFilterMidiEffect d:
                d.LowNote = s.LowNote; d.HighNote = s.HighNote; break;
            case ChannelFilterMidiEffect s when to is ChannelFilterMidiEffect d:
                d.Channel = s.Channel; break;
            case ChannelMapMidiEffect s when to is ChannelMapMidiEffect d:
                d.SourceChannel = s.SourceChannel; d.DestChannel = s.DestChannel; break;
            case BendMidiEffect s when to is BendMidiEffect d:
                d.Semitones = s.Semitones; break;
            case MicroPitchMidiEffect s when to is MicroPitchMidiEffect d:
                d.Cents = s.Cents; break;
            case StrumMidiEffect s when to is StrumMidiEffect d:
                d.SpreadBeats = s.SpreadBeats; break;
            case NoteGridMidiEffect s when to is NoteGridMidiEffect d:
                d.GridBeats = s.GridBeats; break;
            case StepwiseMidiEffect s when to is StepwiseMidiEffect d:
                d.Steps = s.Steps; d.StepBeats = s.StepBeats; break;
            case DribbleMidiEffect s when to is DribbleMidiEffect d:
                d.RateBeats = s.RateBeats; d.Decay = s.Decay; d.MaxHits = s.MaxHits; break;
            case RicochetMidiEffect s when to is RicochetMidiEffect d:
                d.RateBeats = s.RateBeats; d.Bounces = s.Bounces; d.PitchStep = s.PitchStep; break;
            case MultiNoteMidiEffect s when to is MultiNoteMidiEffect d:
                d.Offsets = (int[])s.Offsets.Clone(); break;
            case TransposeMapMidiEffect s when to is TransposeMapMidiEffect d:
                d.Map = (int[])s.Map.Clone(); break;
            case MidiCcMidiEffect s when to is MidiCcMidiEffect d:
                d.Channel = s.Channel; d.Controller = s.Controller; d.Value = s.Value; d.SendOnNote = s.SendOnNote; break;
            case MidiProgramChangeMidiEffect s when to is MidiProgramChangeMidiEffect d:
                d.Channel = s.Channel; d.Program = s.Program; d.UseSongSelect = s.UseSongSelect; break;
        }
    }
}
