using System;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using static Ongenet.Core.Music.SongBuilder;

namespace Ongenet.Core.Music;

/// <summary>
/// Builds "Trap Beat" — a modern trap sketch (F minor, 140 BPM, 32 bars). 808 kick, snare/clap,
/// rolling hi-hats and a sub bass with sparse melody hits.
/// </summary>
public static class TrapBeatSongFactory
{
    public const string SongName = "Trap Beat";
    public const double Bpm = 140.0;

    private const int Bars = 32;

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
        MasteringChains.Add(master.Effects, "club");
        master.Effects.Add(new SpectrumEffect());
        project.Tracks.Add(master);

        project.Tracks.Add(BuildKick808());
        project.Tracks.Add(BuildSnare());
        project.Tracks.Add(BuildHats());
        project.Tracks.Add(BuildSub());

        CommitAll(project);
        return project;
    }

    private static Track BuildKick808()
    {
        var track = NewInstrumentTrack("808", "CatppuccinRed", 0.92,
            PresetInstrument(new KickaInstrument(), "Deep House Kick"));

        var pattern = BarPattern((0.0, 1.0f), (1.5, 0.85f), (2.75, 0.9f));
        track.Clips.Add(DrumClip("808", 0, Bars, pattern, patternBars: 2));
        return track;
    }

    private static Track BuildSnare()
    {
        var track = NewInstrumentTrack("Snare", "CatppuccinPeach", 0.78,
            PresetInstrument(new PercaInstrument(), "Dark Snare"));

        var backbeat = BarPattern((1.0, 0.95f), (3.0, 0.9f));
        track.Clips.Add(DrumClip("Snare", 0, Bars, backbeat));
        return track;
    }

    private static Track BuildHats()
    {
        var track = NewInstrumentTrack("Hats", "CatppuccinYellow", 0.45,
            PresetInstrument(new PercaInstrument(), "Closed Hat"));

        var roll = BarPattern(
            (0.0, 0.5f), (0.25, 0.45f), (0.5, 0.55f), (0.75, 0.5f),
            (1.0, 0.55f), (1.25, 0.5f), (1.5, 0.6f), (1.75, 0.55f),
            (2.0, 0.5f), (2.25, 0.45f), (2.5, 0.55f), (2.75, 0.5f),
            (3.0, 0.55f), (3.25, 0.5f), (3.5, 0.65f), (3.75, 0.6f));
        track.Clips.Add(DrumClip("Hats", 0, Bars, roll));
        return track;
    }

    private static Track BuildSub()
    {
        var track = NewInstrumentTrack("Sub", "CatppuccinTeal", 0.88, FactoryPresets.TranceBass());

        var clip = new Clip { Name = "Sub", StartBeat = 0, LengthBeats = Bars * BeatsPerBar, IsAudio = false };
        var roots = new[] { 29, 29, 26, 24 }; // F1, F1, D1, C1
        for (var bar = 0; bar < Bars; bar++)
        {
            var root = roots[bar % roots.Length];
            var start = bar * BeatsPerBar;
            clip.Notes.Add(new MidiNote { Note = root, StartBeat = start, LengthBeats = 1.75, Velocity = 0.92f });
            if (bar % 2 == 1)
                clip.Notes.Add(new MidiNote { Note = root + 7, StartBeat = start + 2.0, LengthBeats = 0.5, Velocity = 0.7f });
        }

        track.Clips.Add(clip);
        return track;
    }
}
