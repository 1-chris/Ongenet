using System;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using static Ongenet.Core.Music.SongBuilder;

namespace Ongenet.Core.Music;

/// <summary>
/// Builds "Techno Starter" — a driving four-on-the-floor techno template (A minor, 130 BPM, 32 bars).
/// Punchy kick, rolling hats, a syncopated bass and sparse stabs.
/// </summary>
public static class TechnoStarterSongFactory
{
    public const string SongName = "Techno Starter";
    public const double Bpm = 130.0;

    private const int Bars = 32;

    private static readonly int[] BassRoots = { 33, 33, 28, 28 }; // A1, A1, E1, E1
    private static readonly int[][] Stabs =
    {
        new[] { 57, 60, 64 },
        new[] { 57, 60, 64 },
        new[] { 52, 55, 59 },
        new[] { 52, 55, 59 }
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
        master.Effects.Clear();
        foreach (var fx in MasteringChains.Create("techno"))
            master.Effects.Add(fx);
        project.Tracks.Add(master);

        var kick = BuildKick();
        project.Tracks.Add(kick);
        project.Tracks.Add(BuildHats());
        project.Tracks.Add(BuildBass(project, kick.Id));
        project.Tracks.Add(BuildStabs());

        CommitAll(project);
        return project;
    }

    private static Track BuildKick()
    {
        var track = NewInstrumentTrack("Kick", "CatppuccinRed", 0.98,
            PresetInstrument(new KickaInstrument(), "EDM Kick"));

        var floor = BarPattern((0.0, 1.0f), (1.0, 1.0f), (2.0, 1.0f), (3.0, 1.0f));
        track.Clips.Add(DrumClip("Kick", 0, Bars, floor));
        return track;
    }

    private static Track BuildHats()
    {
        var track = NewInstrumentTrack("Hats", "CatppuccinYellow", 0.5,
            PresetInstrument(new PercaInstrument(), "Closed Hat"));

        var pattern = BarPattern((0.0, 0.55f), (0.5, 0.7f), (1.0, 0.55f), (1.5, 0.7f),
            (2.0, 0.55f), (2.5, 0.7f), (3.0, 0.55f), (3.5, 0.75f));
        track.Clips.Add(DrumClip("Hats", 0, Bars, pattern));
        return track;
    }

    private static Track BuildBass(Project project, Guid kickId)
    {
        var track = NewInstrumentTrack("Bass", "CatppuccinGreen", 0.85, FactoryPresets.TranceBass());
        track.Effects.Add(new SidechainEffect { SourceTrackId = kickId, Amount = 0.6, AttackMs = 3, ReleaseMs = 90 });

        var clip = new Clip { Name = "Bass", StartBeat = 0, LengthBeats = Bars * BeatsPerBar, IsAudio = false };
        for (var bar = 0; bar < Bars; bar++)
        {
            var root = BassRoots[bar % BassRoots.Length];
            var start = bar * BeatsPerBar;
            clip.Notes.Add(new MidiNote { Note = root, StartBeat = start, LengthBeats = 0.35, Velocity = 0.95f });
            clip.Notes.Add(new MidiNote { Note = root, StartBeat = start + 0.75, LengthBeats = 0.35, Velocity = 0.85f });
            clip.Notes.Add(new MidiNote { Note = root, StartBeat = start + 1.5, LengthBeats = 0.35, Velocity = 0.9f });
            clip.Notes.Add(new MidiNote { Note = root, StartBeat = start + 2.25, LengthBeats = 0.35, Velocity = 0.88f });
            clip.Notes.Add(new MidiNote { Note = root, StartBeat = start + 3.0, LengthBeats = 0.35, Velocity = 0.92f });
        }

        track.Clips.Add(clip);
        return track;
    }

    private static Track BuildStabs()
    {
        var track = NewInstrumentTrack("Stabs", "CatppuccinLavender", 0.6,
            PresetInstrument(new PaddaInstrument(), "Dusk Pads"));
        track.Effects.Add(new ReverbEffect { Mix = 0.2, RoomSize = 0.55, Damping = 0.5 });

        var clip = new Clip { Name = "Stabs", StartBeat = 0, LengthBeats = Bars * BeatsPerBar, IsAudio = false };
        for (var bar = 0; bar < Bars; bar++)
        {
            if (bar % 4 != 0) continue;
            var chord = Stabs[bar % Stabs.Length];
            var start = bar * BeatsPerBar;
            foreach (var note in chord)
            {
                clip.Notes.Add(new MidiNote
                {
                    Note = note,
                    StartBeat = start,
                    LengthBeats = 0.25,
                    Velocity = 0.75f
                });
            }
        }

        track.Clips.Add(clip);
        return track;
    }
}
