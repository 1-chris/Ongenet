using System;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using static Ongenet.Core.Music.SongBuilder;

namespace Ongenet.Core.Music;

/// <summary>
/// Builds "Static Bloom" — a sparse ambient electronica sketch (E minor, 76 BPM, 40 bars). Soft Field
/// plucks over a deep pad wash and a gentle kick pulse — a minimal canvas for sound design experiments.
/// </summary>
public static class StaticBloomSongFactory
{
    public const string SongName = "Static Bloom";
    public const double Bpm = 76.0;

    private const int Bars = 40;

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
        master.Effects.Add(new LimiterEffect { CeilingDb = -1.5, ReleaseMs = 200 });
        master.Effects.Add(new ReverbEffect { Mix = 0.12, RoomSize = 0.5, Damping = 0.6 });
        project.Tracks.Add(master);

        project.Tracks.Add(BuildKick());
        project.Tracks.Add(BuildPads());
        project.Tracks.Add(BuildPluck(instruments));

        CommitAll(project);
        return project;
    }

    private static Track BuildKick()
    {
        var track = NewInstrumentTrack("Pulse", "CatppuccinRed", 0.5,
            PresetInstrument(new KickaInstrument(), "Deep House Kick"));

        var sparse = BarPattern((0.0, 0.6f), (2.0, 0.45f));
        track.Clips.Add(DrumClip("Pulse", 8, Bars - 8, sparse, patternBars: 2));
        return track;
    }

    private static Track BuildPads()
    {
        var track = NewInstrumentTrack("Pads", "CatppuccinSky", 0.55,
            PresetInstrument(new PaddaInstrument(), "Deep Space"));
        track.Effects.Add(new ReverbEffect { Mix = 0.45, RoomSize = 0.9, Damping = 0.35 });

        var clip = new Clip { Name = "Wash", StartBeat = 0, LengthBeats = Bars * BeatsPerBar, IsAudio = false };
        var notes = new[] { 40, 43, 47, 52 };
        for (var bar = 0; bar < Bars; bar++)
        {
            var start = bar * BeatsPerBar;
            foreach (var note in notes)
            {
                clip.Notes.Add(new MidiNote
                {
                    Note = note,
                    StartBeat = start,
                    LengthBeats = BeatsPerBar * 3.5,
                    Velocity = 0.45f
                });
            }
        }

        track.Clips.Add(clip);
        return track;
    }

    private static Track BuildPluck(IInstrumentRegistry instruments)
    {
        var pluck = PresetInstrument(instruments.Create(FieldInstrument.Id), "Crystal Pluck");
        var track = NewInstrumentTrack("Pluck", "CatppuccinPink", 0.72, pluck);
        track.Effects.Add(new DelayEffect { TimeMs = 480, Feedback = 0.4, Mix = 0.3 });

        var phrase = new (double Beat, int Note, double Length)[]
        {
            (0.0, 64, 0.5), (2.0, 67, 0.5), (4.0, 71, 0.75), (6.0, 69, 0.5),
            (8.0, 67, 0.5), (10.0, 64, 0.75), (12.0, 62, 0.5), (14.0, 64, 1.0)
        };
        track.Clips.Add(PhraseClip("Motif", 4, Bars - 4, phrase, 0.65f));
        return track;
    }
}
