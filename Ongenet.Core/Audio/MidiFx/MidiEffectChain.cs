using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Core.Audio.MidiFx;

/// <summary>One source note before MIDI-FX expansion (clip or pattern step).</summary>
public readonly record struct MidiSourceNote(
    double OnBeat,
    double OffBeat,
    int Note,
    float Velocity,
    int HumanizeTicks = 0);

/// <summary>Expanded note with optional humanize timing and pitch-bend metadata.</summary>
public readonly record struct MidiExpandedNote(
    double OnBeat,
    double OffBeat,
    int Note,
    float Velocity,
    double TimingOffsetBeats = 0,
    int? PitchBend14 = null);

/// <summary>Chord-aware beat-time expanders (arp across simultaneous notes).</summary>
public interface IMidiChordExpander
{
    IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandChord(
        double onBeat, double offBeat, IReadOnlyList<(int Note, float Velocity)> chord);
}

/// <summary>Runs a track's MIDI-FX chain, expanding scheduled notes for the arrangement scheduler.</summary>
public sealed class MidiEffectChain
{
    private const double BeatEpsilon = 1e-6;
    private const double PpqPerBeat = 480.0;

    private readonly IMidiEffect[] _effects;
    private readonly Random _rng = new();

    public MidiEffectChain(IMidiEffect[] effects) => _effects = effects ?? Array.Empty<IMidiEffect>();

    public bool IsEmpty => _effects.Length == 0;

    /// <summary>Processes a single message through enabled effects in series.</summary>
    public IEnumerable<MidiMessage> Process(MidiMessage input)
    {
        IEnumerable<MidiMessage> current = new[] { input };
        foreach (var fx in _effects)
        {
            if (!fx.Enabled) continue;
            var next = new List<MidiMessage>();
            foreach (var msg in current)
                next.AddRange(fx.Process(msg));
            current = next;
        }

        return current;
    }

    /// <summary>Expands one held note (convenience wrapper around <see cref="ExpandNotes"/>).</summary>
    public IEnumerable<MidiExpandedNote> ExpandNote(double onBeat, double offBeat, int note, float velocity, double bpm = 120)
        => ExpandNotes(new[] { new MidiSourceNote(onBeat, offBeat, note, velocity) }, bpm);

    /// <summary>
    /// Expands one or more simultaneous notes through the chain. Groups notes sharing an onset for
    /// Strum spread and chord-aware arpeggiation.
    /// </summary>
    public IEnumerable<MidiExpandedNote> ExpandNotes(IReadOnlyList<MidiSourceNote> sources, double bpm)
    {
        if (sources.Count == 0) yield break;
        if (_effects.Length == 0)
        {
            foreach (var src in sources)
                yield return ToExpanded(src, 0, null);
            yield break;
        }

        foreach (var group in GroupByOnset(sources))
        {
            var strummed = ApplyStrumPrePass(group);
            var notes = ExpandGroup(strummed);
            foreach (var n in ApplyPostEffects(notes, bpm, group))
                yield return n;
        }
    }

    private static IEnumerable<IGrouping<double, MidiSourceNote>> GroupByOnset(IReadOnlyList<MidiSourceNote> sources)
    {
        return sources
            .GroupBy(s => Math.Round(s.OnBeat / BeatEpsilon) * BeatEpsilon)
            .OrderBy(g => g.Key);
    }

    private List<(double OnBeat, double OffBeat, int Note, float Velocity)> ApplyStrumPrePass(
        IGrouping<double, MidiSourceNote> group)
    {
        var list = group.Select(s => (s.OnBeat, s.OffBeat, s.Note, s.Velocity)).ToList();
        var strum = _effects.OfType<StrumMidiEffect>().FirstOrDefault(f => f.Enabled);
        if (strum is null || list.Count <= 1 || strum.SpreadBeats <= 0) return list;

        var sorted = list.OrderBy(n => n.Note).ToList();
        var result = new List<(double, double, int, float)>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
        {
            var n = sorted[i];
            var onset = MidiStrummer.OnsetForIndex(group.Key, i, sorted.Count, strum.SpreadBeats, up: true);
            result.Add((onset, n.OffBeat, n.Note, n.Velocity));
        }
        return result;
    }

    private List<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandGroup(
        List<(double OnBeat, double OffBeat, int Note, float Velocity)> notes)
    {
        foreach (var fx in _effects)
        {
            if (!fx.Enabled) continue;

            if (fx is ArpMidiEffect arp && notes.Count > 1)
            {
                var on = notes.Min(n => n.OnBeat);
                var off = notes.Max(n => n.OffBeat);
                var chord = notes.Select(n => (n.Note, n.Velocity)).ToList();
                notes = arp.ExpandChord(on, off, chord).ToList();
                continue;
            }

            if (fx is IMidiChordExpander chordExpander && notes.Count > 1)
            {
                var on = notes.Min(n => n.OnBeat);
                var off = notes.Max(n => n.OffBeat);
                var chord = notes.Select(n => (n.Note, n.Velocity)).ToList();
                notes = chordExpander.ExpandChord(on, off, chord).ToList();
                continue;
            }

            var next = new List<(double, double, int, float)>();
            foreach (var n in notes)
            {
                if (fx is IMidiNoteExpander expander)
                    next.AddRange(expander.ExpandHeld(n.OnBeat, n.OffBeat, n.Note, n.Velocity));
                else if (fx is ScaleMidiEffect scale)
                {
                    var snapped = scale.SnapNote(n.Note);
                    next.Add((n.OnBeat, n.OffBeat, snapped, n.Velocity));
                }
                else
                {
                    var onMsg = new MidiMessage(MidiMessageKind.NoteOn, 0, (byte)n.Note,
                        (byte)Math.Clamp((int)(n.Velocity * 127f), 1, 127));
                    var expandedOn = new List<(double, double, int, float)>();
                    foreach (var msg in fx.Process(onMsg))
                    {
                        if (msg.Kind == MidiMessageKind.NoteOn)
                            expandedOn.Add((n.OnBeat, n.OffBeat, msg.Note, msg.Velocity / 127f));
                    }

                    if (expandedOn.Count == 0)
                        next.Add((n.OnBeat, n.OffBeat, n.Note, n.Velocity));
                    else
                        next.AddRange(expandedOn);
                }
            }

            notes = next;
        }

        return notes;
    }

    private IEnumerable<MidiExpandedNote> ApplyPostEffects(
        List<(double OnBeat, double OffBeat, int Note, float Velocity)> notes,
        double bpm,
        IGrouping<double, MidiSourceNote> sourceGroup)
    {
        var humanize = _effects.OfType<HumanizeMidiEffect>().LastOrDefault(f => f.Enabled);
        var bend = _effects.OfType<BendMidiEffect>().LastOrDefault(f => f.Enabled);
        var micro = _effects.OfType<MicroPitchMidiEffect>().LastOrDefault(f => f.Enabled);

        var maxHumanizeBeats = humanize is null || humanize.TimingMs <= 0 || bpm <= 0
            ? 0
            : (humanize.TimingMs / 1000.0) * (bpm / 60.0);

        var pitchBend14 = ComputePitchBend14(bend, micro);
        var tickOffsetByNote = sourceGroup.ToDictionary(s => s.Note, s => s.HumanizeTicks);

        foreach (var n in notes)
        {
            var vel = n.Velocity;
            var timing = 0.0;

            if (tickOffsetByNote.TryGetValue(n.Note, out var ticks) && ticks != 0)
                timing += ticks / PpqPerBeat;

            if (humanize is not null)
            {
                if (humanize.VelocityAmount > 0)
                    vel = MidiHumanizer.Velocity(vel, humanize.VelocityAmount, _rng);
                if (maxHumanizeBeats > 0)
                    timing += MidiHumanizer.TimingBeats(maxHumanizeBeats, _rng);
            }

            yield return new MidiExpandedNote(n.OnBeat, n.OffBeat, n.Note, vel, timing, pitchBend14);
        }
    }

    private static int? ComputePitchBend14(BendMidiEffect? bend, MicroPitchMidiEffect? micro)
    {
        var semitones = bend is { Enabled: true } ? bend.Semitones : 0;
        var cents = micro is { Enabled: true } ? micro.Cents : 0f;
        if (semitones == 0 && Math.Abs(cents) < 0.01f) return null;

        const int bendRangeSemitones = 12;
        var fraction = Math.Clamp((semitones + cents / 100f) / bendRangeSemitones, -1f, 1f);
        return (int)Math.Clamp(8192 + fraction * 8191, 0, 16383);
    }

    private static MidiExpandedNote ToExpanded(MidiSourceNote src, double extraTiming, int? pitchBend14)
    {
        var timing = extraTiming + (src.HumanizeTicks == 0 ? 0 : src.HumanizeTicks / PpqPerBeat);
        return new MidiExpandedNote(src.OnBeat, src.OffBeat, src.Note, src.Velocity, timing, pitchBend14);
    }
}
