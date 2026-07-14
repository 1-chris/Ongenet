using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.MidiFx;

/// <summary>Transforms MIDI before it reaches instruments (scale, chord, arp, etc.).</summary>
public interface IMidiEffect
{
    string Name { get; }
    string TypeId { get; }
    bool Enabled { get; set; }
    IReadOnlyList<Parameter> Parameters { get; }
    IMidiEffect Clone();
    void Reset();
    IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input);
}

/// <summary>Optional beat-time expanders used by the scheduler for arps/echoes/repeats.</summary>
public interface IMidiNoteExpander
{
    IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandHeld(
        double onBeat, double offBeat, int note, float velocity);
}

public sealed class ScaleMidiEffect : IMidiEffect
{
    public const string TypeId = "midi.scale";
    string IMidiEffect.TypeId => TypeId;

    private static readonly bool[] MajorMask =
        { true, false, true, false, true, true, false, true, false, true, false, true };
    private static readonly bool[] MinorMask =
        { true, false, true, true, false, true, false, true, true, false, true, false };

    public string Name => "Scale";
    public bool Enabled { get; set; } = true;
    public int Root { get; set; }
    public bool Minor { get; set; }

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }

    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input)
    {
        if (!Enabled || input.Kind is not (Midi.MidiMessageKind.NoteOn or Midi.MidiMessageKind.NoteOff))
            return new[] { input };
        return new[] { input with { Data1 = (byte)SnapNote(input.Note) } };
    }

    public int SnapNote(int note)
    {
        if (!Enabled) return note;
        var mask = Minor ? MinorMask : MajorMask;
        var rel = Mod12(note - Root);
        if (mask[rel]) return note;

        var best = note;
        var bestDist = 128;
        for (var d = 1; d <= 6; d++)
        {
            var up = note + d;
            var down = note - d;
            if (up <= 127 && mask[Mod12(up - Root)] && d < bestDist) { best = up; bestDist = d; }
            if (down >= 0 && mask[Mod12(down - Root)] && d < bestDist) { best = down; bestDist = d; }
        }
        return best;
    }

    private static int Mod12(int n)
    {
        var r = n % 12;
        return r < 0 ? r + 12 : r;
    }
}

public sealed class QuantizeMidiEffect : IMidiEffect, IMidiNoteExpander
{
    public const string TypeId = "midi.quantize";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Quantize";
    public bool Enabled { get; set; } = true;
    /// <summary>0 = free, 1 = hard snap to scale/grid.</summary>
    public float Strength { get; set; } = 1f;
    public int Root { get; set; }
    public bool Minor { get; set; }
    private readonly ScaleMidiEffect _scale = new();
    private const double GridBeats = 0.25;

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }

    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input)
    {
        if (!Enabled || input.Kind is not (Midi.MidiMessageKind.NoteOn or Midi.MidiMessageKind.NoteOff))
            return new[] { input };
        _scale.Root = Root;
        _scale.Minor = Minor;
        var snapped = _scale.SnapNote(input.Note);
        var note = Strength >= 0.999f ? snapped : (int)Math.Round(input.Note + (snapped - input.Note) * Strength);
        return new[] { input with { Data1 = (byte)Math.Clamp(note, 0, 127) } };
    }

    public IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandHeld(
        double onBeat, double offBeat, int note, float velocity)
    {
        if (!Enabled) { yield return (onBeat, offBeat, note, velocity); yield break; }
        _scale.Root = Root;
        _scale.Minor = Minor;
        var snapped = _scale.SnapNote(note);
        var qNote = Strength >= 0.999f ? snapped : (int)Math.Round(note + (snapped - note) * Strength);
        var qOn = MidiHumanizer.Quantize(onBeat, GridBeats, Strength);
        yield return (qOn, offBeat, qNote, velocity);
    }
}

public sealed class ChordMidiEffect : IMidiEffect, IProjectStatefulComponent
{
    public const string TypeId = "midi.chord";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Chord";
    public bool Enabled { get; set; } = true;
    public int[] Intervals { get; set; } = { 0, 4, 7 };

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }

    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input)
    {
        if (!Enabled || input.Kind != Midi.MidiMessageKind.NoteOn) return new[] { input };
        var list = new List<Midi.MidiMessage> { input };
        foreach (var iv in Intervals)
        {
            if (iv == 0) continue;
            list.Add(input with { Data1 = (byte)Math.Clamp(input.Note + iv, 0, 127) });
        }
        return list;
    }

    public void WriteProjectState(OngenWriter w)
    {
        w.WriteInt(Intervals.Length);
        foreach (var iv in Intervals) w.WriteInt(iv);
    }

    public void ReadProjectState(OngenReader r)
    {
        var n = r.ReadInt();
        if (n <= 0) { Intervals = new[] { 0, 4, 7 }; return; }
        var arr = new int[n];
        for (var i = 0; i < n; i++) arr[i] = r.ReadInt();
        Intervals = arr;
    }
}

public sealed class HarmonizeMidiEffect : IMidiEffect
{
    public const string TypeId = "midi.harmonize";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Harmonize";
    public bool Enabled { get; set; } = true;
    public int Interval { get; set; } = 4;
    public int Root { get; set; }
    public bool Minor { get; set; }
    private readonly ScaleMidiEffect _scale = new();

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }

    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input)
    {
        if (!Enabled || input.Kind != Midi.MidiMessageKind.NoteOn) return new[] { input };
        _scale.Root = Root;
        _scale.Minor = Minor;
        var harmony = _scale.SnapNote(input.Note + Interval);
        return new[] { input, input with { Data1 = (byte)Math.Clamp(harmony, 0, 127) } };
    }
}

public sealed class ArpMidiEffect : IMidiEffect, IMidiNoteExpander, IMidiChordExpander
{
    public const string TypeId = "midi.arp";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Arpeggiator";
    public bool Enabled { get; set; } = true;
    public double RateBeats { get; set; } = 0.25;
    public double Gate { get; set; } = 0.85;
    public int OctaveRange { get; set; } = 1;
    public int Pattern { get; set; } // 0 Up, 1 Down, 2 UpDown, 3 Random
    private readonly Random _rng = new();

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }
    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input) => new[] { input };

    public IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandHeld(
        double onBeat, double offBeat, int note, float velocity)
        => ExpandPattern(onBeat, offBeat, new[] { note }, velocity);

    public IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandChord(
        double onBeat, double offBeat, IReadOnlyList<(int Note, float Velocity)> chord)
    {
        if (!Enabled || chord.Count == 0) yield break;
        var pitches = chord.Select(c => c.Note).Distinct().Order().ToArray();
        var vel = chord[0].Velocity;
        foreach (var n in ExpandPattern(onBeat, offBeat, pitches, vel))
            yield return n;
    }

    private IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandPattern(
        double onBeat, double offBeat, IReadOnlyList<int> pitches, float velocity)
    {
        if (!Enabled || RateBeats <= 1e-9)
        {
            foreach (var p in pitches)
                yield return (onBeat, offBeat, p, velocity);
            yield break;
        }

        var gate = RateBeats * Math.Clamp(Gate, 0.05, 1.0);
        var octaves = Math.Max(1, OctaveRange);
        var sequence = BuildPitchSequence(pitches, octaves);
        var step = 0;
        for (var t = onBeat; t < offBeat - 1e-9; t += RateBeats, step++)
        {
            var idx = Pattern switch
            {
                1 => sequence.Count - 1 - (step % sequence.Count),
                2 => UpDownIndex(step, sequence.Count),
                3 => _rng.Next(sequence.Count),
                _ => step % sequence.Count
            };
            yield return (t, Math.Min(t + gate, offBeat), sequence[idx], velocity);
        }
    }

    private List<int> BuildPitchSequence(IReadOnlyList<int> pitches, int octaves)
    {
        var list = new List<int>();
        for (var o = 0; o < octaves; o++)
        {
            foreach (var p in pitches)
                list.Add(Math.Clamp(p + o * 12, 0, 127));
        }
        return Pattern == 1 ? list.AsEnumerable().Reverse().ToList() : list;
    }

    private static int UpDownIndex(int step, int count)
    {
        if (count <= 1) return 0;
        var cycle = count * 2 - 2;
        var p = step % Math.Max(1, cycle);
        return p < count ? p : cycle - p;
    }
}

public sealed class NoteEchoMidiEffect : IMidiEffect, IMidiNoteExpander
{
    public const string TypeId = "midi.echo";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Echo";
    public bool Enabled { get; set; } = true;
    public double DelayBeats { get; set; } = 1;
    public float Feedback { get; set; } = 0.5f;
    public int MaxEchoes { get; set; } = 4;

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }
    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input) => new[] { input };

    public IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandEchoes(
        double onBeat, double offBeat, int note, float velocity)
        => ExpandHeld(onBeat, offBeat, note, velocity);

    public IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandHeld(
        double onBeat, double offBeat, int note, float velocity)
    {
        yield return (onBeat, offBeat, note, velocity);
        if (!Enabled || DelayBeats <= 1e-9) yield break;

        var gate = Math.Max(RateBeatsFallback(), DelayBeats * 0.85);
        var vel = velocity * Feedback;
        var echoOn = onBeat + DelayBeats;
        var echoes = 0;
        while (echoOn < offBeat - 1e-9 && vel > 0.04f && echoes < MaxEchoes)
        {
            yield return (echoOn, Math.Min(echoOn + gate, offBeat), note, vel);
            vel *= Feedback;
            echoOn += DelayBeats;
            echoes++;
        }
    }

    private double RateBeatsFallback() => DelayBeats * 0.85;
}

public sealed class RandomMidiEffect : IMidiEffect
{
    public const string TypeId = "midi.random";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Randomize";
    public bool Enabled { get; set; } = true;
    public float Probability { get; set; } = 0.5f;
    public int PitchRange { get; set; }
    public float VelocityJitter { get; set; }
    private readonly Random _rng = new();

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }

    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input)
    {
        if (!Enabled) return new[] { input };
        if (_rng.NextDouble() > Probability) return Array.Empty<Midi.MidiMessage>();
        var note = input.Note;
        var velByte = input.Data2;
        if (PitchRange > 0 && input.Kind is Midi.MidiMessageKind.NoteOn or Midi.MidiMessageKind.NoteOff)
            note = Math.Clamp(note + _rng.Next(-PitchRange, PitchRange + 1), 0, 127);
        if (VelocityJitter > 0 && input.Kind == Midi.MidiMessageKind.NoteOn)
            velByte = (byte)Math.Clamp((int)(velByte + (_rng.NextDouble() * 2 - 1) * VelocityJitter * 127), 1, 127);
        return new[] { input with { Data1 = (byte)note, Data2 = velByte } };
    }
}

public sealed class HumanizeMidiEffect : IMidiEffect
{
    public const string TypeId = "midi.humanize";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Humanize";
    public bool Enabled { get; set; } = true;
    public float TimingMs { get; set; } = 8f;
    public float VelocityAmount { get; set; } = 0.1f;
    private readonly Random _rng = new();

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }

    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input)
    {
        if (!Enabled || input.Kind != Midi.MidiMessageKind.NoteOn) return new[] { input };
        var vel01 = MidiHumanizer.Velocity(input.Velocity, VelocityAmount, _rng);
        return new[] { input with { Data2 = (byte)Math.Clamp((int)(vel01 * 127), 1, 127) } };
    }
}

public sealed class NoteTransposeMidiEffect : IMidiEffect
{
    public const string TypeId = "midi.transpose";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Note Transpose";
    public bool Enabled { get; set; } = true;
    public int Semitones { get; set; }

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }

    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input)
    {
        if (!Enabled || input.Kind is not (Midi.MidiMessageKind.NoteOn or Midi.MidiMessageKind.NoteOff))
            return new[] { input };
        return new[] { input with { Data1 = (byte)Math.Clamp(input.Note + Semitones, 0, 127) } };
    }
}

public sealed class NoteDelayMidiEffect : IMidiEffect, IMidiNoteExpander
{
    public const string TypeId = "midi.note_delay";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Note Delay";
    public bool Enabled { get; set; } = true;
    public double DelayBeats { get; set; } = 0.25;

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }
    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input) => new[] { input };

    public IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandHeld(
        double onBeat, double offBeat, int note, float velocity)
    {
        if (!Enabled) { yield return (onBeat, offBeat, note, velocity); yield break; }
        var d = Math.Max(0, DelayBeats);
        yield return (onBeat + d, offBeat + d, note, velocity);
    }
}

public sealed class NoteLengthMidiEffect : IMidiEffect, IMidiNoteExpander
{
    public const string TypeId = "midi.note_length";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Note Length";
    public bool Enabled { get; set; } = true;
    public double LengthBeats { get; set; } = 0.25;
    public bool FixedLength { get; set; } = true;

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }
    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input) => new[] { input };

    public IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandHeld(
        double onBeat, double offBeat, int note, float velocity)
    {
        if (!Enabled || !FixedLength) { yield return (onBeat, offBeat, note, velocity); yield break; }
        yield return (onBeat, onBeat + Math.Max(0.01, LengthBeats), note, velocity);
    }
}

public sealed class NoteRepeatsMidiEffect : IMidiEffect, IMidiNoteExpander
{
    public const string TypeId = "midi.note_repeats";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Note Repeats";
    public bool Enabled { get; set; } = true;
    public int Repeats { get; set; } = 3;
    public double RateBeats { get; set; } = 0.125;

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }
    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input) => new[] { input };

    public IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandHeld(
        double onBeat, double offBeat, int note, float velocity)
    {
        yield return (onBeat, offBeat, note, velocity);
        if (!Enabled) yield break;
        var gate = RateBeats * 0.8;
        for (var i = 1; i <= Repeats; i++)
        {
            var t = onBeat + i * RateBeats;
            if (t >= offBeat) break;
            yield return (t, Math.Min(t + gate, offBeat), note, velocity * (1f - i * 0.1f));
        }
    }
}

public sealed class VelocityCurveMidiEffect : IMidiEffect
{
    public const string TypeId = "midi.velocity_curve";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Velocity Curve";
    public bool Enabled { get; set; } = true;
    /// <summary>&lt;1 softens, &gt;1 expands.</summary>
    public float Curve { get; set; } = 1f;
    public float Gain { get; set; } = 1f;

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }

    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input)
    {
        if (!Enabled || input.Kind != Midi.MidiMessageKind.NoteOn) return new[] { input };
        var n = input.Velocity / 127.0;
        var shaped = Math.Pow(n, Math.Max(0.1, Curve)) * Gain;
        return new[] { input with { Data2 = (byte)Math.Clamp((int)(shaped * 127), 1, 127) } };
    }
}

public sealed class KeyFilterMidiEffect : IMidiEffect
{
    public const string TypeId = "midi.key_filter";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Key Filter+";
    public bool Enabled { get; set; } = true;
    public int Root { get; set; }
    public bool Minor { get; set; }
    private readonly ScaleMidiEffect _scale = new();

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }

    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input)
    {
        if (!Enabled || input.Kind is not (Midi.MidiMessageKind.NoteOn or Midi.MidiMessageKind.NoteOff))
            return new[] { input };
        _scale.Root = Root;
        _scale.Minor = Minor;
        return _scale.SnapNote(input.Note) == input.Note ? new[] { input } : Array.Empty<Midi.MidiMessage>();
    }
}

public sealed class NoteFilterMidiEffect : IMidiEffect
{
    public const string TypeId = "midi.note_filter";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Note Filter";
    public bool Enabled { get; set; } = true;
    public int LowNote { get; set; }
    public int HighNote { get; set; } = 127;

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }

    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input)
    {
        if (!Enabled || input.Kind is not (Midi.MidiMessageKind.NoteOn or Midi.MidiMessageKind.NoteOff))
            return new[] { input };
        return input.Note >= LowNote && input.Note <= HighNote ? new[] { input } : Array.Empty<Midi.MidiMessage>();
    }
}

public sealed class ChannelFilterMidiEffect : IMidiEffect
{
    public const string TypeId = "midi.channel_filter";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Channel Filter";
    public bool Enabled { get; set; } = true;
    public int Channel { get; set; } // 0 = all, 1-16

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }

    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input)
    {
        if (!Enabled || Channel <= 0) return new[] { input };
        return input.Channel == Channel - 1 ? new[] { input } : Array.Empty<Midi.MidiMessage>();
    }
}

public sealed class ChannelMapMidiEffect : IMidiEffect
{
    public const string TypeId = "midi.channel_map";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Channel Map";
    public bool Enabled { get; set; } = true;
    public int SourceChannel { get; set; } // 0 = any
    public int DestChannel { get; set; } = 1;

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }

    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input)
    {
        if (!Enabled) return new[] { input };
        if (SourceChannel > 0 && input.Channel != SourceChannel - 1) return new[] { input };
        return new[] { input with { Channel = (byte)Math.Clamp(DestChannel - 1, 0, 15) } };
    }
}

public sealed class BendMidiEffect : IMidiEffect
{
    public const string TypeId = "midi.bend";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Bend";
    public bool Enabled { get; set; } = true;
    public int Semitones { get; set; } = 2;

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }

    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input)
    {
        // Pass-through with TypeId for persistence; bend amount is applied at schedule via Semitones.
        return new[] { input };
    }
}

public sealed class MicroPitchMidiEffect : IMidiEffect
{
    public const string TypeId = "midi.micro_pitch";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Micro-pitch";
    public bool Enabled { get; set; } = true;
    public float Cents { get; set; }

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }
    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input) => new[] { input };
}

public sealed class StrumMidiEffect : IMidiEffect, IMidiNoteExpander
{
    public const string TypeId = "midi.strum";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Strum";
    public bool Enabled { get; set; } = true;
    public double SpreadBeats { get; set; } = 0.05;

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }
    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input) => new[] { input };

    public IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandHeld(
        double onBeat, double offBeat, int note, float velocity)
    {
        // Single-note path; chord strumming is applied when multiple notes share an onset.
        yield return (onBeat, offBeat, note, velocity);
    }
}

public sealed class LatchMidiEffect : IMidiEffect
{
    public const string TypeId = "midi.latch";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Latch";
    public bool Enabled { get; set; } = true;
    private readonly HashSet<int> _held = new();

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() => _held.Clear();

    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input)
    {
        if (!Enabled) return new[] { input };
        if (input.Kind == Midi.MidiMessageKind.NoteOn)
        {
            if (_held.Contains(input.Note))
            {
                _held.Remove(input.Note);
                return new[] { input with { Kind = Midi.MidiMessageKind.NoteOff, Data2 = 0 } };
            }
            _held.Add(input.Note);
            return new[] { input };
        }
        if (input.Kind == Midi.MidiMessageKind.NoteOff)
            return Array.Empty<Midi.MidiMessage>(); // swallow note-offs; latch releases on re-trigger
        return new[] { input };
    }
}

public sealed class MultiNoteMidiEffect : IMidiEffect, IProjectStatefulComponent
{
    public const string TypeId = "midi.multi_note";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Multi-note";
    public bool Enabled { get; set; } = true;
    public int[] Offsets { get; set; } = { 0, 12, 7 };

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }

    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input)
    {
        if (!Enabled || input.Kind is not (Midi.MidiMessageKind.NoteOn or Midi.MidiMessageKind.NoteOff))
            return new[] { input };
        var list = new List<Midi.MidiMessage>();
        foreach (var o in Offsets)
            list.Add(input with { Data1 = (byte)Math.Clamp(input.Note + o, 0, 127) });
        return list;
    }

    public void WriteProjectState(OngenWriter w)
    {
        w.WriteInt(Offsets.Length);
        foreach (var o in Offsets) w.WriteInt(o);
    }

    public void ReadProjectState(OngenReader r)
    {
        var n = r.ReadInt();
        if (n <= 0) { Offsets = new[] { 0, 12, 7 }; return; }
        var arr = new int[n];
        for (var i = 0; i < n; i++) arr[i] = r.ReadInt();
        Offsets = arr;
    }
}

public sealed class NoteGridMidiEffect : IMidiEffect, IMidiNoteExpander
{
    public const string TypeId = "midi.note_grid";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Note Grid";
    public bool Enabled { get; set; } = true;
    public double GridBeats { get; set; } = 0.25;

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }
    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input) => new[] { input };

    public IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandHeld(
        double onBeat, double offBeat, int note, float velocity)
    {
        if (!Enabled || GridBeats <= 1e-9) { yield return (onBeat, offBeat, note, velocity); yield break; }
        var snapped = Math.Round(onBeat / GridBeats) * GridBeats;
        var len = offBeat - onBeat;
        yield return (snapped, snapped + len, note, velocity);
    }
}

public sealed class StepwiseMidiEffect : IMidiEffect, IMidiNoteExpander
{
    public const string TypeId = "midi.stepwise";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Stepwise";
    public bool Enabled { get; set; } = true;
    public int Steps { get; set; } = 8;
    public double StepBeats { get; set; } = 0.125;
    private int _step;

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() => _step = 0;
    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input) => new[] { input };

    public IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandHeld(
        double onBeat, double offBeat, int note, float velocity)
    {
        if (!Enabled) { yield return (onBeat, offBeat, note, velocity); yield break; }
        var s = _step++ % Math.Max(1, Steps);
        var t = onBeat + s * StepBeats;
        yield return (t, Math.Min(t + StepBeats * 0.9, offBeat), note, velocity);
    }
}

public sealed class DribbleMidiEffect : IMidiEffect, IMidiNoteExpander
{
    public const string TypeId = "midi.dribble";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Dribble";
    public bool Enabled { get; set; } = true;
    public double RateBeats { get; set; } = 0.0625;
    public float Decay { get; set; } = 0.7f;
    public int MaxHits { get; set; } = 6;

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }
    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input) => new[] { input };

    public IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandHeld(
        double onBeat, double offBeat, int note, float velocity)
    {
        var vel = velocity;
        var t = onBeat;
        for (var i = 0; i < MaxHits && t < offBeat && vel > 0.04f; i++)
        {
            yield return (t, Math.Min(t + RateBeats * 0.7, offBeat), note, vel);
            if (!Enabled) yield break;
            t += RateBeats;
            vel *= Decay;
        }
    }
}

public sealed class RicochetMidiEffect : IMidiEffect, IMidiNoteExpander
{
    public const string TypeId = "midi.ricochet";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Ricochet";
    public bool Enabled { get; set; } = true;
    public double RateBeats { get; set; } = 0.125;
    public int Bounces { get; set; } = 4;
    public int PitchStep { get; set; } = 1;

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }
    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input) => new[] { input };

    public IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandHeld(
        double onBeat, double offBeat, int note, float velocity)
    {
        yield return (onBeat, offBeat, note, velocity);
        if (!Enabled) yield break;
        for (var i = 1; i <= Bounces; i++)
        {
            var t = onBeat + i * RateBeats;
            if (t >= offBeat) break;
            yield return (t, Math.Min(t + RateBeats * 0.8, offBeat), Math.Clamp(note + i * PitchStep, 0, 127), velocity * (1f - i * 0.12f));
        }
    }
}

public sealed class TransposeMapMidiEffect : IMidiEffect, IProjectStatefulComponent
{
    public const string TypeId = "midi.transpose_map";
    string IMidiEffect.TypeId => TypeId;
    public string Name => "Transpose Map";
    public bool Enabled { get; set; } = true;
    /// <summary>Per-chromatic-class offset (length 12).</summary>
    public int[] Map { get; set; } = new int[12];

    public IReadOnlyList<Parameter> Parameters => MidiEffectParameterFactory.Get(this);
    public IMidiEffect Clone() => MidiEffectCloner.Clone(this, new MidiEffectRegistry());

    public void Reset() { }

    public IEnumerable<Midi.MidiMessage> Process(Midi.MidiMessage input)
    {
        if (!Enabled || input.Kind is not (Midi.MidiMessageKind.NoteOn or Midi.MidiMessageKind.NoteOff))
            return new[] { input };
        var pc = ((input.Note % 12) + 12) % 12;
        var off = Map.Length == 12 ? Map[pc] : 0;
        return new[] { input with { Data1 = (byte)Math.Clamp(input.Note + off, 0, 127) } };
    }

    public void WriteProjectState(OngenWriter w)
    {
        for (var i = 0; i < 12; i++) w.WriteInt(Map.Length == 12 ? Map[i] : 0);
    }

    public void ReadProjectState(OngenReader r)
    {
        Map = new int[12];
        for (var i = 0; i < 12; i++) Map[i] = r.ReadInt();
    }
}
