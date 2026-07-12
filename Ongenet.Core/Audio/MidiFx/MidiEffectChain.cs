using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Core.Audio.MidiFx;

/// <summary>Runs a track's MIDI-FX chain, expanding scheduled notes for the arrangement scheduler.</summary>
public sealed class MidiEffectChain
{
    private readonly IMidiEffect[] _effects;

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

    /// <summary>
    /// Expands one held note into the note events produced by the chain (arp, echo, scale, etc.).
    /// </summary>
    public IEnumerable<(double OnBeat, double OffBeat, int Note, float Velocity)> ExpandNote(
        double onBeat, double offBeat, int note, float velocity)
    {
        var notes = new List<(double, double, int, float)> { (onBeat, offBeat, note, velocity) };
        foreach (var fx in _effects)
        {
            if (!fx.Enabled) continue;
            var next = new List<(double, double, int, float)>();
            foreach (var n in notes)
            {
                if (fx is ArpMidiEffect arp)
                    next.AddRange(arp.ExpandHeld(n.Item1, n.Item2, n.Item3, n.Item4));
                else if (fx is NoteEchoMidiEffect echo)
                    next.AddRange(echo.ExpandEchoes(n.Item1, n.Item2, n.Item3, n.Item4));
                else if (fx is ScaleMidiEffect scale)
                {
                    var snapped = scale.SnapNote(n.Item3);
                    next.Add((n.Item1, n.Item2, snapped, n.Item4));
                }
                else
                {
                    var onMsg = new MidiMessage(MidiMessageKind.NoteOn, 0, (byte)n.Item3,
                        (byte)Math.Clamp((int)(n.Item4 * 127f), 1, 127));
                    var offMsg = new MidiMessage(MidiMessageKind.NoteOff, 0, (byte)n.Item3, 0);
                    var expandedOn = new List<(double, double, int, float)>();
                    foreach (var msg in fx.Process(onMsg))
                    {
                        if (msg.Kind == MidiMessageKind.NoteOn)
                            expandedOn.Add((n.Item1, n.Item2, msg.Note, msg.Velocity));
                    }

                    if (expandedOn.Count == 0)
                        next.Add((n.Item1, n.Item2, n.Item3, n.Item4));
                    else
                        next.AddRange(expandedOn);
                }
            }

            notes = next;
        }

        return notes;
    }
}
