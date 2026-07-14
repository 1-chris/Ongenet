using System;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Containers;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Field.Nodes;
using Ongenet.Core.Audio.Hardware;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.MidiFx;
using Ongenet.Core.Audio.Modulation;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Tests.Library;

public class DeviceRegistryTests
{
    [Fact]
    public void EffectRegistry_ContainsCoreBuiltInDevices()
    {
        var reg = new EffectRegistry();
        string[] mustHave =
        {
            SaturatorEffect.TypeId, AmpEffect.TypeId, CombEffect.TypeId, PitchShiftEffect.TypeId,
            FreqShiftEffect.TypeId, FreqShiftPlusEffect.TypeId, RingModEffect.TypeId, DynamicsEffect.TypeId, DeEsserEffect.TypeId,
            TransientControlEffect.TypeId, SpectrumEffect.TypeId, TunerEffect.TypeId, TestToneEffect.TypeId,
            ConvolutionEffect.TypeId, Delay1Effect.TypeId, Delay2Effect.TypeId, Delay4Effect.TypeId,
            DelayPlusEffect.TypeId, Eq2Effect.TypeId, Eq5Effect.TypeId, EqDjEffect.TypeId, EqPlusEffect.TypeId,
            TiltEffect.TypeId, RotaryEffect.TypeId, ResonatorBankEffect.TypeId, BlurEffect.TypeId,
            FocusEffect.TypeId, SculptEffect.TypeId, SweepEffect.TypeId, TreemonsterEffect.TypeId,
            DcOffsetEffect.TypeId, DualPanEffect.TypeId, FilterPlusEffect.TypeId, LadderEffect.TypeId,
            OverEffect.TypeId, TimeShiftEffect.TypeId, FxLayerEffect.TypeId, MultibandFxEffect.TypeId2,
            BitcrusherEffect.TypeId, CompressorPlusEffect.TypeId, ChorusPlusEffect.TypeId, FlangerPlusEffect.TypeId,
            PhaserPlusEffect.TypeId, PeakLimiterEffect.TypeId, ToolEffect.TypeId, OscilloscopeEffect.TypeId
        };

        foreach (var id in mustHave)
            Assert.NotNull(reg.Create(id));

        Assert.True(reg.Available.Count >= 54);
    }

    [Fact]
    public void InstrumentRegistry_ContainsOrganPhase4DrumModelAndContainers()
    {
        var reg = new InstrumentRegistry();
        Assert.IsType<OrganInstrument>(reg.Create(OrganInstrument.TypeId));
        Assert.IsType<Phase4Instrument>(reg.Create(Phase4Instrument.TypeId));
        Assert.IsType<DrumModelInstrument>(reg.Create(DrumModelInstrument.TypeId));
        Assert.IsType<PolymerInstrument>(reg.Create(PolymerInstrument.TypeId));
        Assert.IsType<PolysynthInstrument>(reg.Create(PolysynthInstrument.TypeId));
        Assert.IsType<DrumMachineInstrument>(reg.Create(DrumMachineInstrument.TypeId));
        Assert.IsType<InstrumentLayerInstrument>(reg.Create(InstrumentLayerInstrument.TypeId));
    }

    [Fact]
    public void MidiEffectRegistry_CoversCoreNoteFxSet()
    {
        var reg = new MidiEffectRegistry();
        Assert.True(reg.Available.Count >= 25);
        Assert.Equal("Scale", reg.Create(ScaleMidiEffect.TypeId).Name);
        Assert.Equal("Arpeggiator", reg.Create(ArpMidiEffect.TypeId).Name);
        Assert.Equal("Humanize", reg.Create(HumanizeMidiEffect.TypeId).Name);
        Assert.Equal("Ricochet", reg.Create(RicochetMidiEffect.TypeId).Name);
        Assert.Equal("MIDI Song Select", reg.Create(MidiSongSelectMidiEffect.TypeId).Name);
    }

    [Fact]
    public void FieldNodeCatalog_HasAtLeast200BuiltInNodes()
    {
        var count = FieldNodeCatalog.BuiltIns().Count();
        Assert.True(count >= 200, $"Expected >= 200 Field nodes, found {count}");
    }

    [Fact]
    public void FieldNodeCatalog_IncludesContainerSpectralAndModulatorNodes()
    {
        var nodes = new FieldNodeRegistry();
        Assert.NotNull(nodes.TryCreate(ContainerLayerNode.Type));
        Assert.NotNull(nodes.TryCreate(ContainerMultibandNode.Type));
        Assert.NotNull(nodes.TryCreate(SpectralSplitNode.Type));
        Assert.NotNull(nodes.TryCreate(SpectralTransientNode.Type));
        Assert.NotNull(nodes.TryCreate(SegmentsNode.Type));
        Assert.NotNull(nodes.TryCreate(WavetableLfoNode.Type));
        Assert.NotNull(nodes.TryCreate(BeatLfoNode.Type));
        Assert.NotNull(nodes.TryCreate(MathCvNode.Type));
        Assert.NotNull(nodes.TryCreate(KarplusNode.Type));
        Assert.NotNull(nodes.TryCreate(PartialBankNode.Type));
        Assert.NotNull(nodes.TryCreate(SpectralImportNode.Type));
    }

    [Fact]
    public void ModulatorRegistry_Contains43Modulators()
    {
        var reg = new ModulatorRegistry();
        Assert.Equal(43, reg.Available.Count);
        foreach (var info in reg.Available)
        {
            var mod = info.Create();
            Assert.NotNull(mod.Clone());
            var v = mod.Evaluate(new ModulatorContext { Bpm = 120 });
            Assert.InRange(v, 0, 1);
        }
    }

    [Fact]
    public void ModulatorSlot_BindsTrackVolume()
    {
        var track = new Track { Name = "Mod", Kind = TrackKind.Instrument, Volume = 0.5 };
        track.ModulatorSlots.Add(new ModulatorSlot
        {
            Depth = 1.0,
            Source = new MacroModulator { Value = 0.0 },
            Target = new AutomationBinding(AutomationTargetKind.TrackVolume, -1, -1)
        });
        track.CommitModulatorSlots();

        TrackModulatorDriver.ApplyTrack(track, beat: 0, bpm: 120);
        Assert.Equal(0.0, track.Volume, 3);
    }

    [Fact]
    public void ContainerFxLayer_RoundTripsAndRenders()
    {
        var fx = new FxLayerEffect();

        var clone = new FxLayerEffect();
        using (var ms = new MemoryStream())
        {
            using (var w = new OngenWriter(ms))
            {
                using (ContainerWriteContext.Scope(new ContainerWriteContext { Store = new SampleStore() }))
                    fx.WriteProjectState(w);
            }

            ms.Position = 0;
            using var r = new OngenReader(ms);
            using (ContainerReadContext.Scope(new ContainerReadContext
            {
                Instruments = new InstrumentRegistry(),
                Effects = new EffectRegistry(),
                MidiEffects = new MidiEffectRegistry(),
                SampleLookup = _ => null,
                Warnings = new System.Collections.Generic.List<string>()
            }))
                clone.ReadProjectState(r);
        }

        Assert.Equal(2, clone.Branches.Count);
        Assert.Single(clone.Branches[0].Effects);

        var format = new AudioFormat(44100, 2);
        var buffer = new float[512];
        for (var i = 0; i < buffer.Length; i++) buffer[i] = 0.4f;
        clone.Prepare(format);
        clone.Process(buffer);
        foreach (var s in buffer) Assert.True(float.IsFinite(s));
    }

    [Fact]
    public void DrumMachineContainer_RoundTripsPadRouting()
    {
        var inst = new DrumMachineInstrument();
        var clone = new DrumMachineInstrument();
        using (var ms = new MemoryStream())
        {
            using (var w = new OngenWriter(ms)) inst.WriteProjectState(w);
            ms.Position = 0;
            using var r = new OngenReader(ms);
            using (ContainerReadContext.Scope(new ContainerReadContext
            {
                Instruments = new InstrumentRegistry(),
                Effects = new EffectRegistry(),
                MidiEffects = new MidiEffectRegistry(),
                SampleLookup = _ => null,
                Warnings = new System.Collections.Generic.List<string>()
            }))
                clone.ReadProjectState(r);
        }

        Assert.True(clone.Children.Count >= 8);
        var routed = inst.RouteNote(36, 1.0f);
        Assert.NotEmpty(routed);

        var format = new AudioFormat(44100, 2);
        clone.Prepare(format);
        var buffer = new float[512];
        clone.Render(buffer);
        foreach (var s in buffer) Assert.True(float.IsFinite(s));
    }

    [Fact]
    public void NewFieldNodes_GraphSerializationRoundTrips()
    {
        var reg = new FieldNodeRegistry();
        var graph = new FieldGraph();
        var split = reg.Create(SpectralSplitNode.Type);
        var layer = reg.Create(ContainerLayerNode.Type);
        var seg = reg.Create(SegmentsNode.Type);
        graph.AddNode(split);
        graph.AddNode(layer);
        graph.AddNode(seg);
        graph.Connect(split.Id, "low", layer.Id, "a");
        graph.Connect(split.Id, "high", layer.Id, "b");

        var restored = new FieldGraph();
        using var ms = new MemoryStream();
        using (var w = new OngenWriter(ms)) FieldGraphSerializer.Write(w, graph);
        ms.Position = 0;
        using (var r = new OngenReader(ms)) FieldGraphSerializer.Read(r, restored, reg);

        Assert.Equal(3, restored.Nodes.Count);
        Assert.Equal(2, restored.Connections.Count);
        Assert.Contains(restored.Nodes, n => n.TypeId == SegmentsNode.Type);
    }

    [Fact]
    public void NewEffects_PrepareAndProcessWithoutThrowing()
    {
        var format = new AudioFormat(44100, 2);
        var buffer = new float[512];
        var reg = new EffectRegistry();
        foreach (var info in reg.Available)
        {
            var fx = info.Create();
            fx.Prepare(format);
            fx.Process(buffer);
            Assert.NotNull(fx.Clone());
        }
    }
}
