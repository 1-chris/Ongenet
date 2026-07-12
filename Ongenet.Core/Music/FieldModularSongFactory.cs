using System;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using static Ongenet.Core.Music.SongBuilder;

namespace Ongenet.Core.Music;

/// <summary>
/// Builds "Field Modular" — a minimal modular-synth sketch using Field instruments (D minor, 120 BPM, 24 bars).
/// Sequenced kick pulse, evolving pad wash and a melodic Field pluck line.
/// </summary>
public static class FieldModularSongFactory
{
    public const string SongName = "Field Modular";
    public const double Bpm = 120.0;

    private const int Bars = 24;

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
        master.Effects.Add(new LimiterEffect { CeilingDb = -1.0, ReleaseMs = 120 });
        master.Effects.Add(new ReverbEffect { Mix = 0.15, RoomSize = 0.6, Damping = 0.5 });
        project.Tracks.Add(master);

        project.Tracks.Add(BuildKick());
        project.Tracks.Add(BuildPad());
        project.Tracks.Add(BuildLead(instruments));

        CommitAll(project);
        return project;
    }

    private static Track BuildKick()
    {
        var track = NewInstrumentTrack("Pulse", "CatppuccinRed", 0.7,
            PresetInstrument(new KickaInstrument(), "Deep House Kick"));

        var sparse = BarPattern((0.0, 0.75f), (2.0, 0.55f));
        track.Clips.Add(DrumClip("Pulse", 0, Bars, sparse, patternBars: 2));
        return track;
    }

    private static Track BuildPad()
    {
        var pad = PresetInstrument(new PaddaInstrument(), "Deep Space");
        var track = NewInstrumentTrack("Pad", "CatppuccinSky", 0.6, pad);
        track.Effects.Add(new ReverbEffect { Mix = 0.35, RoomSize = 0.85, Damping = 0.4 });

        var clip = new Clip { Name = "Wash", StartBeat = 0, LengthBeats = Bars * BeatsPerBar, IsAudio = false };
        var notes = new[] { 38, 41, 45, 50 }; // Dm chord tones
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
                    Velocity = 0.5f
                });
            }
        }

        track.Clips.Add(clip);
        return track;
    }

    private static Track BuildLead(IInstrumentRegistry instruments)
    {
        var pluck = PresetInstrument(instruments.Create(FieldInstrument.Id), "Crystal Pluck");
        var track = NewInstrumentTrack("Lead", "CatppuccinPink", 0.75, pluck);
        track.Effects.Add(new DelayEffect { TimeMs = 375, Feedback = 0.35, Mix = 0.28 });

        var phrase = new (double Beat, int Note, double Length)[]
        {
            (0.0, 62, 0.5), (1.0, 65, 0.5), (2.0, 69, 0.75), (3.0, 67, 0.5),
            (4.0, 65, 0.5), (5.0, 62, 0.75), (6.0, 60, 0.5), (7.0, 62, 1.0)
        };
        track.Clips.Add(PhraseClip("Motif", 4, Bars - 4, phrase, 0.7f));
        return track;
    }
}
