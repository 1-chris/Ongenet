using System;
using System.Linq;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Music;

/// <summary>
/// Shared plumbing for the code-built factory songs (<see cref="PreviewSongFactory"/>,
/// <see cref="DarkDnbSongFactory"/>): track/clip construction, preset loading by name, repeated drum
/// patterns and automation lanes bound exactly the way project load re-binds them — so every built-in
/// song behaves identically before and after an .ongen save/load round-trip.
/// </summary>
internal static class SongBuilder
{
    public const double BeatsPerBar = 4.0;

    public static double Beat(int bar) => bar * BeatsPerBar;

    public static Track NewInstrumentTrack(string name, string colorKey, double volume, IInstrument instrument)
    {
        var track = new Track { Name = name, Kind = TrackKind.Instrument, ColorKey = colorKey, Volume = volume };
        track.Instruments.Add(new InstrumentSlot(instrument) { Enabled = true });
        return track;
    }

    /// <summary>Loads a named built-in preset so the song uses exactly the values the library preset has.</summary>
    public static IInstrument PresetInstrument(IInstrument instrument, string presetName)
    {
        var provider = (IPresetProvider)instrument;
        var index = provider.PresetNames.ToList().IndexOf(presetName);
        if (index < 0) throw new InvalidOperationException($"{instrument.Name} has no built-in preset '{presetName}'.");
        provider.LoadPreset(index);
        return instrument;
    }

    /// <summary>
    /// A MIDI clip repeating a drum pattern (note 60, the reference pitch) for <paramref name="bars"/> bars.
    /// The pattern spans <paramref name="patternBars"/> bars (offsets in beats from the pattern start), so
    /// two-bar grooves and fills repeat correctly.
    /// </summary>
    public static Clip DrumClip(string name, int startBar, int bars,
        (double Offset, float Velocity)[] pattern, int patternBars = 1)
    {
        var clip = new Clip { Name = name, StartBeat = Beat(startBar), LengthBeats = bars * BeatsPerBar, IsAudio = false };
        var patternBeats = patternBars * BeatsPerBar;
        for (var repeat = 0; repeat * patternBars < bars; repeat++)
        {
            foreach (var (offset, velocity) in pattern)
            {
                var start = repeat * patternBeats + offset;
                if (start >= bars * BeatsPerBar) continue;
                clip.Notes.Add(new MidiNote { Note = 60, StartBeat = start, LengthBeats = 0.2, Velocity = velocity });
            }
        }

        return clip;
    }

    /// <summary>Repeats a fixed phrase (notes given in beats from the phrase start) across a clip.
    /// The phrase length is rounded up to whole bars so repeats stay bar-aligned.</summary>
    public static Clip PhraseClip(string name, int startBar, int bars,
        (double Beat, int Note, double Length)[] phrase, float velocity)
    {
        var clip = new Clip { Name = name, StartBeat = Beat(startBar), LengthBeats = bars * BeatsPerBar, IsAudio = false };
        var phraseBeats = Math.Ceiling(phrase.Max(p => p.Beat + p.Length) / BeatsPerBar) * BeatsPerBar;
        for (var offset = 0.0; offset < bars * BeatsPerBar; offset += phraseBeats)
        {
            foreach (var (beat, note, length) in phrase)
            {
                var start = offset + beat;
                if (start + length > bars * BeatsPerBar) continue;
                clip.Notes.Add(new MidiNote { Note = note, StartBeat = start, LengthBeats = length, Velocity = velocity });
            }
        }

        return clip;
    }

    /// <summary>The classic 16th-note snare roll, velocity climbing into the downbeat that follows.
    /// <paramref name="peak"/> scales the whole crescendo so a roll can sit under a mix without
    /// dominating it.</summary>
    public static Clip SnareRoll(int startBar, int bars = 2, float peak = 1.0f)
    {
        var clip = new Clip { Name = "Snare Roll", StartBeat = Beat(startBar), LengthBeats = bars * BeatsPerBar, IsAudio = false };
        var hits = bars * 16;
        for (var i = 0; i < hits; i++)
        {
            clip.Notes.Add(new MidiNote
            {
                Note = 60,
                StartBeat = i * 0.25,
                LengthBeats = 0.2,
                Velocity = (0.25f + 0.75f * i / (hits - 1)) * peak
            });
        }

        return clip;
    }

    /// <summary>Adds an automation lane bound exactly the way project load re-binds lanes, so the
    /// song behaves identically before and after a save/load round-trip.</summary>
    public static void Automate(Track track, Project? project, AutomationTargetKind kind,
        int effectIndex, int paramIndex, params (double Beat, double Value, double Curve)[] points)
    {
        var target = ProjectFile.BuildTarget(track, (int)kind, effectIndex, paramIndex, project)
                     ?? throw new InvalidOperationException($"Could not bind automation {kind} on '{track.Name}'.");
        var lane = new AutomationLane(target) { Binding = new AutomationBinding(kind, effectIndex, paramIndex) };
        foreach (var (beat, value, curve) in points) lane.Points.Add(new AutomationPoint(beat, value, curve));
        lane.Sort();
        track.AutoLanes.Add(lane);
    }

    public static void Automate(Track track, Project? project, AutomationTargetKind kind,
        int effectIndex, int paramIndex, params (double Beat, double Value)[] points)
        => Automate(track, project, kind, effectIndex, paramIndex,
            points.Select(p => (p.Beat, p.Value, 0.0)).ToArray());

    /// <summary>Publishes every track's instruments/effects/automation to the audio thread.</summary>
    public static void CommitAll(Project project)
    {
        foreach (var track in project.Tracks)
        {
            track.CommitInstruments();
            track.CommitEffects();
            track.CommitAutoLanes();
            track.CommitModulators();
        }
    }

    public static (double Offset, float Velocity)[] BarPattern(params (double, float)[] hits) => hits;
}
