using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Core.Audio.MidiFx;

/// <summary>Transforms MIDI before it reaches instruments (scale, chord, arp, etc.).</summary>
public interface IMidiEffect
{
    string Name { get; }
    bool Enabled { get; set; }
    void Reset();
    IEnumerable<MidiMessage> Process(MidiMessage input);
}

public sealed class ScaleMidiEffect : IMidiEffect
{
    private static readonly bool[] MajorMask =
        { true, false, true, false, true, true, false, true, false, true, false, true };

    private static readonly bool[] MinorMask =
        { true, false, true, true, false, true, false, true, true, false, true, false };

    public string Name => "Scale";
    public bool Enabled { get; set; } = true;
    public int Root { get; set; } = 0;
    public bool Minor { get; set; }

    public void Reset() { }

    public IEnumerable<MidiMessage> Process(MidiMessage input)
    {
        if (!Enabled || input.Kind is not (MidiMessageKind.NoteOn or MidiMessageKind.NoteOff))
            return new[] { input };
        var snapped = SnapNote(input.Note);
        return new[] { input with { Data1 = (byte)snapped } };
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

public sealed class ChordMidiEffect : IMidiEffect
{
    public string Name => "Chord";
    public bool Enabled { get; set; } = true;
    public int[] Intervals { get; set; } = { 0, 4, 7 };

    public void Reset() { }

    public IEnumerable<MidiMessage> Process(MidiMessage input)
    {
        if (!Enabled || input.Kind != MidiMessageKind.NoteOn) return new[] { input };
        var list = new List<MidiMessage> { input };
        foreach (var iv in Intervals)
        {
            if (iv == 0) continue;
            list.Add(input with { Data1 = (byte)Math.Clamp(input.Note + iv, 0, 127) });
        }

        return list;
    }
}

public sealed class ArpMidiEffect : IMidiEffect
{
    public string Name => "Arp";
    public bool Enabled { get; set; } = true;
    public double RateBeats { get; set; } = 0.25;

    public void Reset() { }

    public IEnumerable<MidiMessage> Process(MidiMessage input) => new[] { input };

    public IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandHeld(
        double onBeat, double offBeat, int note, float velocity)
    {
        if (!Enabled || RateBeats <= 1e-9)
        {
            yield return (onBeat, offBeat, note, velocity);
            yield break;
        }

        var gate = RateBeats * 0.85;
        for (var t = onBeat; t < offBeat - 1e-9; t += RateBeats)
        {
            var noteOff = Math.Min(t + gate, offBeat);
            yield return (t, noteOff, note, velocity);
        }
    }
}

public sealed class RandomMidiEffect : IMidiEffect
{
    public string Name => "Random";
    public bool Enabled { get; set; } = true;
    public float Probability { get; set; } = 0.5f;
    private readonly Random _rng = new();

    public void Reset() { }

    public IEnumerable<MidiMessage> Process(MidiMessage input)
    {
        if (!Enabled || _rng.NextDouble() > Probability) return Array.Empty<MidiMessage>();
        return new[] { input };
    }
}

public sealed class NoteEchoMidiEffect : IMidiEffect
{
    public string Name => "Note Echo";
    public bool Enabled { get; set; } = true;
    public double DelayBeats { get; set; } = 1;
    public float Feedback { get; set; } = 0.5f;
    public int MaxEchoes { get; set; } = 4;

    public void Reset() { }

    public IEnumerable<MidiMessage> Process(MidiMessage input) => new[] { input };

    public IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandEchoes(
        double onBeat, double offBeat, int note, float velocity)
    {
        yield return (onBeat, offBeat, note, velocity);
        if (!Enabled || DelayBeats <= 1e-9) yield break;

        var vel = velocity * Feedback;
        var echoOn = onBeat + DelayBeats;
        var echoes = 0;
        while (echoOn < offBeat - 1e-9 && vel > 0.04f && echoes < MaxEchoes)
        {
            yield return (echoOn, offBeat, note, vel);
            vel *= Feedback;
            echoOn += DelayBeats;
            echoes++;
        }
    }
}
