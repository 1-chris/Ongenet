using System;
using System.Linq;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using static Ongenet.Core.Music.SongBuilder;

namespace Ongenet.Core.Music;

/// <summary>
/// Builds "Dust &amp; Vinyl" — a mellow lo-fi hip-hop sketch (D minor, 88 BPM, 48 bars). Dusty drums,
/// velvet keys and a sub bass with a slow volume LFO on the keys for a drifting feel. Built from factory
/// presets so every sound is available in the library.
/// </summary>
public static class LoFiBeatSongFactory
{
    public const string SongName = "Dust & Vinyl";
    public const double Bpm = 88.0;

    private const int Bars = 48;

    private static readonly int[] ChordRoots = { 38, 36, 34, 33 }; // D2, C2, Bb1, A1
    private static readonly int[][] KeyChords =
    {
        new[] { 50, 53, 57, 60 }, // Dm7
        new[] { 48, 51, 55, 58 }, // Cmaj7
        new[] { 46, 50, 53, 57 }, // Bbmaj7
        new[] { 45, 48, 52, 55 }  // Am7
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
        // Gentle Streaming Master — less width/clipper than Full Master for dusty material.
        MasteringChains.Add(master.Effects, "streaming");
        project.Tracks.Add(master);

        project.Tracks.Add(BuildKick());
        project.Tracks.Add(BuildSnare());
        project.Tracks.Add(BuildHats());
        project.Tracks.Add(BuildKeys(project));
        project.Tracks.Add(BuildBass());

        CommitAll(project);
        return project;
    }

    private static Track BuildKick()
    {
        var track = NewInstrumentTrack("Kick", "CatppuccinRed", 0.85,
            PresetInstrument(new KickaInstrument(), "Deep House Kick"));

        var pattern = BarPattern((0.0, 0.9f), (2.0, 0.75f));
        track.Clips.Add(DrumClip("Kick", 0, Bars, pattern, patternBars: 2));
        return track;
    }

    private static Track BuildSnare()
    {
        var track = NewInstrumentTrack("Snare", "CatppuccinPeach", 0.65,
            PresetInstrument(new PercaInstrument(), "Dark Snare"));
        track.Effects.Add(new ReverbEffect { Mix = 0.22, RoomSize = 0.6, Damping = 0.55 });

        var backbeat = BarPattern((1.0, 0.8f), (3.0, 0.75f));
        track.Clips.Add(DrumClip("Snare", 4, Bars - 4, backbeat));
        return track;
    }

    private static Track BuildHats()
    {
        var track = NewInstrumentTrack("Hats", "CatppuccinYellow", 0.45,
            PresetInstrument(new PercaInstrument(), "Closed Hat"));

        var shuffle = BarPattern((0.5, 0.35f), (1.5, 0.5f), (2.5, 0.35f), (3.5, 0.45f));
        track.Clips.Add(DrumClip("Hats", 0, Bars, shuffle));
        return track;
    }

    private static Track BuildKeys(Project project)
    {
        var track = NewInstrumentTrack("Keys", "CatppuccinLavender", 0.7,
            PresetInstrument(new PaddaInstrument(), "Velvet Strings"));
        track.Effects.Add(new FilterEffect { Mode = FilterMode.LowPass, Frequency = 2200, Resonance = 0.4 });
        track.Effects.Add(new ReverbEffect { Mix = 0.35, RoomSize = 0.85, Damping = 0.45 });

        var clip = new Clip { Name = "Chords", StartBeat = 0, LengthBeats = Bars * BeatsPerBar, IsAudio = false };
        for (var bar = 0; bar < Bars; bar++)
        {
            var chord = KeyChords[bar % KeyChords.Length];
            var start = bar * BeatsPerBar;
            foreach (var note in chord)
            {
                clip.Notes.Add(new MidiNote
                {
                    Note = note,
                    StartBeat = start,
                    LengthBeats = BeatsPerBar * 0.95,
                    Velocity = 0.55f
                });
            }
        }

        track.Clips.Add(clip);

        track.Modulators.Add(new TrackModulator
        {
            Enabled = true,
            RateHz = 0.12,
            Depth = 0.35,
            Target = new AutomationBinding(AutomationTargetKind.TrackVolume, -1, -1)
        });

        Automate(track, project, AutomationTargetKind.EffectParam, 0, 1,
            (0, 800, 0.3), (Beat(8), 2800, 0.2), (Beat(Bars), 600, -0.2));
        return track;
    }

    private static Track BuildBass()
    {
        var track = NewInstrumentTrack("Bass", "CatppuccinBlue", 0.8, FactoryPresets.DeepSubBass());

        var clip = new Clip { Name = "Roots", StartBeat = 0, LengthBeats = Bars * BeatsPerBar, IsAudio = false };
        for (var bar = 0; bar < Bars; bar++)
        {
            var root = ChordRoots[bar % ChordRoots.Length];
            clip.Notes.Add(new MidiNote
            {
                Note = root,
                StartBeat = bar * BeatsPerBar,
                LengthBeats = BeatsPerBar * 0.9,
                Velocity = 0.85f
            });
        }

        track.Clips.Add(clip);
        return track;
    }
}
