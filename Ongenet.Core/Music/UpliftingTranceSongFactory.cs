using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using static Ongenet.Core.Music.SongBuilder;

namespace Ongenet.Core.Music;

/// <summary>
/// Builds "Ascension" — modern Japanese uplifting progressive trance (Oda / Otographic style) in
/// A minor at 138 BPM, 224 bars ≈ 6:30. The harmony is the signature non-repetitive 8-bar loop
/// Fmaj7 – G6 – Am – Em/G – Fmaj7 – G – Esus4 – E: it opens on the major VI (yearning), floats up
/// through VII, grounds on the tonic, and turns around through a suspended dominant that refuses to
/// resolve cleanly. The lead is a dense call-and-response melody (a 2-bar call answered an octave up)
/// with strong voice leading — a held tone recoloured as the chords move beneath it — over a
/// 1/16 pluck arp. Supersaw wall in the climaxes only; a whole-step lift to B minor in the final
/// statement resolves home for the outro.
/// </summary>
public static class UpliftingTranceSongFactory
{
    public const string SongName = "Ascension";
    public const double Bpm = 138.0;

    private const int Bars = 224;

    // Section boundaries, in bars (0-based).
    private const int Intro = 0;        // kick + rolling bass, hats join half-way
    private const int Groove = 16;      // clap/open hats/plucks; pads swell in
    private const int Break1 = 32;      // drums out — theme introduced, arp joins, riser + roll
    private const int Climax1 = 64;     // first anthem: theme + supersaw + arp + full drums
    private const int Mid = 96;         // progressive mid-section: groove + plucks only
    private const int Break2 = 112;     // the big breakdown: theme variation, counter-melody, arp
    private const int Climax2 = 152;    // biggest section: octave layer + counter-melody
    private const int Outro = 184;      // layers peel away, filters close

    // The Oda-signature 8-bar loop in A minor (non-repetitive, jazz-tinged, no clean resolution):
    // Fmaj7 – G6 – Am – Em/G – Fmaj7 – G – Esus4 – E. VI opens with yearning, VII floats up, the
    // tonic grounds, an inverted Em/G softens the bass, then a suspended dominant turnaround
    // (Esus4 → E, with its G# leading tone) demands the loop restart. One chord per bar.

    /// <summary>Authored directly in A minor (harmonic-minor G# appears only in the E dominant).</summary>
    private const int Transpose = 0;

    /// <summary>Final climax statement lifts a whole step to B minor (bars 168–184), then the
    /// outro resolves back to A minor.</summary>
    private const int KeyChangeBar = Climax2 + 16;
    private const int KeyChangeLift = 2;

    /// <summary>Tracks whose note numbers are trigger pitches (drum one-shots, noise sweeps) — never
    /// transposed, or the kick would literally detune.</summary>
    public static readonly IReadOnlyList<string> UnpitchedTracks = new[]
        { "Kick", "Clap", "Snare", "Hat Closed", "Hat Open", "Crash", "Riser", "Sweeps" };
    // Chord roots per bar: F G A G(Em/G bass) F G E E.
    private static readonly int[] BassRoots = { 29, 31, 33, 31, 29, 31, 28, 28 };   // F1 G1 A1 G1 | F1 G1 E1 E1
    private static readonly int[] ArpRoots = { 53, 55, 57, 55, 53, 55, 52, 52 };    // F3 G3 A3 G3 | F3 G3 E3 E3
    private static readonly int[] PluckRoots = { 65, 67, 69, 67, 65, 67, 64, 64 };  // one octave up
    private const int PedalNote = 33; // A1 — intro pedal on the tonic

    private static readonly int[][] PadChords =
    {
        new[] { 53, 57, 60, 64 },     // Fmaj7  (F3 A3 C4 E4)
        new[] { 55, 59, 62, 64 },     // G6     (G3 B3 D4 E4)
        new[] { 57, 60, 64, 67 },     // Am7    (A3 C4 E4 G4)
        new[] { 55, 59, 62, 64 },     // Em/G   (G3 B3 D4 E4)
        new[] { 53, 57, 60, 64 },     // Fmaj7
        new[] { 55, 59, 62, 67 },     // G      (G3 B3 D4 G4)
        new[] { 52, 57, 59, 64 },     // Esus4  (E3 A3 B3 E4)
        new[] { 52, 56, 59, 64 }      // E      (E3 G#3 B3 E4) — dominant turnaround
    };

    // Root + fifth dyads an octave below the pad voicings.
    private static readonly int[][] PadLowDyads =
    {
        new[] { 41, 48 }, // F2 C3
        new[] { 43, 50 }, // G2 D3
        new[] { 45, 52 }, // A2 E3
        new[] { 43, 50 }, // G2 D3 (Em/G bass)
        new[] { 41, 48 }, // F2 C3
        new[] { 43, 50 }, // G2 D3
        new[] { 40, 47 }, // E2 B2
        new[] { 40, 47 }  // E2 B2
    };

    private const int CycleBars = 8;

    /// <summary>Builds the complete song. Requires the Field instrument to be registered
    /// (see <c>FieldBootstrap.Initialize</c>) — the plucks, arps, theme and anthem are Field patches.</summary>
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
        // The mastering chain, in the canonical trance order (mixdown → corrective EQ → mid/side →
        // glue comp → soft clip → brickwall limiter → meter):
        //  1. A corrective EQ: a steep 26 Hz high-pass strips inaudible sub-rumble that would trick
        //     the limiter into clamping early, and a 19.5 kHz low-pass shaves digital hiss.
        //  2. Mid/side EQ: mono the sub-bass below 120 Hz into the dead centre for club punch, and a
        //     gentle +1.2 dB air shelf on the sides pushes the supersaw sheen outward.
        //  3. VCA-style glue compression (2:1, slow 30 ms attack so kick/pluck transients pass, fast
        //     release so it pumps in sync with the 138 BPM) — just ~1–2 dB of gain reduction.
        //  4. A soft clipper shaves the microscopic transient peaks so the limiter works less.
        //  5. A -1.0 dBFS brickwall limiter leaves inter-sample-peak headroom for lossy encoding.
        var masterEq = new EqEffect();
        masterEq.Bands[0].Type = EqBandType.HighPass; masterEq.Bands[0].Frequency = 26; masterEq.Bands[0].Q = 0.7;
        masterEq.Bands[1].Type = EqBandType.LowPass; masterEq.Bands[1].Frequency = 19500; masterEq.Bands[1].Q = 0.7;
        masterEq.Bands[2].Type = EqBandType.HighShelf; masterEq.Bands[2].Frequency = 12000; masterEq.Bands[2].GainDb = 0.5;
        masterEq.CommitBands();
        master.Effects.Add(masterEq);
        master.Effects.Add(new MidSideEqEffect { SideLowCutHz = 120, SideAirHz = 9000, SideAirDb = 1.2 });
        master.Effects.Add(new CompressorEffect { ThresholdDb = -14, Ratio = 2.0, AttackMs = 30, ReleaseMs = 110, MakeupDb = 1.5 });
        master.Effects.Add(new StereoWidthEffect { Width = 1.1 });
        master.Effects.Add(new ClipperEffect { DriveDb = 1.5, CeilingDb = -0.5 });
        master.Effects.Add(new LimiterEffect { CeilingDb = -1.0, ReleaseMs = 110 });
        master.Effects.Add(new WaveformVisualizerEffect());
        project.Tracks.Add(master);

        // Group buses: effects and automation applied once per family instead of per track.
        var kick = BuildKick();

        // Drums: a mastering-style bus chain — a punch EQ (boom up, mud out, snap and air up), a
        // glue compressor whose 15 ms attack lets the kick transient through before the squeeze
        // grabs the body, and a bus limiter for density.
        var drums = new Track { Name = "Drums", Kind = TrackKind.Group, ColorKey = "CatppuccinRed", Volume = 1.0 };
        var drumEq = new EqEffect();
        drumEq.AddBand(new EqBand(EqBandType.LowShelf, 90, 2.5, 0.7));   // boom
        drumEq.AddBand(new EqBand(EqBandType.Bell, 300, -2.5, 1.0));     // mud cut
        drumEq.AddBand(new EqBand(EqBandType.Bell, 3500, 2.0, 0.9));     // snap
        drumEq.AddBand(new EqBand(EqBandType.HighShelf, 9000, 1.5, 0.7)); // air
        drums.Effects.Add(drumEq);
        // Glue bus compression: slow attack lets the kick/clap transient click through, fast release
        // pumps in time, ~2-3 dB GR binds the kit into one cohesive unit.
        drums.Effects.Add(new CompressorEffect { ThresholdDb = -14, Ratio = 4.0, AttackMs = 30, ReleaseMs = 100, MakeupDb = 3 });
        // Mono the low end (kick dead-centre for club punch) while spreading the hat/clap "air" wide —
        // the modern trance drum image: driving centre, exploding sides.
        drums.Effects.Add(new MidSideEqEffect { SideLowCutHz = 250, SideAirHz = 9000, SideAirDb = 2.5 });
        drums.Effects.Add(new LimiterEffect { CeilingDb = -1.0, ReleaseMs = 80 });
        AddGroup(project, drums, kick, BuildClap(), BuildSnare(), BuildClosedHat(), BuildOpenHat(), BuildCrash());

        var bassGroup = new Track { Name = "Bass Bus", Kind = TrackKind.Group, ColorKey = "CatppuccinMauve", Volume = 1.0 };
        AddGroup(project, bassGroup, BuildBass(project, kick.Id), BuildSub(kick.Id), BuildAcid(project, instruments, kick.Id));

        // Leads: width polish, mud-cut EQ, and a group-level swell into each climax.
        // Leads: width polish, mud-cut EQ, an OTT-style multiband compressor to "inflate" the wall
        // into that modern, in-your-face trance sheen, and a group-level swell into each climax.
        var leads = new Track { Name = "Leads", Kind = TrackKind.Group, ColorKey = "CatppuccinBlue", Volume = 1.0 };
        var leadEq = new EqEffect();
        leadEq.AddBand(new EqBand(EqBandType.HighPass, 180, 0, 0.7));
        leadEq.AddBand(new EqBand(EqBandType.Bell, 380, -2.0, 1.2)); // keep the wall out of the bass mud
        leadEq.AddBand(new EqBand(EqBandType.HighShelf, 8000, 1.5, 0.7)); // modern air
        leads.Effects.Add(leadEq);
        leads.Effects.Add(new StereoWidthEffect { Width = 1.12 });
        leads.Effects.Add(new MultibandCompressorEffect { Depth = 0.45, HighBoostDb = 3 });
        leads.Effects.Add(new UtilityEffect { GainDb = 1.5 }); // bring the lead group up front, level with the drums
        AddGroup(project, leads,
            BuildPlucks(project, instruments), BuildArp(project, instruments), BuildSparkle(instruments),
            BuildTheme(project, instruments), BuildAnthem(project, instruments, kick.Id),
            BuildLeadCore(instruments, kick.Id), BuildLeadClick(instruments, kick.Id),
            BuildSawLayer(instruments, kick.Id), BuildCounter(instruments), BuildBells(kick.Id));
        Automate(leads, null, AutomationTargetKind.TrackVolume, -1, -1,
            (0, 0.9), (Beat(Climax1), 1.0), (Beat(Mid), 0.9), (Beat(Climax2), 1.0), (Beat(Outro), 0.9));
        leads.CommitAutoLanes();

        var atmosphere = new Track { Name = "Atmosphere", Kind = TrackKind.Group, ColorKey = "CatppuccinLavender", Volume = 1.0 };
        AddGroup(project, atmosphere, BuildPads(project), BuildPadsLow(project), BuildAtmos(project, kick.Id),
            BuildRiser(project), BuildTonalRiser(project, instruments), BuildSweeps());

        // Lift the final climax a whole step (B minor) before the outro resolves home to A minor.
        foreach (var track in project.Tracks.Where(t =>
                     t.Kind == TrackKind.Instrument && !UnpitchedTracks.Contains(t.Name)))
        {
            foreach (var clip in track.Clips)
            {
                foreach (var note in clip.Notes)
                {
                    var absoluteBeat = clip.StartBeat + note.StartBeat;
                    var lifted = absoluteBeat >= Beat(KeyChangeBar) && absoluteBeat < Beat(Outro);
                    note.Note += Transpose + (lifted ? KeyChangeLift : 0);
                }
            }
        }

        CommitAll(project);
        return project;
    }

    private static void AddGroup(Project project, Track group, params Track[] children)
    {
        project.Tracks.Add(group);
        foreach (var child in children)
        {
            child.ParentId = group.Id;
            project.Tracks.Add(child);
        }
    }

    // ---- Drums ----

    private static Track BuildKick()
    {
        // A layered kick: the round Trance Kick body with the clickier EDM Kick stacked behind it
        // (trimmed -8 dB in its slot) supplying the transient top the trance kick lacks alone.
        var track = NewInstrumentTrack("Kick", "CatppuccinRed", 1.0, PresetInstrument(new KickaInstrument(), "Trance Kick"));
        var clickSlot = new InstrumentSlot(PresetInstrument(new KickaInstrument(), "EDM Kick")) { Enabled = true };
        clickSlot.Effects.Add(new UtilityEffect { GainDb = -8 });
        clickSlot.CommitEffects();
        track.Instruments.Add(clickSlot);

        var floor = new[] { (0.0, 1.0f), (1.0, 1.0f), (2.0, 1.0f), (3.0, 1.0f) };
        track.Clips.Add(DrumClip("Kick", Intro, Break1 - Intro, floor));
        track.Clips.Add(DrumClip("Kick", Climax1, Break2 - Climax1, floor));
        track.Clips.Add(DrumClip("Kick", Climax2, Outro + 16 - Climax2, floor));
        track.Clips.Add(DrumClip("Kick Out", Outro + 16, 8, floor));
        return track;
    }

    private static Track BuildClap()
    {
        var track = NewInstrumentTrack("Clap", "CatppuccinPeach", 0.75, PresetInstrument(new PercaInstrument(), "House Clap"));
        track.Effects.Add(new ReverbEffect { Mix = 0.22, RoomSize = 0.6, Damping = 0.4, Quality = 1 });
        track.Effects.Add(new StereoWidthEffect { Width = 1.3 }); // stadium spread

        var backbeat = new[] { (1.0, 0.9f), (3.0, 0.9f) };
        track.Clips.Add(DrumClip("Clap", Groove, Break1 - Groove, backbeat));
        track.Clips.Add(DrumClip("Clap", Climax1, Break2 - Climax1, backbeat));
        track.Clips.Add(DrumClip("Clap", Climax2, Outro - Climax2, backbeat));
        return track;
    }

    private static Track BuildSnare()
    {
        var track = NewInstrumentTrack("Snare", "CatppuccinMaroon", 0.8, PresetInstrument(new PercaInstrument(), "Dark Snare"));
        track.Effects.Add(new ReverbEffect { Mix = 0.3, RoomSize = 0.7, Damping = 0.35, Quality = 1 });

        // Four-bar crescendo rolls into each climax (the trance build's engine room), scaled back a
        // touch so the roll drives the build without dominating the mix.
        track.Clips.Add(SnareRoll(Climax1 - 4, 4, peak: 0.7f));
        track.Clips.Add(SnareRoll(Climax2 - 4, 4, peak: 0.7f));

        // ...plus a snare layered under the clap on the climax backbeats — the two together make
        // one fat hit where either alone sounds thin.
        var backbeat = new[] { (1.0, 0.55f), (3.0, 0.55f) };
        track.Clips.Add(DrumClip("Snare Layer", Climax1, Break2 - Climax1, backbeat));
        track.Clips.Add(DrumClip("Snare Layer", Climax2, Outro - Climax2, backbeat));
        return track;
    }

    private static Track BuildClosedHat()
    {
        var track = NewInstrumentTrack("Hat Closed", "CatppuccinYellow", 0.55, PresetInstrument(new PercaInstrument(), "Closed Hat"));

        // The trance offbeat: strong "and" hits with light 16th ghosts either side in full sections.
        var offbeats = new[] { (0.5, 0.7f), (1.5, 0.7f), (2.5, 0.7f), (3.5, 0.7f) };
        var driving = new[]
        {
            (0.25, 0.25f), (0.5, 0.75f), (0.75, 0.3f), (1.25, 0.25f), (1.5, 0.75f), (1.75, 0.3f),
            (2.25, 0.25f), (2.5, 0.75f), (2.75, 0.3f), (3.25, 0.25f), (3.5, 0.75f), (3.75, 0.3f)
        };

        track.Clips.Add(DrumClip("Hats", Intro + 8, Groove - Intro - 8, offbeats));
        track.Clips.Add(DrumClip("Hats", Groove, Break1 - Groove, driving));
        track.Clips.Add(DrumClip("Hats", Climax1, Break2 - Climax1, driving));
        track.Clips.Add(DrumClip("Hats", Climax2, Outro - Climax2, driving));
        track.Clips.Add(DrumClip("Hats", Outro, 16, offbeats));

        Automate(track, null, AutomationTargetKind.TrackVolume, -1, -1,
            (Beat(Intro), 0.45), (Beat(Groove), 0.55), (Beat(Climax1), 0.65),
            (Beat(Break2), 0.5), (Beat(Climax2), 0.7));
        return track;
    }

    private static Track BuildOpenHat()
    {
        var track = NewInstrumentTrack("Hat Open", "CatppuccinGreen", 0.5, PresetInstrument(new PercaInstrument(), "Open Hat"));

        var offbeats = new[] { (0.5, 0.75f), (1.5, 0.75f), (2.5, 0.75f), (3.5, 0.75f) };
        track.Clips.Add(DrumClip("Open Hat", Climax1, Break2 - Climax1, offbeats));
        track.Clips.Add(DrumClip("Open Hat", Climax2, Outro - Climax2, offbeats));
        return track;
    }

    // ---- Bass ----

    private static Track BuildBass(Project project, Guid kickTrackId)
    {
        var track = NewInstrumentTrack("Bass", "CatppuccinMauve", 0.8, FactoryPresets.TranceBass());
        track.Effects.Add(new FilterEffect { Mode = FilterMode.LowPass, Frequency = 500, Resonance = 0.9 });
        track.Effects.Add(new SidechainEffect
        {
            SourceTrackId = kickTrackId,
            Amount = 0.75,
            AttackMs = 3,
            ReleaseMs = 150
        });

        // The intro rides a single repeating root-note pedal; the full rolling line only arrives
        // with the groove — the classic reveal.
        track.Clips.Add(RollingBassClip("Bass Pedal", Intro + 4, Groove - Intro - 4, pedal: true));
        track.Clips.Add(RollingBassClip("Bass", Groove, Break1 - Groove, pedal: false));
        track.Clips.Add(RollingBassClip("Bass", Climax1, Break2 - Climax1, pedal: false));
        track.Clips.Add(RollingBassClip("Bass", Climax2, Outro + 16 - Climax2, pedal: false));

        // The bass darkens/brightens with the arrangement: muffled pedal, opening into each climax.
        Automate(track, project, AutomationTargetKind.EffectParam, 0, 1,
            (Beat(Intro), 400, 0), (Beat(Groove), 800, 0), (Beat(Climax1), 2200, 0),
            (Beat(Mid), 1000, 0), (Beat(Climax2), 2400, 0), (Beat(Outro), 900, 0));
        return track;
    }

    /// <summary>The rolling trance bass: driving eighths, offbeats accented, a 16th pickup into each
    /// bar. In pedal mode every note is the tonic root (the intro's held breath); otherwise the line
    /// follows the chord roots through the 8-bar cycle.</summary>
    private static Clip RollingBassClip(string name, int startBar, int bars, bool pedal)
    {
        var clip = new Clip { Name = name, StartBeat = Beat(startBar), LengthBeats = bars * BeatsPerBar, IsAudio = false };
        var pattern = new[]
        {
            (0.5, 0.9f), (1.0, 0.6f), (1.5, 0.9f), (2.0, 0.6f),
            (2.5, 0.9f), (3.0, 0.6f), (3.5, 0.9f), (3.75, 0.5f)
        };
        for (var bar = 0; bar < bars; bar++)
        {
            var root = pedal ? PedalNote : BassRoots[(startBar + bar) % CycleBars];
            foreach (var (offset, velocity) in pattern)
            {
                clip.Notes.Add(new MidiNote
                {
                    Note = root,
                    StartBeat = bar * BeatsPerBar + offset,
                    LengthBeats = 0.2,
                    Velocity = velocity
                });
            }
        }

        return clip;
    }

    /// <summary>The Oda-style acid line: a squelching 303 thumping the tonic through the mid-section
    /// between the choruses and again through the second build, its filter insert sweeping open all
    /// the way into the final climax so the transition arrives already boiling.</summary>
    private static readonly (double Beat, int Note, double Length)[] AcidPhrase =
    {
        // Two bars of 16ths on A1 with A-minor-pentatonic flicks (A C D E G).
        (0.0, 33, 0.2), (0.25, 33, 0.2), (0.5, 45, 0.2), (0.75, 33, 0.2),
        (1.0, 33, 0.2), (1.25, 40, 0.2), (1.5, 33, 0.2), (1.75, 45, 0.2),
        (2.0, 33, 0.2), (2.25, 33, 0.2), (2.5, 43, 0.2), (2.75, 33, 0.2),
        (3.0, 45, 0.2), (3.25, 33, 0.2), (3.5, 40, 0.2), (3.75, 33, 0.2),
        (4.0, 33, 0.2), (4.25, 33, 0.2), (4.5, 45, 0.2), (4.75, 33, 0.2),
        (5.0, 33, 0.2), (5.25, 40, 0.2), (5.5, 33, 0.2), (5.75, 43, 0.2),
        (6.0, 33, 0.2), (6.25, 45, 0.2), (6.5, 33, 0.2), (6.75, 40, 0.2),
        (7.0, 33, 0.2), (7.25, 43, 0.2), (7.5, 47, 0.2), (7.75, 45, 0.2)
    };

    private static Track BuildAcid(Project project, IInstrumentRegistry instruments, Guid kickTrackId)
    {
        var acid = PresetInstrument(instruments.Create(FieldInstrument.Id), "Acid Bass");
        var track = NewInstrumentTrack("Acid", "CatppuccinYellow", 0.6, acid);
        track.Effects.Add(new FilterEffect { Mode = FilterMode.LowPass, Frequency = 500, Resonance = 1.5 });
        track.Effects.Add(new DelayEffect { TimeMs = DottedEighthMs(), Feedback = 0.25, Mix = 0.15 });
        track.Effects.Add(new SidechainEffect { SourceTrackId = kickTrackId, Amount = 0.6, AttackMs = 3, ReleaseMs = 130 });

        track.Clips.Add(PhraseClip("Acid", Mid, Break2 - Mid, AcidPhrase, 0.8f));
        track.Clips.Add(PhraseClip("Acid Build", Break2 + 24, Climax2 - Break2 - 24, AcidPhrase, 0.85f));

        // The classic acid arc: the insert filter crawls open across the mid-section, drops back,
        // then screams open through the final build.
        Automate(track, project, AutomationTargetKind.EffectParam, 0, 1,
            (Beat(Mid), 500, 0), (Beat(Break2), 2600, 0), (Beat(Break2) + 0.5, 500, 0),
            (Beat(Break2 + 24), 600, -0.3), (Beat(Climax2), 3200, 0), (Beat(Climax2) + 0.5, 500, 0));
        return track;
    }

    /// <summary>Sustained sub roots under the climaxes — the weight layer beneath the rolling bass.</summary>
    private static Track BuildSub(Guid kickTrackId)
    {
        var track = NewInstrumentTrack("Sub", "CatppuccinMaroon", 0.6, FactoryPresets.DeepSubBass());
        track.Effects.Add(new SidechainEffect
        {
            SourceTrackId = kickTrackId,
            Amount = 0.7,
            AttackMs = 3,
            ReleaseMs = 140
        });

        foreach (var (start, bars) in new[] { (Climax1, Break2 - Climax1), (Climax2, Outro - Climax2) })
        {
            var clip = new Clip { Name = "Sub", StartBeat = Beat(start), LengthBeats = bars * BeatsPerBar, IsAudio = false };
            for (var bar = 0; bar < bars; bar++)
            {
                clip.Notes.Add(new MidiNote
                {
                    Note = BassRoots[(start + bar) % CycleBars],
                    StartBeat = bar * BeatsPerBar,
                    LengthBeats = BeatsPerBar - 0.1,
                    Velocity = 0.8f
                });
            }

            track.Clips.Add(clip);
        }

        return track;
    }

    // ---- Plucks and arps ----

    private static Track BuildPlucks(Project project, IInstrumentRegistry instruments)
    {
        var pluck = PresetInstrument(instruments.Create(FieldInstrument.Id), "Crystal Pluck");
        var track = NewInstrumentTrack("Plucks", "CatppuccinTeal", 0.65, pluck);
        track.Pan = -0.3; // plucks left, arp right — the groove answers itself across the field
        // A high-pass that starts thin and telephone-like, opening to full body as sections build.
        track.Effects.Add(new FilterEffect { Mode = FilterMode.HighPass, Frequency = 700, Resonance = 0.8 });
        track.Effects.Add(new DelayEffect { TimeMs = DottedEighthMs(), Feedback = 0.3, Mix = 0.18 });
        track.Effects.Add(new ChorusEffect { RateHz = 0.35, Depth = 0.35, Mix = 0.15, Spread = 0.7 });

        track.Clips.Add(PluckClip("Plucks", Groove, Break1 - Groove));
        track.Clips.Add(PluckClip("Plucks", Mid, Break2 - Mid));
        track.Clips.Add(PluckClip("Plucks", Outro, 24));

        Automate(track, project, AutomationTargetKind.EffectParam, 0, 1,
            (Beat(Groove), 700, 0), (Beat(Break1), 60, 0), (Beat(Mid), 500, 0),
            (Beat(Break2), 60, 0), (Beat(Outro), 300, 0), (Beat(Bars), 900, 0));
        return track;
    }

    /// <summary>Offbeat plucks alternating root and fifth an octave up — the "question" the delay
    /// answers, with more motion than a plain root pattern.</summary>
    private static Clip PluckClip(string name, int startBar, int bars)
    {
        var clip = new Clip { Name = name, StartBeat = Beat(startBar), LengthBeats = bars * BeatsPerBar, IsAudio = false };
        for (var bar = 0; bar < bars; bar++)
        {
            var root = PluckRoots[(startBar + bar) % CycleBars];
            for (var b = 0; b < 4; b++)
            {
                clip.Notes.Add(new MidiNote
                {
                    Note = b % 2 == 0 ? root : root + 7,
                    StartBeat = bar * BeatsPerBar + b + 0.5,
                    LengthBeats = 0.25,
                    Velocity = 0.7f
                });
            }
        }

        return clip;
    }

    private static Track BuildArp(Project project, IInstrumentRegistry instruments)
    {
        var arp = PresetInstrument(instruments.Create(FieldInstrument.Id), "Crystal Pluck");
        var track = NewInstrumentTrack("Arp", "CatppuccinSky", 0.55, arp);
        track.Pan = 0.3;
        // The arp emerges from behind a closed low-pass, sweeping open through every build.
        track.Effects.Add(new FilterEffect { Mode = FilterMode.LowPass, Frequency = 900, Resonance = 1.2 });
        track.Effects.Add(new DelayEffect { TimeMs = DottedEighthMs(), Feedback = 0.3, Mix = 0.18 });
        track.Effects.Add(new ReverbEffect { Mix = 0.12, RoomSize = 0.65, Damping = 0.45, Quality = 1 });

        track.Clips.Add(ArpClip("Arp", Break1 + 16, Climax1 - Break1 - 16));
        track.Clips.Add(ArpClip("Arp", Climax1, Break2 - Climax1));
        track.Clips.Add(ArpClip("Arp", Break2 + 24, Climax2 - Break2 - 24));
        track.Clips.Add(ArpClip("Arp", Climax2, Outro - Climax2));
        track.Clips.Add(ArpClip("Arp Out", Outro, 24));

        // The signature arp journey: born dull behind a nearly-closed filter, brightening through
        // the build and CONTINUING to open across the climax itself, so the top never stops rising.
        Automate(track, project, AutomationTargetKind.EffectParam, 0, 1,
            (Beat(Break1 + 16), 350, 0), (Beat(Climax1), 4500, 0), (Beat(Climax1 + 24), 7500, 0),
            (Beat(Mid), 2500, 0), (Beat(Break2 + 24), 500, -0.2), (Beat(Climax2), 5000, 0),
            (Beat(Climax2 + 24), 9000, 0), (Beat(Outro), 3000, 0), (Beat(Bars), 400, 0));

        // Resonance leans in with the brightness for that singing filter edge...
        Automate(track, project, AutomationTargetKind.EffectParam, 0, 2,
            (Beat(Break1 + 16), 1.0, 0), (Beat(Climax1), 2.2, 0), (Beat(Mid), 1.2, 0),
            (Beat(Break2 + 24), 1.0, 0), (Beat(Climax2), 2.5, 0), (Beat(Bars), 1.0, 0));

        // ...and the delay wash deepens through the breakdowns, drying up for the tight climaxes.
        Automate(track, project, AutomationTargetKind.EffectParam, 1, 2,
            (Beat(Break1 + 16), 0.35, 0), (Beat(Climax1), 0.2, 0), (Beat(Break2 + 24), 0.35, 0),
            (Beat(Climax2), 0.2, 0), (Beat(Outro), 0.3, 0));

        // The arp swells up through each breakdown so the builds feel alive.
        Automate(track, project, AutomationTargetKind.TrackVolume, -1, -1,
            (Beat(Break1 + 16), 0.35), (Beat(Climax1), 0.55), (Beat(Break2 + 24), 0.4),
            (Beat(Climax2), 0.6), (Beat(Outro), 0.5), (Beat(Bars) - 4, 0.05));
        return track;
    }

    /// <summary>16th-note arpeggios accented on the beat: the cycle's first half climbs
    /// root–fifth–octave, the second half falls octave–fifth–root — chord-agnostic intervals that
    /// stay diatonic across the whole 8-bar progression while giving the line a rise-and-fall shape.</summary>
    private static Clip ArpClip(string name, int startBar, int bars)
    {
        var clip = new Clip { Name = name, StartBeat = Beat(startBar), LengthBeats = bars * BeatsPerBar, IsAudio = false };
        int[] climb = { 0, 7, 12, 7 };
        int[] fall = { 12, 7, 0, 7 };
        for (var bar = 0; bar < bars; bar++)
        {
            var cycleBar = (startBar + bar) % CycleBars;
            var root = ArpRoots[cycleBar];
            var steps = cycleBar < 4 ? climb : fall;
            for (var i = 0; i < 16; i++)
            {
                clip.Notes.Add(new MidiNote
                {
                    Note = root + steps[i % 4],
                    StartBeat = bar * BeatsPerBar + i * 0.25,
                    LengthBeats = 0.2,
                    Velocity = i % 4 == 0 ? 0.75f : 0.5f
                });
            }
        }

        return clip;
    }

    /// <summary>A second Crystal Pluck an octave up, panned hard opposite the arp and hitting the
    /// off-16ths — the ping-pong shimmer over the second breakdown and final climax.</summary>
    private static Track BuildSparkle(IInstrumentRegistry instruments)
    {
        var sparkle = PresetInstrument(instruments.Create(FieldInstrument.Id), "Crystal Pluck");
        var track = NewInstrumentTrack("Sparkle", "CatppuccinFlamingo", 0.4, sparkle);
        track.Pan = -0.45;
        track.Effects.Add(new DelayEffect { TimeMs = DottedEighthMs(), Feedback = 0.5, Mix = 0.35 });
        track.Effects.Add(new ReverbEffect { Mix = 0.3, RoomSize = 0.75, Damping = 0.35, Quality = 1 });
        // A light crush gives the shimmer a glassy lo-fi edge that separates it from the arp.
        track.Effects.Add(new BitcrusherEffect { Bits = 10, Downsample = 2, Mix = 0.35 });

        track.Clips.Add(SparkleClip("Sparkle", Break2 + 24, Climax2 - Break2 - 24));
        track.Clips.Add(SparkleClip("Sparkle", Climax2, Outro - Climax2));
        return track;
    }

    private static Clip SparkleClip(string name, int startBar, int bars)
    {
        var clip = new Clip { Name = name, StartBeat = Beat(startBar), LengthBeats = bars * BeatsPerBar, IsAudio = false };
        for (var bar = 0; bar < bars; bar++)
        {
            var root = PluckRoots[(startBar + bar) % CycleBars];
            for (var i = 0; i < 8; i++)
            {
                // Off-16ths ("e" and "a"), alternating the octave and the twelfth above the pluck root.
                clip.Notes.Add(new MidiNote
                {
                    Note = root + (i % 2 == 0 ? 12 : 19),
                    StartBeat = bar * BeatsPerBar + i * 0.5 + 0.25,
                    LengthBeats = 0.2,
                    Velocity = 0.45f
                });
            }
        }

        return clip;
    }

    // ---- Theme, anthem and counter-melody ----

    /// <summary>
    /// The lead — a simple, memorable call-and-response hook in the Oda blueprint (138 BPM, A minor).
    /// An 8-bar motif: Call (E5 rise, hold the tense B5) → Response (fast C6–G5 cascade resolving to
    /// A5) → Variation call leaping an octave to E6 → a 1/16 turnaround run → a low-to-high E5→E6
    /// exclamation. Bars 9–14 restate the motif so the shifting chords underneath recolour it, and
    /// bars 15–16 break the pattern with a rising run to E6 and a silence "vacuum" before the reset.
    /// Deliberately sparse: a few long tones per bar, off-beat entries, wide interval leaps.
    /// </summary>
    private static readonly (double Beat, int Note, double Length)[] Theme =
    {
        // --- 8-bar motif ---
        // Bar1 CALL — E5 rises to G5, A5 (incomplete, off-beat)
        (0.0, 76, 1.5), (2.0, 79, 1.0), (3.0, 81, 1.0),
        // Bar2 — hold on the tense B5
        (4.0, 83, 3.0),
        // Bar3 RESPONSE — fast downward cascade C6 B5 A5 G5
        (8.0, 84, 0.75), (9.0, 83, 0.75), (10.0, 81, 0.75), (11.0, 79, 0.75),
        // Bar4 — clean resolution on A5
        (12.0, 81, 3.5),
        // Bar5 VARIATION CALL — as bar1
        (16.0, 76, 1.5), (18.0, 79, 1.0), (19.0, 81, 1.0),
        // Bar6 — octave leap to the E6 peak
        (20.0, 88, 3.0),
        // Bar7 TURNAROUND — 1/16 rolling run down
        (24.0, 86, 0.25), (24.25, 84, 0.25), (24.5, 83, 0.25), (24.75, 81, 0.25),
        (25.0, 79, 0.25), (25.25, 77, 0.25), (25.5, 76, 0.5), (26.0, 79, 2.0),
        // Bar8 EXCLAMATION — low-to-high octave jump E5 → E6
        (28.0, 76, 1.5), (30.0, 88, 2.0),
        // --- Restatement (bars 9–14 = motif; chords recolour it) ---
        (32.0, 76, 1.5), (34.0, 79, 1.0), (35.0, 81, 1.0),
        (36.0, 83, 3.0),
        (40.0, 84, 0.75), (41.0, 83, 0.75), (42.0, 81, 0.75), (43.0, 79, 0.75),
        (44.0, 81, 3.5),
        (48.0, 76, 1.5), (50.0, 79, 1.0), (51.0, 81, 1.0),
        (52.0, 88, 3.0),
        // Bar15 — the micro-hook: a rising run up to the E6 peak
        (56.0, 81, 0.5), (56.5, 83, 0.5), (57.0, 84, 0.5), (57.5, 86, 0.5), (58.0, 88, 1.5),
        // Bar16 — one accent then a silence vacuum into the drop
        (62.0, 84, 1.0)
    };

    /// <summary>Counter: a voice-led sustained line — E is common to Fmaj7 / G6 / Am / Em, so it
    /// simply holds and recolours as the harmony shifts, dipping to D only over the plain G.</summary>
    private static readonly (double Beat, int Note, double Length)[] Counter =
    {
        (0.0, 64, 3.9), (4.0, 64, 3.9), (8.0, 64, 3.9), (12.0, 64, 3.9),
        (16.0, 64, 3.9), (20.0, 62, 3.9), (24.0, 64, 3.9), (28.0, 64, 3.9)
    };

    private static Track BuildTheme(Project project, IInstrumentRegistry instruments)
    {
        // Breakdown lead: one voice only — Solace Lead, dry-ish, with space. The supersaw wall
        // arrives separately on the Anthem track in the climaxes (no double-stacking here).
        var track = NewInstrumentTrack("Theme", "CatppuccinLavender", 0.82,
            PresetInstrument(instruments.Create(FieldInstrument.Id), "Solace Lead"));

        track.Effects.Add(new FilterEffect { Mode = FilterMode.HighPass, Frequency = 250, Resonance = 0.7 });
        track.Effects.Add(new DelayEffect { TimeMs = DottedEighthMs(), Feedback = 0.3, Mix = 0.18, PingPong = true });
        // A big hall for scale, then a high-pass on the wet tail so the wash never muddies the mix —
        // massive but clear as day.
        track.Effects.Add(new ReverbEffect { Mix = 0.34, RoomSize = 0.95, Damping = 0.35, Width = 1.0 });
        track.Effects.Add(new FilterEffect { Mode = FilterMode.HighPass, Frequency = 320, Resonance = 0.6 });
        track.Effects.Add(new StereoWidthEffect { Width = 1.25 });

        track.Clips.Add(PhraseClip("Theme", Break1, Climax1 - Break1, Theme, 0.7f));
        track.Clips.Add(PhraseClip("Theme", Climax1, Break2 - Climax1, Theme, 0.55f)); // duck under anthem
        track.Clips.Add(PhraseClip("Theme Soft", Break2 + 8, 16, Theme, 0.55f));
        track.Clips.Add(PhraseClip("Theme", Break2 + 24, Climax2 - Break2 - 24, Theme, 0.6f));
        track.Clips.Add(PhraseClip("Theme", Climax2, Outro - Climax2, Theme, 0.5f));

        // Strong high-pass filter throw on the last two bars of every 16-bar phrase: the lead's
        // low/mid body is ripped away into a thin, resonant whistle as the melody reaches its
        // turnaround, then snaps wide open on the downbeat of the next phrase. Aggressive by design.
        var themePhraseEnds = PhraseEnds(Break1 + 16, Outro);
        Automate(track, project, AutomationTargetKind.EffectParam, 0, 1,
            PhraseEndSweep(themePhraseEnds, 250, 4200));
        Automate(track, project, AutomationTargetKind.EffectParam, 0, 2,
            PhraseEndSweep(themePhraseEnds, 0.7, 4.0));
        return track;
    }

    /// <summary>The 16-bar phrase boundaries in [<paramref name="fromBar"/>, <paramref name="toBar"/>].</summary>
    private static int[] PhraseEnds(int fromBar, int toBar)
    {
        var bars = new List<int>();
        for (var b = fromBar; b <= toBar; b += 16) bars.Add(b);
        return bars.ToArray();
    }

    /// <summary>Builds an aggressive "throw" automation for a filter param: it holds at
    /// <paramref name="baseVal"/>, ramps hard up to <paramref name="peakVal"/> across the final two
    /// bars before each phrase boundary, then snaps back on the downbeat.</summary>
    private static (double Beat, double Value, double Curve)[] PhraseEndSweep(
        IEnumerable<int> boundaries, double baseVal, double peakVal)
    {
        var points = new List<(double, double, double)> { (0, baseVal, 0) };
        foreach (var b in boundaries)
        {
            points.Add((Beat(b - 2), baseVal, 0.6));   // hold low, then accelerate up
            points.Add((Beat(b) - 0.25, peakVal, 0));  // slam to the harsh peak at the phrase end
            points.Add((Beat(b), baseVal, 0));         // snap wide open on the next downbeat
        }

        return points.ToArray();
    }

    /// <summary>Bells doubling the lead's octave-up response peaks (the E6 answers) and the final
    /// A resolution — a subtle crystalline sparkle, not a second melody.</summary>
    private static readonly (double Beat, int Note, double Length)[] BellAnchors =
    {
        (9.0, 88, 1.0), (11.0, 88, 1.0), (25.0, 88, 1.0), (41.0, 88, 1.0), (61.0, 81, 2.0)
    };

    private static Track BuildBells(Guid kickTrackId)
    {
        var track = NewInstrumentTrack("Bells", "CatppuccinRosewater", 0.32, FactoryPresets.GlassBells());
        track.Pan = 0.35;
        track.Effects.Add(new DelayEffect { TimeMs = DottedEighthMs(), Feedback = 0.35, Mix = 0.18 });
        track.Effects.Add(new ReverbEffect { Mix = 0.25, RoomSize = 0.75, Damping = 0.4, Quality = 1 });
        track.Effects.Add(new StereoWidthEffect { Width = 1.3 });
        // Light kick duck: the long bell tails bow out of the kick's way, keeping the top end clear.
        track.Effects.Add(new SidechainEffect { SourceTrackId = kickTrackId, Amount = 0.3, AttackMs = 3, ReleaseMs = 120 });

        track.Clips.Add(PhraseClip("Bells", Break1 + 8, Climax1 - 8 - Break1 - 8, BellAnchors, 0.5f));
        track.Clips.Add(PhraseClip("Bells", Break2 + 8, Climax2 - 8 - Break2 - 8, BellAnchors, 0.5f));
        track.Clips.Add(PhraseClip("Bells", Climax2, Outro - Climax2, BellAnchors, 0.55f));
        return track;
    }

    // ---- Big chord-lead harmonisation ----

    private static readonly int[] AMinorScalePcs = { 9, 11, 0, 2, 4, 5, 7 };

    /// <summary>The full A-natural-minor MIDI ladder, built once, for diatonic harmonisation.</summary>
    private static readonly int[] AMinorLadder = BuildAMinorLadder();

    private static int[] BuildAMinorLadder()
    {
        var list = new List<int>();
        for (var m = 21; m <= 108; m++)
            if (System.Array.IndexOf(AMinorScalePcs, m % 12) >= 0) list.Add(m);
        return list.ToArray();
    }

    /// <summary>A diatonic interval below <paramref name="note"/> within A natural minor
    /// (steps = 2 → a third below, 4 → a fifth below), falling back chromatically off-scale.</summary>
    private static int DiatonicBelow(int note, int steps)
    {
        var idx = System.Array.IndexOf(AMinorLadder, note);
        if (idx < 0) return note - (steps == 2 ? 3 : 7);
        return AMinorLadder[System.Math.Max(0, idx - steps)];
    }

    /// <summary>Harmonises a single-note line into stacked diatonic triads (melody on top plus a
    /// third and a fifth below) — the huge parallel-chord lead that makes a progressive-trance
    /// climax sound massive. Big by design; reserved for the full-energy anthem sections.</summary>
    private static (double Beat, int Note, double Length)[] HarmonizeTriads(
        (double Beat, int Note, double Length)[] line)
    {
        var chorded = new List<(double, int, double)>(line.Length * 3);
        foreach (var (beat, note, length) in line)
        {
            chorded.Add((beat, note, length));
            chorded.Add((beat, DiatonicBelow(note, 2), length));
            chorded.Add((beat, DiatonicBelow(note, 4), length));
        }

        return chorded.ToArray();
    }

    /// <summary>The theme harmonised into stacked triads for the anthem climax.</summary>
    private static readonly (double Beat, int Note, double Length)[] ThemeChords = HarmonizeTriads(Theme);

    private static Track BuildAnthem(Project project, IInstrumentRegistry instruments, Guid kickTrackId)
    {
        // The Aether Lead — the double-unison chorus wall — carries the theme through both climaxes,
        // harmonised into full parallel triads (melody + third + fifth below) so the drop lands huge.
        // A light kick sidechain keeps the wall pumping.
        var anthem = PresetInstrument(instruments.Create(FieldInstrument.Id), "Aether Lead");
        var track = NewInstrumentTrack("Anthem", "CatppuccinBlue", 0.95, anthem);
        // Ping-pong delay fills the gaps between phrases; a massive hall reverb makes the wall
        // enormous, then a high-pass on the wet keeps it clear; the whole thing pumps under the kick.
        track.Effects.Add(new DelayEffect { TimeMs = DottedEighthMs(), Feedback = 0.32, Mix = 0.2, PingPong = true });
        track.Effects.Add(new ReverbEffect { Mix = 0.3, RoomSize = 0.92, Damping = 0.3, Width = 1.0 });
        track.Effects.Add(new FilterEffect { Mode = FilterMode.HighPass, Frequency = 300, Resonance = 0.6 });
        track.Effects.Add(new SidechainEffect
        {
            SourceTrackId = kickTrackId,
            Amount = 0.4,
            AttackMs = 3,
            ReleaseMs = 140
        });

        track.Clips.Add(PhraseClip("Anthem", Climax1, Break2 - Climax1, ThemeChords, 0.8f));
        track.Clips.Add(PhraseClip("Anthem", Climax2, Outro - Climax2, ThemeChords, 0.85f));

        // Swells in over the first bars of each climax instead of slamming in at full width.
        Automate(track, project, AutomationTargetKind.TrackVolume, -1, -1,
            (Beat(Climax1), 0.7, -0.2), (Beat(Climax1 + 4), 1.0, 0), (Beat(Break2), 1.0, 0),
            (Beat(Climax2), 0.75, -0.2), (Beat(Climax2 + 4), 1.0, 0));

        // The signature filter throw, even harsher on the climax wall: rip the whole supersaw wall up
        // into a thin resonant scream over the last two bars of each 16-bar phrase, then drop it back
        // in full-bodied on the downbeat. This is the "don't be weak" phrase-end filtering.
        var anthemPhraseEnds = new[] { Climax1 + 16, Climax1 + 32, Break2, Climax2 + 16, Outro };
        Automate(track, project, AutomationTargetKind.EffectParam, 2, 1,
            PhraseEndSweep(anthemPhraseEnds, 300, 5200));
        Automate(track, project, AutomationTargetKind.EffectParam, 2, 2,
            PhraseEndSweep(anthemPhraseEnds, 0.6, 4.5));
        return track;
    }

    /// <summary>
    /// Layer 2 of the festival wall — the Mono Core (the punch). A near-mono supersaw folded to the
    /// centre with a low-mid "chest" boost, doubling the melody an octave down under the wide Aether
    /// wings. It provides the physical presence that hits on a club system so the wide layers never
    /// sound hollow. Dry and centred — no delay/reverb, so it stays tight and punchy.
    /// </summary>
    private static Track BuildLeadCore(IInstrumentRegistry instruments, Guid kickTrackId)
    {
        var core = PresetInstrument(instruments.Create(FieldInstrument.Id), "Nova Saw");
        var track = NewInstrumentTrack("Lead Core", "CatppuccinBlue", 0.52, core);
        track.Pan = 0.0;
        var eq = new EqEffect();
        eq.Bands[0].Type = EqBandType.Bell; eq.Bands[0].Frequency = 420; eq.Bands[0].GainDb = 3.0; eq.Bands[0].Q = 0.9;
        eq.CommitBands();
        track.Effects.Add(eq);
        track.Effects.Add(new StereoWidthEffect { Width = 0.0 }); // fold to dead-centre mono
        track.Effects.Add(new SidechainEffect { SourceTrackId = kickTrackId, Amount = 0.4, AttackMs = 3, ReleaseMs = 140 });

        var core1 = Theme.Select(n => (n.Beat, n.Note - 12, n.Length)).ToArray();
        track.Clips.Add(PhraseClip("Core", Climax1, Break2 - Climax1, core1, 0.55f));
        track.Clips.Add(PhraseClip("Core", Climax2, Outro - Climax2, core1, 0.6f));
        return track;
    }

    /// <summary>
    /// Layer 3 of the festival wall — the Transient Click (the definition). A sharp pluck high-passed
    /// to leave only the crisp top-end "snap" at the start of every note, so the melody cuts cleanly
    /// through the drums and rolling bass. Panned slightly opposite the counter for width.
    /// </summary>
    private static Track BuildLeadClick(IInstrumentRegistry instruments, Guid kickTrackId)
    {
        var click = PresetInstrument(instruments.Create(FieldInstrument.Id), "Crystal Pluck");
        var track = NewInstrumentTrack("Lead Click", "CatppuccinTeal", 0.4, click);
        track.Pan = 0.15;
        track.Effects.Add(new FilterEffect { Mode = FilterMode.HighPass, Frequency = 2200, Resonance = 0.7 });
        track.Effects.Add(new SidechainEffect { SourceTrackId = kickTrackId, Amount = 0.4, AttackMs = 3, ReleaseMs = 140 });

        track.Clips.Add(PhraseClip("Click", Climax1, Break2 - Climax1, Theme, 0.4f));
        track.Clips.Add(PhraseClip("Click", Climax2, Outro - Climax2, Theme, 0.45f));
        return track;
    }

    /// <summary>The Nova Saw stacked behind the Aether Lead: unison doubling in the first climax,
    /// a sub-octave doubling in the second — weight under the wall for the final lift.</summary>
    private static Track BuildSawLayer(IInstrumentRegistry instruments, Guid kickTrackId)
    {
        var saw = PresetInstrument(instruments.Create(FieldInstrument.Id), "Nova Saw");
        var track = NewInstrumentTrack("Saw Layer", "CatppuccinSapphire", 0.45, saw);
        track.Effects.Add(new ReverbEffect { Mix = 0.3, RoomSize = 0.8, Damping = 0.35, Quality = 1 });
        track.Effects.Add(new SidechainEffect
        {
            SourceTrackId = kickTrackId,
            Amount = 0.4,
            AttackMs = 3,
            ReleaseMs = 140
        });

        track.Clips.Add(PhraseClip("Saw Double", Climax1, Break2 - Climax1, Theme, 0.5f));

        var subOctave = PhraseClip("Saw -8ve", Climax2, Outro - Climax2,
            Theme.Select(n => (n.Beat, n.Note - 12, n.Length)).ToArray(), 0.5f);
        track.Clips.Add(subOctave);
        return track;
    }

    private static Track BuildCounter(IInstrumentRegistry instruments)
    {
        var counter = PresetInstrument(instruments.Create(FieldInstrument.Id), "Solace Lead");
        var track = NewInstrumentTrack("Counter", "CatppuccinGreen", 0.38, counter);
        track.Pan = -0.3;
        track.Effects.Add(new ReverbEffect { Mix = 0.35, RoomSize = 0.85, Damping = 0.4, Quality = 1 });

        // Enters half-way through the second breakdown and carries into the final climax.
        track.Clips.Add(PhraseClip("Counter", Break2 + 16, Climax2 - Break2 - 16, Counter, 0.6f));
        track.Clips.Add(PhraseClip("Counter", Climax2, Outro - Climax2, Counter, 0.65f));
        return track;
    }

    // ---- Pads and risers ----

    private static Track BuildPads(Project project)
    {
        var track = NewInstrumentTrack("Pads High", "CatppuccinPink", 0.48, PresetInstrument(new PaddaInstrument(), "Dusk Pads"));
        track.Effects.Add(new FilterEffect { Mode = FilterMode.LowPass, Frequency = 600, Resonance = 0.8 });
        track.Effects.Add(new SidechainEffect { Amount = 0.35, RateIndex = 2 }); // 1/4-note pump
        track.Effects.Add(new StereoWidthEffect { Width = 1.2 });

        var clip = new Clip { Name = "Pads High", StartBeat = Beat(Groove + 8), LengthBeats = (Bars - Groove - 8) * BeatsPerBar, IsAudio = false };
        for (var bar = 0; bar < Bars - Groove - 8; bar++)
        {
            foreach (var note in PadChords[(Groove + 8 + bar) % CycleBars])
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

        // The pads breathe with the arrangement: closed in the groove, wide open in the breakdowns
        // and climaxes, closing down over the outro.
        Automate(track, project, AutomationTargetKind.EffectParam, 0, 1,
            (Beat(Groove + 8), 600, 0.2), (Beat(Break1), 2800, 0), (Beat(Climax1), 3400, 0),
            (Beat(Mid), 1400, 0), (Beat(Break2), 3000, 0), (Beat(Climax2), 3600, 0),
            (Beat(Outro), 2200, 0), (Beat(Bars), 300, -0.2));

        // The stereo image widens through the breakdowns and pulls back for the dense climaxes.
        Automate(track, project, AutomationTargetKind.EffectParam, 2, 0,
            (Beat(Groove + 8), 1.0, 0), (Beat(Break1), 1.7, 0), (Beat(Climax1), 1.2, 0),
            (Beat(Break2), 1.8, 0), (Beat(Climax2), 1.3, 0), (Beat(Bars), 1.0, 0));

        // Slow eight-bar volume waves in and out of the phrase boundaries keep the layer alive.
        Automate(track, project, AutomationTargetKind.TrackVolume, -1, -1,
            PadVolumeWave(Groove + 8, low: 0.48, high: 0.62, startHigh: false));
        return track;
    }

    /// <summary>The warm low layer: Dusk Pads on root+fifth dyads an octave down, its own darker
    /// filter arc and a volume wave in antiphase with the high pads so the bed constantly moves.</summary>
    private static Track BuildPadsLow(Project project)
    {
        var track = NewInstrumentTrack("Pads Low", "CatppuccinLavender", 0.5, PresetInstrument(new PaddaInstrument(), "Dusk Pads"));
        track.Effects.Add(new FilterEffect { Mode = FilterMode.LowPass, Frequency = 500, Resonance = 0.7 });
        track.Effects.Add(new SidechainEffect { Amount = 0.4, RateIndex = 2 });
        track.Effects.Add(new StereoWidthEffect { Width = 1.3 });
        track.Effects.Add(new PhaserEffect { RateHz = 0.15, Depth = 0.6, Feedback = 0.3, Mix = 0.3 }); // slow swirl

        var clip = new Clip { Name = "Pads Low", StartBeat = Beat(Break1), LengthBeats = (Bars - Break1) * BeatsPerBar, IsAudio = false };
        for (var bar = 0; bar < Bars - Break1; bar++)
        {
            foreach (var note in PadLowDyads[(Break1 + bar) % CycleBars])
            {
                clip.Notes.Add(new MidiNote
                {
                    Note = note,
                    StartBeat = bar * BeatsPerBar,
                    LengthBeats = BeatsPerBar - 0.05,
                    Velocity = 0.6f
                });
            }
        }

        track.Clips.Add(clip);

        Automate(track, project, AutomationTargetKind.EffectParam, 0, 1,
            (Beat(Break1), 500, 0.2), (Beat(Climax1), 1400, 0), (Beat(Mid), 700, 0),
            (Beat(Break2), 1200, 0), (Beat(Climax2), 1600, 0), (Beat(Bars), 250, -0.2));
        Automate(track, project, AutomationTargetKind.TrackVolume, -1, -1,
            PadVolumeWave(Break1, low: 0.35, high: 0.5, startHigh: true));
        return track;
    }

    /// <summary>Builds a slow volume wave (alternating every 8 bars with eased curves) from
    /// <paramref name="fromBar"/> to the outro, then a fade to silence at the end.</summary>
    private static (double Beat, double Value, double Curve)[] PadVolumeWave(
        int fromBar, double low, double high, bool startHigh)
    {
        var points = new System.Collections.Generic.List<(double, double, double)>();
        var highNow = startHigh;
        for (var bar = fromBar; bar <= Outro; bar += 8)
        {
            points.Add((Beat(bar), highNow ? high : low, highNow ? 0.25 : -0.25));
            highNow = !highNow;
        }

        points.Add((Beat(Bars) - 4, 0.05, 0));
        return points.ToArray();
    }

    /// <summary>The environmental bed: Deep Space droning the tonic root+fifth far behind everything,
    /// swelling through the breakdowns and receding under the dense climaxes.</summary>
    private static Track BuildAtmos(Project project, Guid kickTrackId)
    {
        var track = NewInstrumentTrack("Atmos", "CatppuccinOverlay1", 0.45, PresetInstrument(new PaddaInstrument(), "Deep Space"));
        track.Effects.Add(new StereoWidthEffect { Width = 1.6 });
        // Duck the wash off the kick so the low-mids stay clear for the bass family.
        track.Effects.Add(new SidechainEffect { SourceTrackId = kickTrackId, Amount = 0.35, AttackMs = 4, ReleaseMs = 160 });

        var clip = new Clip { Name = "Atmos", StartBeat = 0, LengthBeats = Bars * BeatsPerBar, IsAudio = false };
        for (var bar = 0; bar < Bars; bar += 8)
        {
            foreach (var note in new[] { 45, 52 }) // A2 + E3 — tonic drone
            {
                clip.Notes.Add(new MidiNote
                {
                    Note = note,
                    StartBeat = bar * BeatsPerBar,
                    LengthBeats = 8 * BeatsPerBar - 0.1,
                    Velocity = 0.55f
                });
            }
        }

        track.Clips.Add(clip);

        Automate(track, project, AutomationTargetKind.TrackVolume, -1, -1,
            (0, 0.5, 0), (Beat(Groove), 0.35, 0), (Beat(Break1), 0.55, 0), (Beat(Climax1), 0.25, 0),
            (Beat(Break2), 0.55, 0), (Beat(Climax2), 0.25, 0), (Beat(Outro), 0.5, 0), (Beat(Bars) - 4, 0.05, 0));
        return track;
    }

    /// <summary>Crash washes marking every section boundary (plus the phrase midpoints of the
    /// climaxes) — the splashes that stitch the arrangement's seams together.</summary>
    private static Track BuildCrash()
    {
        var track = NewInstrumentTrack("Crash", "CatppuccinSubtext0", 0.6, PresetInstrument(new PercaInstrument(), "Crash"));
        track.Effects.Add(new ReverbEffect { Mix = 0.35, RoomSize = 0.8, Damping = 0.3, Quality = 1 });

        var clip = new Clip { Name = "Crashes", StartBeat = 0, LengthBeats = Bars * BeatsPerBar, IsAudio = false };
        foreach (var bar in new[]
                 {
                     Groove, Break1, Climax1, Climax1 + 16, Mid,
                     Break2, Break2 + 16, Climax2, Climax2 + 16, Outro
                 })
        {
            clip.Notes.Add(new MidiNote { Note = 60, StartBeat = Beat(bar), LengthBeats = 0.5, Velocity = 0.9f });
        }

        track.Clips.Add(clip);
        return track;
    }

    // Every section boundary gets a buildup layer; the climaxes get the full 8-bar treatment.
    // The key-change bar counts as a transition too — its sweep and crash sell the lift.
    private static readonly int[] MinorTransitions = { Groove, Break1, Mid, Break2, KeyChangeBar, Outro };

    private static Track BuildRiser(Project project)
    {
        var track = NewInstrumentTrack("Riser", "CatppuccinRosewater", 0.0, FactoryPresets.WhiteRiser());
        track.Effects.Add(new FilterEffect { Mode = FilterMode.LowPass, Frequency = 300, Resonance = 2.0 });
        track.Effects.Add(new FlangerEffect { RateHz = 0.25, Depth = 0.7, Feedback = 0.4, Mix = 0.4 }); // jet-engine sweep

        // The big climax sweeps run through the drop: eight bars up, four bars falling away as a
        // downlifter — the wash that lets the climax breathe instead of cutting to silence.
        foreach (var startBar in new[] { Climax1 - 8, Climax2 - 8 })
        {
            var clip = new Clip { Name = "Riser", StartBeat = Beat(startBar), LengthBeats = 12 * BeatsPerBar, IsAudio = false };
            clip.Notes.Add(new MidiNote { Note = 60, StartBeat = 0, LengthBeats = 12 * BeatsPerBar - 0.1, Velocity = 0.9f });
            track.Clips.Add(clip);
        }

        // Every other section boundary gets a short two-bar mini-sweep.
        foreach (var bar in MinorTransitions)
        {
            var clip = new Clip { Name = "Mini Riser", StartBeat = Beat(bar - 2), LengthBeats = 2 * BeatsPerBar, IsAudio = false };
            clip.Notes.Add(new MidiNote { Note = 60, StartBeat = 0, LengthBeats = 2 * BeatsPerBar - 0.1, Velocity = 0.8f });
            track.Clips.Add(clip);
        }

        var cutoff = new System.Collections.Generic.List<(double, double, double)> { (0, 300, 0) };
        var volume = new System.Collections.Generic.List<(double, double, double)> { (0, 0, 0) };
        foreach (var bar in MinorTransitions)
        {
            cutoff.Add((Beat(bar - 2), 300, -0.3));
            cutoff.Add((Beat(bar), 6000, 0));
            cutoff.Add((Beat(bar) + 0.5, 300, 0));
            volume.Add((Beat(bar - 2), 0, -0.3));
            volume.Add((Beat(bar), 0.5, 0));
            volume.Add((Beat(bar) + 0.5, 0, 0));
        }

        foreach (var climax in new[] { Climax1, Climax2 })
        {
            cutoff.Add((Beat(climax - 8), 300, -0.4));
            cutoff.Add((Beat(climax), 10000, 0.3));
            cutoff.Add((Beat(climax + 4), 400, 0));
            volume.Add((Beat(climax - 8), 0, -0.3));
            volume.Add((Beat(climax), 0.8, 0.3));
            volume.Add((Beat(climax + 4), 0, 0));
        }

        Automate(track, project, AutomationTargetKind.EffectParam, 0, 1, cutoff.ToArray());
        Automate(track, project, AutomationTargetKind.TrackVolume, -1, -1, volume.ToArray());
        return track;
    }

    /// <summary>The harmonic riser layer: Comet Riser saws climbing ~24 semitones under the noise
    /// sweep through each climax build — the "second voice" that makes the rise feel enormous.</summary>
    private static Track BuildTonalRiser(Project project, IInstrumentRegistry instruments)
    {
        var comet = PresetInstrument(instruments.Create(FieldInstrument.Id), "Comet Riser");
        var track = NewInstrumentTrack("Tonal Riser", "CatppuccinSapphire", 0.0, comet);
        track.Effects.Add(new ReverbEffect { Mix = 0.35, RoomSize = 0.8, Damping = 0.35, Quality = 1 });

        foreach (var climax in new[] { Climax1, Climax2 })
        {
            var clip = new Clip { Name = "Comet", StartBeat = Beat(climax - 4), LengthBeats = 4 * BeatsPerBar, IsAudio = false };
            clip.Notes.Add(new MidiNote { Note = 57, StartBeat = 0, LengthBeats = 4 * BeatsPerBar - 0.1, Velocity = 0.9f });
            track.Clips.Add(clip);
        }

        Automate(track, project, AutomationTargetKind.TrackVolume, -1, -1,
            (0, 0, 0), (Beat(Climax1 - 4), 0, -0.3), (Beat(Climax1), 0.55, 0), (Beat(Climax1) + 0.5, 0, 0),
            (Beat(Climax2 - 4), 0, -0.3), (Beat(Climax2), 0.55, 0), (Beat(Climax2) + 0.5, 0, 0));
        return track;
    }

    /// <summary>Reverse-cymbal swells whooshing into every section boundary — the third buildup
    /// layer, gluing even the quiet transitions together.</summary>
    private static Track BuildSweeps()
    {
        var track = NewInstrumentTrack("Sweeps", "CatppuccinTeal", 0.55, FactoryPresets.ReverseCymbal());
        track.Effects.Add(new ReverbEffect { Mix = 0.3, RoomSize = 0.75, Damping = 0.35, Quality = 1 });
        track.Effects.Add(new StereoWidthEffect { Width = 1.4 });

        var clip = new Clip { Name = "Sweeps", StartBeat = 0, LengthBeats = Bars * BeatsPerBar, IsAudio = false };
        foreach (var bar in MinorTransitions.Concat(new[] { Climax1, Climax2 }).OrderBy(b => b))
        {
            clip.Notes.Add(new MidiNote
            {
                Note = 69,
                StartBeat = Beat(bar - 2),
                LengthBeats = 2 * BeatsPerBar - 0.05,
                Velocity = 0.85f
            });
        }

        track.Clips.Add(clip);
        return track;
    }

    // ---- Helpers ----

    /// <summary>Dotted-eighth delay time at the song tempo (the trance pluck delay).</summary>
    private static double DottedEighthMs() => 60000.0 / Bpm * 0.75;
}
