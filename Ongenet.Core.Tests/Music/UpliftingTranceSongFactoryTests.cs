using System.IO;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Field.Nodes;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Music;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Tests.Music;

/// <summary>
/// Verifies the "Ascension" uplifting trance built-in project: tempo/length (a real ~6:30 trance
/// arrangement at 138 BPM), the A-minor key with its Oda-style dominant colouring, the layered
/// Field patches, snare-roll builds, sidechains, and .ongen round-trip fidelity.
/// </summary>
public class UpliftingTranceSongFactoryTests
{
    // A natural minor (A B C D E F G) plus the G# leading tone of the E-dominant turnaround.
    private static readonly int[] AMinor = { 9, 11, 0, 2, 4, 5, 7, 8 };

    // B natural minor plus its A# leading tone — whole-step lift in the final climax statement.
    private static readonly int[] BMinor = { 11, 1, 2, 4, 6, 7, 9, 10 };

    // The modulated window: the second half of the final climax (bars 168–184 → beats 672–736).
    private const double KeyChangeStartBeat = 168 * 4.0;
    private const double KeyChangeEndBeat = 184 * 4.0;

    private static IInstrumentRegistry Registry()
    {
        var instruments = new InstrumentRegistry();
        FieldBootstrap.Initialize(new FieldNodeRegistry(), instruments, new EffectRegistry());
        return instruments;
    }

    [Fact]
    public void SongHasExpectedGlobalsAndLength()
    {
        var song = UpliftingTranceSongFactory.Create(Registry());

        Assert.Equal("Ascension", song.Name);
        Assert.Equal(138.0, song.Tempo.BeatsPerMinute);
        Assert.Equal(224, song.BarCount);
        Assert.NotNull(song.Master);
        Assert.Contains(song.Master!.Effects, e => e is WaveformVisualizerEffect);

        // A true trance runtime: 224 bars at 138 BPM ≈ 6:30 (within the 6–7 minute brief).
        var seconds = song.BarCount * 4 * 60.0 / song.Tempo.BeatsPerMinute;
        Assert.InRange(seconds, 360, 420);
    }

    [Fact]
    public void AllPitchedNotesAreInAMinorAndWithinTheArrangement()
    {
        var song = UpliftingTranceSongFactory.Create(Registry());
        var endBeat = song.BarCount * 4.0;

        foreach (var track in song.Tracks.Where(t => t.Kind == TrackKind.Instrument))
        {
            var pitched = !UpliftingTranceSongFactory.UnpitchedTracks.Contains(track.Name);
            Assert.NotEmpty(track.Clips);
            foreach (var clip in track.Clips)
            {
                Assert.True(clip.StartBeat >= 0 && clip.EndBeat <= endBeat,
                    $"clip '{clip.Name}' on '{track.Name}' must lie within the arrangement");
                Assert.NotEmpty(clip.Notes);
                foreach (var note in clip.Notes)
                {
                    if (pitched)
                    {
                        var absolute = clip.StartBeat + note.StartBeat;
                        var scale = absolute >= KeyChangeStartBeat && absolute < KeyChangeEndBeat
                            ? BMinor   // final-statement modulation
                            : AMinor;  // home key, incl. outro resolve
                        Assert.Contains(note.Note % 12, scale);
                    }

                    Assert.True(note.StartBeat >= 0 && note.StartBeat + note.LengthBeats <= clip.LengthBeats + 1e-6,
                        $"note in '{clip.Name}' must lie within its clip");
                }
            }
        }
    }

    [Fact]
    public void LayeringUsesTheExpectedFieldPatches()
    {
        var song = UpliftingTranceSongFactory.Create(Registry());

        // Plucks + arp: the Crystal Pluck decomposition (curve-env cutoff kick into a filter).
        foreach (var name in new[] { "Plucks", "Arp" })
        {
            var field = Assert.IsType<FieldInstrument>(song.Tracks.Single(t => t.Name == name).PrimaryInstrument);
            Assert.Contains(field.Graph.Nodes, n => n is CurveEnvNode);
            Assert.Contains(field.Graph.Nodes, n => n is BiquadFilterNode);
        }

        // Anthem: the wide Nova Saw supersaw.
        var anthem = Assert.IsType<FieldInstrument>(song.Tracks.Single(t => t.Name == "Anthem").PrimaryInstrument);
        Assert.Contains(anthem.Graph.Nodes, n => n is UnisonOscNode);

        // Theme + counter: the Solace Lead — a layered voice (unison chorusing + anchor oscillators)
        // with a per-note filter bloom.
        foreach (var name in new[] { "Theme", "Counter" })
        {
            var field = Assert.IsType<FieldInstrument>(song.Tracks.Single(t => t.Name == name).PrimaryInstrument);
            Assert.Contains(field.Graph.Nodes, n => n is UnisonOscNode);
            Assert.True(field.Graph.Nodes.OfType<WaveOscNode>().Count() >= 2, "anchor + warmth layers");
            Assert.Contains(field.Graph.Nodes, n => n is CurveEnvNode); // the cutoff bloom
        }
    }

    [Fact]
    public void BassAndAnthemSidechainFromTheKick()
    {
        var song = UpliftingTranceSongFactory.Create(Registry());
        var kick = song.Tracks.Single(t => t.Name == "Kick");

        foreach (var name in new[] { "Bass", "Anthem", "Saw Layer", "Sub", "Acid", "Bells", "Atmos" })
        {
            var track = song.Tracks.Single(t => t.Name == name);
            var sidechain = track.Effects.OfType<SidechainEffect>().Single();
            Assert.Equal(kick.Id, sidechain.SourceTrackId);
        }

        // Both pad layers ride the tempo pump instead.
        foreach (var name in new[] { "Pads High", "Pads Low" })
        {
            var pads = song.Tracks.Single(t => t.Name == name);
            Assert.False(pads.Effects.OfType<SidechainEffect>().Single().IsTrackMode);
        }
    }

    [Fact]
    public void SnareRollsCrescendoIntoBothClimaxes()
    {
        var song = UpliftingTranceSongFactory.Create(Registry());
        var snare = song.Tracks.Single(t => t.Name == "Snare");
        var rolls = snare.Clips.Where(c => c.Name == "Snare Roll").ToList();

        Assert.Equal(2, rolls.Count);
        foreach (var roll in rolls)
        {
            Assert.Equal(64, roll.Notes.Count); // four bars of 16ths
            Assert.True(roll.Notes[^1].Velocity > roll.Notes[0].Velocity, "the roll should crescendo");
        }
    }

    [Fact]
    public void IntroBassIsAPedalBeforeTheFullRollingLine()
    {
        var song = UpliftingTranceSongFactory.Create(Registry());
        var bass = song.Tracks.Single(t => t.Name == "Bass");

        // The intro clip repeats a single root note; the groove clip moves through the chord roots.
        var pedal = bass.Clips.Single(c => c.Name == "Bass Pedal");
        Assert.Single(pedal.Notes.Select(n => n.Note).Distinct());

        var rolling = bass.Clips.First(c => c.Name == "Bass");
        Assert.True(rolling.Notes.Select(n => n.Note).Distinct().Count() > 1, "the full line follows the changes");
    }

    [Fact]
    public void InstrumentsHaveFilterSweepAutomation()
    {
        var song = UpliftingTranceSongFactory.Create(Registry());

        // Plucks emerge through a high-pass; the arp and bass open through low-passes.
        foreach (var (name, mode) in new[]
                 {
                     ("Plucks", FilterMode.HighPass),
                     ("Arp", FilterMode.LowPass),
                     ("Bass", FilterMode.LowPass)
                 })
        {
            var track = song.Tracks.Single(t => t.Name == name);
            var filter = Assert.IsType<FilterEffect>(track.Effects[0]);
            Assert.Equal(mode, filter.Mode);
            Assert.Contains(track.AutoLanes, l =>
                l.Binding is { Kind: Ongenet.Core.Audio.Automation.AutomationTargetKind.EffectParam, EffectIndex: 0, ParamIndex: 1 }
                && l.Points.Select(p => p.Value).Distinct().Count() > 1);
        }
    }

    [Fact]
    public void LayeringIncludesSubAndBells()
    {
        var song = UpliftingTranceSongFactory.Create(Registry());

        var sub = song.Tracks.Single(t => t.Name == "Sub");
        Assert.IsType<TripleOscInstrument>(sub.PrimaryInstrument);
        Assert.True(sub.Effects.OfType<SidechainEffect>().Single().IsTrackMode);

        var bells = song.Tracks.Single(t => t.Name == "Bells");
        Assert.IsType<FmSynthInstrument>(bells.PrimaryInstrument);
    }

    [Fact]
    public void ThemeIsASimpleCallAndResponseHook()
    {
        var song = UpliftingTranceSongFactory.Create(Registry());
        var theme = song.Tracks.Single(t => t.Name == "Theme");

        // One lead voice in breakdowns — the supersaw wall is on the anthem layers, not stacked here.
        Assert.Single(theme.Instruments);
        Assert.Contains(theme.Effects, e => e is FilterEffect);

        var clip = theme.Clips.First(c => c.Name == "Theme");
        var firstHalf = clip.Notes.Where(n => n.StartBeat < 32).Select(n => (n.StartBeat, n.Note)).ToList();
        var secondHalf = clip.Notes.Where(n => n.StartBeat is >= 32 and < 64)
            .Select(n => (n.StartBeat - 32, n.Note)).ToList();
        Assert.NotEqual(firstHalf, secondHalf); // bars 15–16 break the restatement
        Assert.True(clip.Notes.Max(n => n.StartBeat) >= 32, "the phrase spans the full 16 bars");

        // A memorable hook: sparse (a handful of notes per bar), not a dense 16th fill.
        var oneCycle = clip.Notes.Count(n => n.StartBeat < 64);
        Assert.InRange(oneCycle, 30, 55);

        // Wide interval leaps into the top octave (the E6 peak) for that rush of euphoria.
        Assert.True(clip.Notes.Max(n => n.Note) >= 88, "the octave leap reaches the E6 peak");
    }

    [Fact]
    public void FestivalLeadStacksThreeDistinctLayers()
    {
        var song = UpliftingTranceSongFactory.Create(Registry());

        // Layer 1 — the wide stereo wings (Aether supersaw) with a ping-pong delay.
        var anthem = song.Tracks.Single(t => t.Name == "Anthem");
        Assert.Contains(anthem.Effects, e => e is DelayEffect { PingPong: true });

        // Layer 2 — the mono core: folded to dead centre for punch.
        var core = song.Tracks.Single(t => t.Name == "Lead Core");
        Assert.Equal(0.0, core.Pan);
        Assert.Contains(core.Effects, e => e is StereoWidthEffect { Width: 0.0 });
        Assert.NotEmpty(core.Clips);

        // Layer 3 — the transient click: high-passed so only the top-end snap remains.
        var click = song.Tracks.Single(t => t.Name == "Lead Click");
        Assert.Contains(click.Effects, e => e is FilterEffect { Mode: FilterMode.HighPass });
        Assert.NotEmpty(click.Clips);

        // The lead bus is inflated by the OTT-style multiband compressor.
        var leads = song.Tracks.Single(t => t.Name == "Leads");
        Assert.Contains(leads.Effects, e => e is MultibandCompressorEffect);
    }

    [Fact]
    public void LeadWallStacksThreeLayersWithOctaveInTheFinale()
    {
        var song = UpliftingTranceSongFactory.Create(Registry());

        // Theme (Prism Lead) + Anthem (Aether Lead) + Saw Layer (Nova Saw) form the lead stack.
        var anthem = Assert.IsType<FieldInstrument>(song.Tracks.Single(t => t.Name == "Anthem").PrimaryInstrument);
        Assert.True(anthem.Graph.Nodes.OfType<UnisonOscNode>().Count() >= 2, "the Aether Lead is a double unison");

        var sawLayer = song.Tracks.Single(t => t.Name == "Saw Layer");
        var subOctave = sawLayer.Clips.Single(c => c.Name == "Saw -8ve");
        var plain = sawLayer.Clips.Single(c => c.Name == "Saw Double");
        Assert.True(subOctave.Notes.Min(n => n.Note) < plain.Notes.Min(n => n.Note),
            "the final climax layer adds weight an octave down");
    }

    [Fact]
    public void StereoFieldIsUsedAcrossTheLayers()
    {
        var song = UpliftingTranceSongFactory.Create(Registry());

        // Complementary panning left/right of centre...
        Assert.True(song.Tracks.Single(t => t.Name == "Plucks").Pan < 0);
        Assert.True(song.Tracks.Single(t => t.Name == "Arp").Pan > 0);
        Assert.True(song.Tracks.Single(t => t.Name == "Sparkle").Pan < 0);
        Assert.True(song.Tracks.Single(t => t.Name == "Bells").Pan > 0);
        Assert.True(song.Tracks.Single(t => t.Name == "Counter").Pan < 0);

        // ...and width processing on the pad/bell layers.
        foreach (var name in new[] { "Pads High", "Pads Low", "Bells" })
            Assert.Contains(song.Tracks.Single(t => t.Name == name).Effects, e => e is StereoWidthEffect);
    }

    [Fact]
    public void TracksAreOrganizedIntoGroupBuses()
    {
        var song = UpliftingTranceSongFactory.Create(Registry());

        var groups = song.Tracks.Where(t => t.Kind == TrackKind.Group).Select(t => t.Name).ToList();
        Assert.Equal(new[] { "Drums", "Bass Bus", "Leads", "Atmosphere" }, groups);

        // Every non-bus content track routes into one of the groups.
        foreach (var track in song.Tracks.Where(t => t.Kind == TrackKind.Instrument))
        {
            var parent = song.Tracks.Single(t => t.Id == track.ParentId);
            Assert.Equal(TrackKind.Group, parent.Kind);
        }

        // The drum bus carries the punch-glue compressor; the leads bus is EQ'd and width-polished.
        var drums = song.Tracks.Single(t => t.Name == "Drums");
        Assert.Contains(drums.Effects, e => e is CompressorEffect);
        var leads = song.Tracks.Single(t => t.Name == "Leads");
        Assert.Contains(leads.Effects, e => e is EqEffect);
        Assert.Contains(leads.Effects, e => e is StereoWidthEffect);
        Assert.NotEmpty(leads.AutoLanes);
    }

    [Fact]
    public void AcidLineThumpsTheMidSectionAndFinalBuild()
    {
        var song = UpliftingTranceSongFactory.Create(Registry());
        var acid = song.Tracks.Single(t => t.Name == "Acid");

        Assert.IsType<FieldInstrument>(acid.PrimaryInstrument);
        Assert.Equal(2, acid.Clips.Count); // the mid-section and the second build
        Assert.Contains(acid.AutoLanes, l =>
            l.Binding is { Kind: Ongenet.Core.Audio.Automation.AutomationTargetKind.EffectParam, EffectIndex: 0 });
        Assert.True(acid.Effects.OfType<SidechainEffect>().Single().IsTrackMode);
    }

    [Fact]
    public void FactoryFxChainsAreDefinedAndBuildable()
    {
        Assert.True(FactoryPresets.ChainDefinitions.Count >= 4);
        foreach (var chain in FactoryPresets.ChainDefinitions)
        {
            var effects = chain.Create();
            Assert.NotEmpty(effects);
            Assert.NotSame(effects[0], chain.Create()[0]); // fresh instances every build
        }
    }

    [Fact]
    public void TransitionsAreLayeredAcrossThreeBuildupVoices()
    {
        var song = UpliftingTranceSongFactory.Create(Registry());

        // Noise riser: two 12-bar climax sweeps plus a mini-sweep into every other section
        // boundary (including the key-change bar).
        var riser = song.Tracks.Single(t => t.Name == "Riser");
        Assert.Equal(2, riser.Clips.Count(c => c.Name == "Riser"));
        Assert.Equal(6, riser.Clips.Count(c => c.Name == "Mini Riser"));

        // Tonal layer: the Comet Riser climbs into both climaxes.
        var tonal = song.Tracks.Single(t => t.Name == "Tonal Riser");
        Assert.Equal(2, tonal.Clips.Count);
        Assert.IsType<FieldInstrument>(tonal.PrimaryInstrument);

        // Reverse-cymbal whooshes land on every transition (6 minor incl. key change + 2 climaxes).
        var sweeps = song.Tracks.Single(t => t.Name == "Sweeps");
        Assert.Equal(8, sweeps.Clips.Single().Notes.Count);
    }

    [Fact]
    public void MasterChainFollowsTheTranceMasteringOrder()
    {
        var song = UpliftingTranceSongFactory.Create(Registry());
        var fx = song.Master!.Effects;

        // Corrective EQ → mid/side → glue comp → (width) → soft clip → brickwall limiter → meter.
        var eqAt = fx.ToList().FindIndex(e => e is EqEffect);
        var msAt = fx.ToList().FindIndex(e => e is MidSideEqEffect);
        var compAt = fx.ToList().FindIndex(e => e is CompressorEffect);
        var clipAt = fx.ToList().FindIndex(e => e is ClipperEffect);
        var limAt = fx.ToList().FindIndex(e => e is LimiterEffect);

        Assert.True(eqAt >= 0 && msAt > eqAt && compAt > msAt && clipAt > compAt && limAt > clipAt,
            "the master chain must run in the canonical trance order");

        // The corrective EQ mono-folds nothing but strips sub-rumble and hiss.
        var eq = (EqEffect)fx[eqAt];
        Assert.Contains(eq.Bands, b => b.Type == EqBandType.HighPass && b.Frequency <= 30);
        Assert.Contains(eq.Bands, b => b.Type == EqBandType.LowPass && b.Frequency >= 19000);

        // The mid/side stage mono's the sub and adds air to the sides.
        var ms = (MidSideEqEffect)fx[msAt];
        Assert.InRange(ms.SideLowCutHz, 100, 140);
        Assert.True(ms.SideAirDb > 0);

        // The glue compressor is gentle (2:1, slow attack) and the limiter leaves ISP headroom.
        var comp = (CompressorEffect)fx[compAt];
        Assert.Equal(2.0, comp.Ratio);
        Assert.True(comp.AttackMs >= 25, "slow attack lets transients through");
        Assert.Equal(-1.0, ((LimiterEffect)fx[limAt]).CeilingDb);
    }

    [Fact]
    public void AnthemCarriesAHarmonisedChordLead()
    {
        var song = UpliftingTranceSongFactory.Create(Registry());
        var anthem = song.Tracks.Single(t => t.Name == "Anthem");
        var clip = anthem.Clips.First(c => c.Name == "Anthem");

        // Every melody note is stacked into a triad (three simultaneous notes on the same beat).
        var beat = clip.Notes.First().StartBeat;
        var stacked = clip.Notes.Count(n => Math.Abs(n.StartBeat - beat) < 1e-6);
        Assert.True(stacked >= 3, "the anthem lead should be a stacked chord, not a single line");

        // Roughly 3× the notes of the single-line theme it harmonises.
        var theme = song.Tracks.Single(t => t.Name == "Theme").Clips.First(c => c.Name == "Theme");
        Assert.True(clip.Notes.Count > theme.Notes.Count * 2, "the chord lead triples the melody voices");
    }

    [Fact]
    public void SongRoundTripsThroughProjectFile()
    {
        var instruments = new InstrumentRegistry();
        FieldBootstrap.Initialize(new FieldNodeRegistry(), instruments, new EffectRegistry());
        var song = UpliftingTranceSongFactory.Create(instruments);

        using var ms = new MemoryStream();
        ProjectFile.Save(song, ms, "test", 0, 0, 0);
        ms.Position = 0;
        var loaded = ProjectFile.Load(ms, instruments, new EffectRegistry()).Project;

        Assert.Equal(song.Name, loaded.Name);
        Assert.Equal(song.Tempo.BeatsPerMinute, loaded.Tempo.BeatsPerMinute);
        Assert.Equal(song.Tracks.Count, loaded.Tracks.Count);
        foreach (var (before, after) in song.Tracks.Zip(loaded.Tracks))
        {
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.Clips.Sum(c => c.Notes.Count), after.Clips.Sum(c => c.Notes.Count));
            Assert.Equal(before.AutoLanes.Count, after.AutoLanes.Count);
        }
    }

    [Fact]
    public void CatalogListsAllBuiltInProjects()
    {
        var names = BuiltInProjects.All.Select(p => p.Name).ToList();
        Assert.Equal(9, names.Count);
        Assert.Contains("First Light", names);
        Assert.Contains("Undertow", names);
        Assert.Contains("Ascension", names);
        Assert.Contains("Dust & Vinyl", names);
        Assert.Contains("House Starter", names);
        Assert.Contains("Static Bloom", names);
        Assert.Contains("Techno Starter", names);
        Assert.Contains("Trap Beat", names);
        Assert.Contains("Field Modular", names);
    }
}
