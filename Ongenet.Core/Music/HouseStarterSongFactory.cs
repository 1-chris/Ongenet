using System;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using static Ongenet.Core.Music.SongBuilder;

namespace Ongenet.Core.Music;

/// <summary>
/// Builds "House Starter" — a compact four-on-the-floor house template (A minor, 124 BPM, 32 bars).
/// Kick, hats, a rolling bass and pad stabs give users a clean starting point to arrange from.
/// </summary>
public static class HouseStarterSongFactory
{
    public const string SongName = "House Starter";
    public const double Bpm = 124.0;

    private const int Bars = 32;

    private static readonly int[] BassRoots = { 33, 33, 29, 28 }; // A1, A1, F1, E1
    private static readonly int[][] PadStabs =
    {
        new[] { 57, 60, 64 }, // Am
        new[] { 57, 60, 64 },
        new[] { 53, 57, 60 }, // F
        new[] { 52, 55, 59 }  // Em
    };

    public static Project Create(IInstrumentRegistry instruments)
    {
        var project = new Project
        {
            Name = SongName,
            Tempo = new Tempo(Bpm),
            TimeSignature = TimeSignature.FourFour,
            BarCount = Bars
        };

        var master = new Track
        {
            Name = "Master",
            Kind = TrackKind.Master,
            ColorKey = "CatppuccinSubtext0",
            Volume = 1.0
        };
        master.Effects.Add(new LimiterEffect { CeilingDb = -0.5, ReleaseMs = 100 });
        project.Tracks.Add(master);

        var kick = BuildKick();
        project.Tracks.Add(kick);
        project.Tracks.Add(BuildHats());
        project.Tracks.Add(BuildBass(project, kick.Id));
        project.Tracks.Add(BuildPads(project));

        CommitAll(project);
        return project;
    }

    private static Track BuildKick()
    {
        var track = NewInstrumentTrack("Kick", "CatppuccinRed", 0.95,
            PresetInstrument(new KickaInstrument(), "Deep House Kick"));

        var floor = BarPattern((0.0, 1.0f), (1.0, 1.0f), (2.0, 1.0f), (3.0, 1.0f));
        track.Clips.Add(DrumClip("Kick", 0, Bars, floor));
        return track;
    }

    private static Track BuildHats()
    {
        var track = NewInstrumentTrack("Hats", "CatppuccinYellow", 0.55,
            PresetInstrument(new PercaInstrument(), "Closed Hat"));

        var offbeats = BarPattern((0.5, 0.6f), (1.5, 0.65f), (2.5, 0.6f), (3.5, 0.65f));
        track.Clips.Add(DrumClip("Hats", 0, Bars, offbeats));
        return track;
    }

    private static Track BuildBass(Project project, Guid kickId)
    {
        var track = NewInstrumentTrack("Bass", "CatppuccinTeal", 0.82, FactoryPresets.TranceBass());
        track.Effects.Add(new SidechainEffect { SourceTrackId = kickId, Amount = 0.55, AttackMs = 5, ReleaseMs = 120 });

        var clip = new Clip { Name = "Bass", StartBeat = 0, LengthBeats = Bars * BeatsPerBar, IsAudio = false };
        for (var bar = 0; bar < Bars; bar++)
        {
            var root = BassRoots[bar % BassRoots.Length];
            var start = bar * BeatsPerBar;
            clip.Notes.Add(new MidiNote { Note = root, StartBeat = start, LengthBeats = 0.45, Velocity = 0.95f });
            clip.Notes.Add(new MidiNote { Note = root, StartBeat = start + 0.5, LengthBeats = 0.45, Velocity = 0.85f });
            clip.Notes.Add(new MidiNote { Note = root, StartBeat = start + 1.0, LengthBeats = 0.45, Velocity = 0.9f });
            clip.Notes.Add(new MidiNote { Note = root, StartBeat = start + 1.5, LengthBeats = 0.45, Velocity = 0.8f });
            clip.Notes.Add(new MidiNote { Note = root, StartBeat = start + 2.0, LengthBeats = 0.45, Velocity = 0.9f });
            clip.Notes.Add(new MidiNote { Note = root, StartBeat = start + 2.5, LengthBeats = 0.45, Velocity = 0.85f });
            clip.Notes.Add(new MidiNote { Note = root, StartBeat = start + 3.0, LengthBeats = 0.45, Velocity = 0.9f });
            clip.Notes.Add(new MidiNote { Note = root, StartBeat = start + 3.5, LengthBeats = 0.45, Velocity = 0.75f });
        }

        track.Clips.Add(clip);
        return track;
    }

    private static Track BuildPads(Project project)
    {
        var track = NewInstrumentTrack("Pads", "CatppuccinMauve", 0.65,
            PresetInstrument(new PaddaInstrument(), "Dusk Pads"));
        track.Effects.Add(new ReverbEffect { Mix = 0.28, RoomSize = 0.7, Damping = 0.4 });

        var clip = new Clip { Name = "Stabs", StartBeat = 0, LengthBeats = Bars * BeatsPerBar, IsAudio = false };
        for (var bar = 0; bar < Bars; bar++)
        {
            if (bar % 2 != 0) continue;
            var chord = PadStabs[bar % PadStabs.Length];
            var start = bar * BeatsPerBar;
            foreach (var note in chord)
            {
                clip.Notes.Add(new MidiNote
                {
                    Note = note,
                    StartBeat = start,
                    LengthBeats = 0.35,
                    Velocity = 0.7f
                });
            }
        }

        track.Clips.Add(clip);

        Automate(track, project, AutomationTargetKind.TrackVolume, -1, -1,
            (0, 0.5), (Beat(4), 0.65), (Beat(Bars - 4), 0.65), (Beat(Bars), 0.2));
        return track;
    }
}
