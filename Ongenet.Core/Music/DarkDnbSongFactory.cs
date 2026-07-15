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
/// Builds "Undertow" — the dark drum &amp; bass built-in project (F minor, 170 BPM, 96 bars). A growling
/// Field "Reese Bass" (a detuned saw unison driven into a dark low-pass) carries the drops over a
/// tight two-step groove with a hard Perca "Dark Snare"; the "Nova Saw" supersaw sketches a
/// restrained minor melody that stays behind the bass. Like every built-in song it is deterministic
/// code built from factory presets, and survives an .ongen save/load round-trip unchanged.
/// </summary>
public static class DarkDnbSongFactory
{
    public const string SongName = "Undertow";
    public const double Bpm = 170.0;

    private const int Bars = 96;

    // Section boundaries, in bars (0-based).
    private const int Intro = 0;    // pads + a distant melody hint
    private const int Groove = 8;   // two-step drums + sub roots; riser and snare roll into the drop
    private const int Drop1 = 24;   // Reese pattern A, full drums
    private const int Break = 48;   // drums out, melody carries; second riser/roll
    private const int Drop2 = 64;   // Reese pattern B (busier), ghost snares, lead echoes
    private const int Outro = 88;   // drums thin, pads close down

    // F natural minor, i–i–VI–v (Fm, Fm, Db, Cm) — one chord per bar, cycling every 4 bars.
    private static readonly int[] ReeseRoots = { 41, 41, 37, 36 }; // F2, F2, Db2, C2
    private static readonly int[] SubRoots = { 29, 29, 25, 24 };   // F1, F1, Db1, C1
    private static readonly int[][] PadChords =
    {
        new[] { 41, 48, 51, 56 }, // Fm7  (F2 C3 Eb3 Ab3)
        new[] { 41, 48, 51, 56 },
        new[] { 49, 53, 56, 60 }, // Dbmaj7 (Db3 F3 Ab3 C4)
        new[] { 48, 55, 58, 63 }  // Cm7  (C3 G3 Bb3 Eb4)
    };

    /// <summary>Builds the complete song. Requires the Field instrument to be registered
    /// (see <c>FieldBootstrap.Initialize</c>) — the Reese bass and lead are Field patches.</summary>
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
        // Club Loud suits DnB peak energy; keep Spectrum via Full Master polarity with club ceiling.
        MasteringChains.Add(master.Effects, "club");
        master.Effects.Add(new SpectrumEffect());
        master.Effects.Add(new WaveformVisualizerEffect());
        project.Tracks.Add(master);

        var kick = BuildKick();
        project.Tracks.Add(kick);
        project.Tracks.Add(BuildSnare());
        project.Tracks.Add(BuildHats());
        project.Tracks.Add(BuildReese(project, instruments, kick.Id));
        project.Tracks.Add(BuildSub(kick.Id));
        project.Tracks.Add(BuildPads(project));
        project.Tracks.Add(BuildLead(instruments));
        project.Tracks.Add(BuildRiser(project));

        CommitAll(project);
        return project;
    }

    // ---- Drums ----

    private static Track BuildKick()
    {
        var track = NewInstrumentTrack("Kick", "CatppuccinRed", 0.95, PresetInstrument(new KickaInstrument(), "DnB Kick"));

        // Two-step: downbeat + the "and of two", with a pickup at the end of every second bar.
        var twoStep = new[]
        {
            (0.0, 1.0f), (2.5, 0.95f),
            (4.0, 1.0f), (6.5, 0.95f), (7.75, 0.7f)
        };
        track.Clips.Add(DrumClip("Kick", Groove, Drop1 + 24 - Groove, twoStep, patternBars: 2));
        track.Clips.Add(DrumClip("Kick", Drop2, Outro - Drop2, twoStep, patternBars: 2));
        track.Clips.Add(DrumClip("Kick Out", Outro, 4, twoStep, patternBars: 2));
        return track;
    }

    private static Track BuildSnare()
    {
        var track = NewInstrumentTrack("Snare", "CatppuccinPeach", 0.85, PresetInstrument(new PercaInstrument(), "Dark Snare"));
        track.Effects.Add(new ReverbEffect { Mix = 0.2, RoomSize = 0.45, Damping = 0.55 });

        var backbeat = new[] { (1.0, 1.0f), (3.0, 1.0f) };
        var backbeatGhosts = new[]
        {
            (1.0, 1.0f), (3.0, 1.0f),
            (5.0, 1.0f), (6.75, 0.3f), (7.0, 1.0f), (7.5, 0.35f)
        };

        track.Clips.Add(DrumClip("Snare", Groove, Drop1 - 2 - Groove, backbeat));
        track.Clips.Add(SnareRoll(Drop1 - 2));
        track.Clips.Add(DrumClip("Snare", Drop1, 24, backbeatGhosts, patternBars: 2));
        track.Clips.Add(SnareRoll(Drop2 - 2));
        track.Clips.Add(DrumClip("Snare", Drop2, Outro - Drop2, backbeatGhosts, patternBars: 2));
        return track;
    }

    private static Track BuildHats()
    {
        var track = NewInstrumentTrack("Hats", "CatppuccinYellow", 0.55, PresetInstrument(new PercaInstrument(), "Closed Hat"));

        var sparse = new[] { (0.5, 0.4f), (1.5, 0.4f), (2.5, 0.4f), (3.5, 0.4f) };
        var shuffle = new[]
        {
            (0.0, 0.55f), (0.5, 0.35f), (0.75, 0.2f), (1.0, 0.5f), (1.5, 0.35f),
            (2.0, 0.55f), (2.25, 0.2f), (2.5, 0.35f), (3.0, 0.5f), (3.5, 0.35f), (3.75, 0.25f)
        };

        track.Clips.Add(DrumClip("Hats", Intro + 4, Groove - Intro - 4, sparse));
        track.Clips.Add(DrumClip("Hats", Groove, Drop1 - Groove, shuffle));
        track.Clips.Add(DrumClip("Hats", Drop1, 24, shuffle));
        track.Clips.Add(DrumClip("Hats", Drop2, Outro - Drop2, shuffle));

        Automate(track, null, AutomationTargetKind.TrackVolume, -1, -1,
            (Beat(Intro), 0.4), (Beat(Groove), 0.5), (Beat(Drop1), 0.6), (Beat(Drop2), 0.65));
        return track;
    }

    // ---- Bass ----

    /// <summary>Reese pattern A — the first drop's figure: syncopated stabs around the chord root
    /// with octave jumps and neighbour tones, all diatonic to F minor. 16 beats (one 4-bar cycle).</summary>
    private static readonly (double Beat, int Note, double Length)[] ReeseA =
    {
        // Bar 1 (F)
        (0.5, 41, 0.75), (1.5, 41, 0.25), (1.75, 44, 0.25), (2.0, 41, 1.0), (3.25, 39, 0.5),
        // Bar 2 (F) — octave flick
        (4.5, 41, 0.5), (5.25, 53, 0.25), (5.75, 41, 0.75), (6.75, 46, 0.25), (7.0, 44, 0.5), (7.5, 41, 0.5),
        // Bar 3 (Db)
        (8.5, 37, 0.75), (9.5, 37, 0.25), (9.75, 41, 0.25), (10.0, 37, 1.0), (11.0, 49, 0.5), (11.5, 44, 0.5),
        // Bar 4 (C) — walks back up to F
        (12.5, 36, 0.5), (13.25, 36, 0.25), (13.75, 43, 0.25), (14.0, 36, 0.75), (15.0, 39, 0.5), (15.5, 41, 0.5)
    };

    /// <summary>Reese pattern B — the second drop doubles the subdivision: 16th stutters, wider jumps.</summary>
    private static readonly (double Beat, int Note, double Length)[] ReeseB =
    {
        // Bar 1 (F)
        (0.5, 41, 0.375), (1.0, 41, 0.25), (1.25, 44, 0.25), (1.75, 41, 0.5),
        (2.5, 53, 0.25), (2.75, 51, 0.25), (3.0, 41, 0.5), (3.75, 39, 0.25),
        // Bar 2 (F)
        (4.5, 41, 0.25), (4.75, 41, 0.25), (5.25, 46, 0.25), (5.75, 44, 0.5),
        (6.5, 41, 0.25), (6.75, 48, 0.25), (7.0, 44, 0.5), (7.75, 39, 0.25),
        // Bar 3 (Db)
        (8.5, 37, 0.375), (9.0, 37, 0.25), (9.25, 41, 0.25), (9.75, 37, 0.5),
        (10.5, 49, 0.25), (10.75, 44, 0.25), (11.0, 37, 0.75), (11.75, 36, 0.25),
        // Bar 4 (C)
        (12.5, 36, 0.375), (13.0, 43, 0.25), (13.25, 36, 0.25), (13.75, 39, 0.5),
        (14.5, 36, 0.25), (14.75, 43, 0.25), (15.0, 39, 0.5), (15.75, 41, 0.25)
    };

    private static Track BuildReese(Project project, IInstrumentRegistry instruments, Guid kickTrackId)
    {
        var reese = PresetInstrument(instruments.Create(FieldInstrument.Id), "Reese Bass");
        var track = NewInstrumentTrack("Reese", "CatppuccinMauve", 0.7, reese);
        track.Effects.Add(new SidechainEffect
        {
            SourceTrackId = kickTrackId,
            Amount = 0.45,
            AttackMs = 2,
            ReleaseMs = 90
        });

        track.Clips.Add(PhraseClip("Reese A", Drop1, Break - Drop1, ReeseA, 0.9f));
        track.Clips.Add(PhraseClip("Reese B", Drop2, Outro - Drop2, ReeseB, 0.9f));

        // The duck bites harder across drop 1, staying firm for drop 2.
        Automate(track, project, AutomationTargetKind.EffectParam, 0, 0,
            (Beat(Drop1), 0.35), (Beat(Break), 0.55), (Beat(Drop2), 0.55));
        return track;
    }

    private static Track BuildSub(Guid kickTrackId)
    {
        var track = NewInstrumentTrack("Sub", "CatppuccinMaroon", 0.75, FactoryPresets.DeepSubBass());
        track.Effects.Add(new SidechainEffect
        {
            SourceTrackId = kickTrackId,
            Amount = 0.6,
            AttackMs = 2,
            ReleaseMs = 80
        });

        track.Clips.Add(SubClip("Sub", Groove, Break - Groove));
        track.Clips.Add(SubClip("Sub", Drop2, Outro - Drop2));
        return track;
    }

    private static Clip SubClip(string name, int startBar, int bars)
    {
        // One sustained root per bar under everything.
        var clip = new Clip { Name = name, StartBeat = Beat(startBar), LengthBeats = bars * BeatsPerBar, IsAudio = false };
        for (var bar = 0; bar < bars; bar++)
        {
            clip.Notes.Add(new MidiNote
            {
                Note = SubRoots[(startBar + bar) % 4],
                StartBeat = bar * BeatsPerBar,
                LengthBeats = BeatsPerBar - 0.1,
                Velocity = 0.85f
            });
        }

        return clip;
    }

    // ---- Harmony and melody ----

    private static Track BuildPads(Project project)
    {
        // Tar Pit: Padda's deep, grungy factory pad — already the right darkness for this song.
        var track = NewInstrumentTrack("Pads", "CatppuccinLavender", 0.45, PresetInstrument(new PaddaInstrument(), "Tar Pit"));
        track.Effects.Add(new FilterEffect { Mode = FilterMode.LowPass, Frequency = 500, Resonance = 0.8 });
        track.Effects.Add(new SidechainEffect { Amount = 0.4, RateIndex = 2 }); // four-to-the-bar tempo pump

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
                    Velocity = 0.65f
                });
            }
        }

        track.Clips.Add(clip);

        // Dark through the intro, opens a little for the drops, breathes wide in the break, closes out.
        Automate(track, project, AutomationTargetKind.EffectParam, 0, 1,
            (0, 500, 0.2), (Beat(Groove), 900, 0), (Beat(Drop1), 1500, 0),
            (Beat(Break), 2400, 0), (Beat(Drop2), 1500, 0),
            (Beat(Outro), 1500, 0), (Beat(Bars), 250, -0.2));
        Automate(track, project, AutomationTargetKind.TrackVolume, -1, -1,
            (0, 0.45), (Beat(Outro), 0.45), (Beat(Bars) - 2, 0.06));
        return track;
    }

    /// <summary>The 8-bar minor melody, deliberately restrained: long tones circling Ab–F–C so it
    /// haunts the background instead of fighting the bass. Diatonic to F minor.</summary>
    private static readonly (double Beat, int Note, double Length)[] LeadPhrase =
    {
        (0.0, 68, 2.0), (3.0, 67, 1.0),
        (4.0, 65, 3.0),
        (8.0, 72, 2.0), (11.0, 70, 1.0),
        (12.0, 68, 2.5),
        (16.0, 65, 1.5), (18.0, 68, 2.0),
        (20.0, 63, 3.0),
        (24.0, 72, 1.5), (26.0, 75, 2.0),
        (28.0, 72, 2.0), (30.5, 70, 1.5)
    };

    private static Track BuildLead(IInstrumentRegistry instruments)
    {
        // Nova Saw: the wide seven-voice supersaw Field patch.
        var lead = PresetInstrument(instruments.Create(FieldInstrument.Id), "Nova Saw");
        var track = NewInstrumentTrack("Lead", "CatppuccinSky", 0.55, lead);
        track.Effects.Add(new DelayEffect { TimeMs = DottedEighthMs(), Feedback = 0.4, Mix = 0.25 });
        track.Effects.Add(new ReverbEffect { Mix = 0.3, RoomSize = 0.8, Damping = 0.4 });

        // A distant hint in the intro...
        var hint = new Clip { Name = "Lead Hint", StartBeat = 0, LengthBeats = 8 * BeatsPerBar, IsAudio = false };
        foreach (var (beat, note) in new[] { (0.0, 65), (8.0, 68), (16.0, 63), (24.0, 60) })
            hint.Notes.Add(new MidiNote { Note = note, StartBeat = beat, LengthBeats = 6, Velocity = 0.5f });
        track.Clips.Add(hint);

        // ...the full phrase carrying the break...
        track.Clips.Add(PhraseClip("Lead", Break, Drop2 - Break, LeadPhrase, 0.7f));

        // ...and quiet echoes inside the second drop, sitting behind the Reese.
        track.Clips.Add(PhraseClip("Lead Echo", Drop2 + 8, 8, LeadPhrase, 0.45f));
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

        Automate(track, project, AutomationTargetKind.EffectParam, 0, 1,
            (0, 300, 0), (Beat(Drop1 - 4), 300, -0.4), (Beat(Drop1), 9500, 0), (Beat(Drop1) + 0.5, 300, 0),
            (Beat(Drop2 - 4), 300, -0.4), (Beat(Drop2), 9500, 0), (Beat(Drop2) + 0.5, 300, 0));
        Automate(track, project, AutomationTargetKind.TrackVolume, -1, -1,
            (0, 0, 0), (Beat(Drop1 - 4), 0, -0.3), (Beat(Drop1), 0.8, 0), (Beat(Drop1) + 0.5, 0, 0),
            (Beat(Drop2 - 4), 0, -0.3), (Beat(Drop2), 0.8, 0), (Beat(Drop2) + 0.5, 0, 0));
        return track;
    }

    // ---- Helpers ----

    /// <summary>Dotted-eighth delay time at the song tempo.</summary>
    private static double DottedEighthMs() => 60000.0 / Bpm * 0.75;
}
