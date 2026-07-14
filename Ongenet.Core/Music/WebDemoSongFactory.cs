using System;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using static Ongenet.Core.Music.SongBuilder;

namespace Ongenet.Core.Music;

/// <summary>
/// Builds "Web Demo" — a barebones house sketch for the browser/Android startup path
/// (A minor, 124 BPM, 16 bars). Kick + hats + quarter-note bass only; no pads, reverb, or
/// sidechain so the main-thread ScriptProcessor path has as little DSP as possible.
/// </summary>
public static class WebDemoSongFactory
{
    public const string SongName = "Web Demo";
    public const double Bpm = 124.0;

    private const int Bars = 16;

    private static readonly int[] BassRoots = { 33, 33, 29, 28 }; // A1, A1, F1, E1

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
        // Soft ceiling only — cheaper than a dense FX graph on the web path.
        master.Effects.Add(new LimiterEffect { CeilingDb = -0.5, ReleaseMs = 100 });
        project.Tracks.Add(master);

        project.Tracks.Add(BuildKick());
        project.Tracks.Add(BuildHats());
        project.Tracks.Add(BuildBass());

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

        var offbeats = BarPattern((0.5, 0.55f), (1.5, 0.6f), (2.5, 0.55f), (3.5, 0.6f));
        track.Clips.Add(DrumClip("Hats", 0, Bars, offbeats));
        return track;
    }

    private static Track BuildBass()
    {
        // Quarter notes only (4 hits/bar) — House Starter's 8ths were twice the voice cost.
        var track = NewInstrumentTrack("Bass", "CatppuccinTeal", 0.85, FactoryPresets.TranceBass());

        var clip = new Clip { Name = "Bass", StartBeat = 0, LengthBeats = Bars * BeatsPerBar, IsAudio = false };
        for (var bar = 0; bar < Bars; bar++)
        {
            var root = BassRoots[bar % BassRoots.Length];
            var start = bar * BeatsPerBar;
            for (var beat = 0; beat < 4; beat++)
            {
                clip.Notes.Add(new MidiNote
                {
                    Note = root,
                    StartBeat = start + beat,
                    LengthBeats = 0.7,
                    Velocity = beat == 0 ? 0.95f : 0.8f
                });
            }
        }

        track.Clips.Add(clip);
        return track;
    }
}
