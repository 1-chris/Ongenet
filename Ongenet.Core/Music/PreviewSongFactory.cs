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
/// Builds "First Light" — the deep progressive house preview song shown when the app opens without a
/// project (C Major, 128 BPM, 64 bars). Every sound is a factory preset (Kicka "Deep House Kick",
/// Perca clap/hats, Padda "Dusk Pads", the Field "Prism Lead" wavetable patch with its 3D scope, and
/// the 3x Osc bass/riser from <see cref="FactoryPresets"/>), so users can pull the exact same sounds
/// from the library. The whole arrangement — notes, automation, kick-triggered sidechain — is plain
/// deterministic code: rebuilding always produces the same song.
/// </summary>
public static class PreviewSongFactory
{
    public const string SongName = "First Light";
    public const double Bpm = 128.0;

    private const int Bars = 64;

    // Section boundaries, in bars (0-based).
    private const int Intro = 0;      // pads + hats, kick joins half-way
    private const int Build = 8;      // kick/clap/bass in, riser last 4 bars
    private const int Drop1 = 16;     // everything + lead melody
    private const int Bridge = 32;    // drums out, sparse lead, riser last 4 bars
    private const int Drop2 = 40;     // full again, octave lead layer half-way
    private const int Outro = 56;     // drums thin out, pads fade

    // C major (I–vi–IV–V), one chord per bar, cycling every 4 bars.
    private static readonly int[] ChordRoots = { 36, 33, 29, 31 }; // C2, A1, F1, G1
    private static readonly int[][] PadChords =
    {
        new[] { 48, 52, 55, 59 }, // Cmaj7
        new[] { 45, 48, 52, 55 }, // Am7
        new[] { 41, 45, 48, 52 }, // Fmaj7
        new[] { 43, 47, 50, 53 }  // G7
    };

    /// <summary>Builds the complete preview song. Requires the Field instrument to be registered
    /// (see <c>FieldBootstrap.Initialize</c>), since the lead is a Field patch.</summary>
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
        // The 3D scope on the master puts the waveform trail on screen the moment the song plays.
        master.Effects.Add(new LimiterEffect { CeilingDb = -0.5, ReleaseMs = 120 });
        master.Effects.Add(new WaveformVisualizerEffect());
        project.Tracks.Add(master);

        var kick = BuildKick();
        project.Tracks.Add(kick);
        project.Tracks.Add(BuildClap());
        project.Tracks.Add(BuildClosedHat());
        project.Tracks.Add(BuildOpenHat());
        project.Tracks.Add(BuildBass(project, kick.Id));
        project.Tracks.Add(BuildPads(project));
        project.Tracks.Add(BuildLead(instruments));
        project.Tracks.Add(BuildRiser(project));

        CommitAll(project);
        return project;
    }

    // ---- Tracks ----

    private static Track BuildKick()
    {
        var track = NewInstrumentTrack("Kick", "CatppuccinRed", 0.95, PresetInstrument(new KickaInstrument(), "Techno Thump"));

        // Four-on-the-floor. The kick sits out of the intro's first half and the bridge.
        var floor = BarPattern((0.0, 1.0f), (1.0, 1.0f), (2.0, 1.0f), (3.0, 1.0f));
        track.Clips.Add(DrumClip("Kick In", Intro + 4, 4, floor));
        track.Clips.Add(DrumClip("Kick", Build, Drop1 + 16 - Build, floor));
        track.Clips.Add(DrumClip("Kick", Drop2, Outro - Drop2, floor));
        track.Clips.Add(DrumClip("Kick Out", Outro, 4, floor));
        return track;
    }

    private static Track BuildClap()
    {
        var track = NewInstrumentTrack("Clap", "CatppuccinPeach", 0.7, PresetInstrument(new PercaInstrument(), "House Clap"));
        track.Effects.Add(new ReverbEffect { Mix = 0.18, RoomSize = 0.55, Damping = 0.5 });

        var backbeat = BarPattern((1.0, 0.95f), (3.0, 0.95f));
        track.Clips.Add(DrumClip("Clap", Build, Drop1 + 16 - Build, backbeat));
        track.Clips.Add(DrumClip("Clap", Drop2, Outro - Drop2, backbeat));
        return track;
    }

    private static Track BuildClosedHat()
    {
        var track = NewInstrumentTrack("Hat Closed", "CatppuccinYellow", 0.6, PresetInstrument(new PercaInstrument(), "Closed Hat"));

        var offbeats = BarPattern((0.5, 0.55f), (1.5, 0.55f), (2.5, 0.55f), (3.5, 0.55f));
        var eighths = BarPattern(
            (0.0, 0.4f), (0.5, 0.75f), (1.0, 0.4f), (1.5, 0.75f),
            (2.0, 0.4f), (2.5, 0.75f), (3.0, 0.4f), (3.5, 0.75f));
        var sixteenths = BarPattern(Enumerable.Range(0, 16)
            .Select(i => (i * 0.25, (i % 4) switch { 0 => 0.4f, 2 => 0.7f, _ => 0.3f })).ToArray());

        track.Clips.Add(DrumClip("Hats", Intro, Build - Intro, offbeats));
        track.Clips.Add(DrumClip("Hats", Build, Drop1 - Build, eighths));
        track.Clips.Add(DrumClip("Hats", Drop1, 16, eighths));
        track.Clips.Add(DrumClip("Hats", Bridge, Drop2 - Bridge, offbeats));
        track.Clips.Add(DrumClip("Hats 16ths", Drop2, Outro - Drop2, sixteenths));
        track.Clips.Add(DrumClip("Hats", Outro, 4, offbeats));

        // A gentle lift out of each build into the drop.
        Automate(track, null, AutomationTargetKind.TrackVolume, -1, -1,
            (Beat(Intro), 0.6), (Beat(Build), 0.5), (Beat(Drop1), 0.75),
            (Beat(Bridge), 0.55), (Beat(Drop2), 0.8));
        return track;
    }

    private static Track BuildOpenHat()
    {
        var track = NewInstrumentTrack("Hat Open", "CatppuccinGreen", 0.55, PresetInstrument(new PercaInstrument(), "Open Hat"));

        var offbeats = BarPattern((0.5, 0.8f), (1.5, 0.8f), (2.5, 0.8f), (3.5, 0.8f));
        track.Clips.Add(DrumClip("Open Hat", Drop1, 16, offbeats));
        track.Clips.Add(DrumClip("Open Hat", Drop2, Outro - Drop2, offbeats));
        return track;
    }

    private static Track BuildBass(Project project, Guid kickTrackId)
    {
        var track = NewInstrumentTrack("Bass", "CatppuccinMauve", 0.8,
            PresetInstrument(new BassSynthInstrument(), "Deep Sub"));

        // Kick-triggered sidechain: the classic pumping deep-house low end.
        track.Effects.Add(new SidechainEffect
        {
            SourceTrackId = kickTrackId,
            Amount = 0.8,
            AttackMs = 4,
            ReleaseMs = 180
        });

        track.Clips.Add(BassClip("Bass", Build, Drop1 + 16 - Build));
        track.Clips.Add(BassClip("Bass", Drop2, Outro - Drop2));

        // The duck eases deeper across the first drop, then stays firm for the second.
        Automate(track, project, AutomationTargetKind.EffectParam, 0, 0,
            (Beat(Build), 0.6), (Beat(Drop1), 0.6), (Beat(Drop1 + 16), 0.85), (Beat(Drop2), 0.85));
        return track;
    }

    private static Track BuildPads(Project project)
    {
        var track = NewInstrumentTrack("Pads", "CatppuccinLavender", 0.7, PresetInstrument(new PaddaInstrument(), "Dusk Pads"));
        track.Effects.Add(new FilterEffect { Mode = FilterMode.LowPass, Frequency = 400, Resonance = 0.9 });
        track.Effects.Add(new SidechainEffect { Amount = 0.3, RateIndex = 2 }); // light tempo pump, 1/4

        // One chord per bar for the whole song; the filter and volume shape the sections.
        var clip = new Clip { Name = "Pads", StartBeat = 0, LengthBeats = Bars * BeatsPerBar, IsAudio = false };
        for (var bar = 0; bar < Bars; bar++)
        {
            foreach (var note in PadChords[bar % 4])
            {
                clip.Notes.Add(new MidiNote
                {
                    Note = note,
                    StartBeat = bar * BeatsPerBar,
                    LengthBeats = BeatsPerBar - 0.05,
                    Velocity = 0.7f
                });
            }
        }

        track.Clips.Add(clip);

        // Filter opens across the intro/build, breathes through the song and closes in the outro.
        Automate(track, project, AutomationTargetKind.EffectParam, 0, 1,
            (0, 400, 0.2), (Beat(Build), 1200, 0), (Beat(Drop1), 2600, 0),
            (Beat(Outro), 2600, 0), (Beat(Bars), 300, -0.2));
        Automate(track, project, AutomationTargetKind.TrackVolume, -1, -1,
            (0, 0.7), (Beat(Outro), 0.7), (Beat(Bars) - 2, 0.1));
        return track;
    }

    private static Track BuildLead(IInstrumentRegistry instruments)
    {
        // The Field lead: "Prism Lead" is the wavetable-plus-3D-scope patch, loaded from the same
        // built-in patch list users see in the Field preset picker.
        var lead = PresetInstrument(instruments.Create(FieldInstrument.Id), "Prism Lead");
        var track = NewInstrumentTrack("Lead", "CatppuccinSky", 0.75, lead);
        track.Effects.Add(new DelayEffect { TimeMs = DottedEighthMs(), Feedback = 0.35, Mix = 0.22 });
        track.Effects.Add(new ReverbEffect { Mix = 0.25, RoomSize = 0.75, Damping = 0.35 });

        track.Clips.Add(MelodyClip("Lead", Drop1, withOctave: false));
        track.Clips.Add(BridgeLeadClip());
        track.Clips.Add(MelodyClip("Lead", Drop2, withOctave: true));
        return track;
    }

    private static Track BuildRiser(Project project)
    {
        var track = NewInstrumentTrack("Riser", "CatppuccinPink", 0.0, FactoryPresets.WhiteRiser());
        track.Effects.Add(new FilterEffect { Mode = FilterMode.LowPass, Frequency = 300, Resonance = 2.0 });

        foreach (var startBar in new[] { Drop1 - 4, Drop2 - 4 })
        {
            var clip = new Clip
            {
                Name = "Riser",
                StartBeat = Beat(startBar),
                LengthBeats = 4 * BeatsPerBar,
                IsAudio = false
            };
            clip.Notes.Add(new MidiNote { Note = 60, StartBeat = 0, LengthBeats = 4 * BeatsPerBar - 0.1, Velocity = 0.9f });
            track.Clips.Add(clip);
        }

        // Filter sweep + volume swell into each drop, snapping shut right after the downbeat.
        Automate(track, project, AutomationTargetKind.EffectParam, 0, 1,
            (0, 300, 0), (Beat(Drop1 - 4), 300, -0.4), (Beat(Drop1), 9000, 0), (Beat(Drop1) + 0.5, 300, 0),
            (Beat(Drop2 - 4), 300, -0.4), (Beat(Drop2), 9000, 0), (Beat(Drop2) + 0.5, 300, 0));
        Automate(track, project, AutomationTargetKind.TrackVolume, -1, -1,
            (0, 0, 0), (Beat(Drop1 - 4), 0, -0.3), (Beat(Drop1), 0.85, 0), (Beat(Drop1) + 0.5, 0, 0),
            (Beat(Drop2 - 4), 0, -0.3), (Beat(Drop2), 0.85, 0), (Beat(Drop2) + 0.5, 0, 0));
        return track;
    }

    // ---- Musical content ----

    /// <summary>The 8-bar lead phrase, diatonic to C major over the C–Am–F–G cycle. Kept sparse and
    /// mid-register (two or three long tones per bar) so it floats over the pads rather than nagging.
    /// Beats are phrase-relative; the phrase is laid down twice per 16-bar drop.</summary>
    private static readonly (double Beat, int Note, double Length)[] LeadPhrase =
    {
        // Bars 1-2 (C) — a slow rise to the major seventh
        (0.0, 64, 1.5), (2.0, 67, 2.0),
        (4.0, 72, 3.0), (7.5, 71, 0.5),
        // Bars 3-4 (Am) — answered a third higher
        (8.0, 69, 1.5), (10.0, 72, 2.0),
        (12.0, 76, 3.0), (15.0, 74, 1.0),
        // Bars 5-6 (F) — settling back down
        (16.0, 72, 1.5), (18.0, 69, 2.0),
        (20.0, 65, 3.0), (23.0, 67, 1.0),
        // Bars 7-8 (G) — B natural leads back to C
        (24.0, 67, 1.5), (26.0, 71, 2.0),
        (28.0, 74, 2.0), (30.0, 72, 1.0), (31.0, 71, 1.0)
    };

    private static Clip MelodyClip(string name, int startBar, bool withOctave)
    {
        var clip = new Clip { Name = name, StartBeat = Beat(startBar), LengthBeats = 16 * BeatsPerBar, IsAudio = false };
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var offset = repeat * 8 * BeatsPerBar;
            foreach (var (beat, note, length) in LeadPhrase)
            {
                clip.Notes.Add(new MidiNote { Note = note, StartBeat = offset + beat, LengthBeats = length, Velocity = 0.75f });

                // The second drop doubles its second half an octave up for the final lift.
                if (withOctave && repeat == 1)
                    clip.Notes.Add(new MidiNote { Note = note + 12, StartBeat = offset + beat, LengthBeats = length, Velocity = 0.4f });
            }
        }

        return clip;
    }

    private static Clip BridgeLeadClip()
    {
        // Long, sparse tones floating over the pads while the drums rest.
        var clip = new Clip { Name = "Lead Sparse", StartBeat = Beat(Bridge), LengthBeats = 8 * BeatsPerBar, IsAudio = false };
        foreach (var (beat, note) in new[] { (0.0, 67), (8.0, 69), (16.0, 64), (24.0, 65) })
            clip.Notes.Add(new MidiNote { Note = note, StartBeat = beat, LengthBeats = 6, Velocity = 0.55f });
        return clip;
    }

    private static Clip BassClip(string name, int startBar, int bars)
    {
        // Offbeat eighths on the chord root — the pocket between the kicks.
        var clip = new Clip { Name = name, StartBeat = Beat(startBar), LengthBeats = bars * BeatsPerBar, IsAudio = false };
        for (var bar = 0; bar < bars; bar++)
        {
            var root = ChordRoots[(startBar + bar) % 4];
            for (var b = 0; b < 4; b++)
            {
                clip.Notes.Add(new MidiNote
                {
                    Note = root,
                    StartBeat = bar * BeatsPerBar + b + 0.5,
                    LengthBeats = 0.4,
                    Velocity = 0.9f
                });
            }
        }

        return clip;
    }

    // ---- Helpers ----

    /// <summary>Dotted-eighth delay time at the song tempo (the staple house delay).</summary>
    private static double DottedEighthMs() => 60000.0 / Bpm * 0.75;

    private static (double Offset, float Velocity)[] BarPattern(params (double, float)[] hits) => hits;
}
