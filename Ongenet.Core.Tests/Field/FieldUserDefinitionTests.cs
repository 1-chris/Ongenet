using System;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Field.Nodes;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Tests.Field;

public class FieldUserDefinitionTests
{
    private static readonly AudioFormat Fmt = new(44100, 2);

    private static (IFieldNodeRegistry nodes, IInstrumentRegistry inst, IEffectRegistry fx) Registries()
    {
        var nodes = new FieldNodeRegistry();
        var inst = new InstrumentRegistry();
        var fx = new EffectRegistry();
        FieldBootstrap.Initialize(nodes, inst, fx);
        return (nodes, inst, fx);
    }

    private static FieldGraph MakeInstrumentGraph(IFieldNodeRegistry reg)
    {
        var g = new FieldGraph();
        var note = new NoteInNode { X = 0, Y = 0 };
        var osc = new WaveOscNode { X = 180, Y = 0 };
        var outN = new AudioOutNode { X = 360, Y = 0 };
        g.AddNode(note);
        g.AddNode(osc);
        g.AddNode(outN);
        g.Connect(note.Id, "pitch", osc.Id, "pitch");
        g.Connect(osc.Id, "out", outN.Id, "in");
        return g;
    }

    private static FieldGraph MakeEffectGraph()
    {
        var g = new FieldGraph();
        var ain = new AudioInNode { X = 0, Y = 0 };
        var aout = new AudioOutNode { X = 200, Y = 0 };
        g.AddNode(ain);
        g.AddNode(aout);
        g.Connect(ain.Id, "out", aout.Id, "in");
        return g;
    }

    [Fact]
    public void DefinitionPackageRoundTrips()
    {
        var (nodes, _, _) = Registries();
        var graph = MakeInstrumentGraph(nodes);
        var osc = graph.Nodes.OfType<WaveOscNode>().First();
        var surface = new FieldSurfaceDefinition
        {
            CanvasWidth = 400,
            CanvasHeight = 240,
            ExposedControls =
            {
                new FieldExposedControl
                {
                    NodeId = osc.Id,
                    ParamIndex = 1, // Coarse on WaveOscNode
                    ExpectedKind = FieldBoundParamKind.Float,
                    DisplayName = "Tune"
                }
            },
            Widgets =
            {
                new FieldWidget
                {
                    Kind = FieldWidgetKind.Knob,
                    Label = "Tune",
                    BindingKind = FieldWidgetBindingKind.Parameter,
                    ParameterBinding = new FieldParameterBinding
                    {
                        NodeId = osc.Id, ParamIndex = 1, ExpectedKind = FieldBoundParamKind.Float
                    }
                }
            }
        };

        var def = new FieldGraphDefinition
        {
            Role = FieldGraphRole.Instrument,
            DisplayName = "Test Lead",
            Category = "User Instruments",
            Author = "tester",
            Surface = surface
        };

        using var ms = new MemoryStream();
        FieldDefinitionFile.Save(def, graph, "tester", ms);
        ms.Position = 0;

        var loaded = FieldDefinitionFile.Load(ms, nodes);
        Assert.NotNull(loaded);
        Assert.Equal(def.DefinitionId, loaded!.Definition.DefinitionId);
        Assert.Equal("Test Lead", loaded.Definition.DisplayName);
        Assert.Equal(FieldGraphRole.Instrument, loaded.Definition.Role);
        Assert.Single(loaded.Definition.Surface.ExposedControls);
        Assert.Equal("Tune", loaded.Definition.Surface.ExposedControls[0].DisplayName);
        Assert.Contains(loaded.Graph.Nodes, n => n is WaveOscNode);
    }

    [Fact]
    public void ExposedParameterProxiesUpdateNodeValues()
    {
        var (nodes, _, _) = Registries();
        var graph = MakeInstrumentGraph(nodes);
        var osc = graph.Nodes.OfType<WaveOscNode>().First();
        var coarseIndex = -1;
        for (var i = 0; i < osc.Parameters.Count; i++)
            if (osc.Parameters[i] is FloatParameter { Name: "Coarse" }) { coarseIndex = i; break; }
        Assert.True(coarseIndex >= 0);

        var surface = new FieldSurfaceDefinition
        {
            ExposedControls =
            {
                new FieldExposedControl
                {
                    NodeId = osc.Id,
                    ParamIndex = coarseIndex,
                    ExpectedKind = FieldBoundParamKind.Float,
                    DisplayName = "Coarse"
                }
            }
        };

        var host = new FieldInstrument(nodes, buildDefault: false);
        host.ApplyDefinition(new FieldGraphDefinition
        {
            Role = FieldGraphRole.Instrument,
            DisplayName = "Macro Host",
            Surface = surface
        }, graph);

        Assert.Single(host.Parameters);
        var proxy = Assert.IsType<FloatParameter>(host.Parameters[0]);
        proxy.Value = 12;
        var liveOsc = host.Graph.Nodes.OfType<WaveOscNode>().First();
        Assert.Equal(12, ((FloatParameter)liveOsc.Parameters[coarseIndex]).Value);
    }

    [Fact]
    public void UnresolvedExposedControlIsInertPlaceholder()
    {
        var (nodes, _, _) = Registries();
        var graph = MakeInstrumentGraph(nodes);
        var surface = new FieldSurfaceDefinition
        {
            ExposedControls =
            {
                new FieldExposedControl
                {
                    NodeId = Guid.NewGuid(),
                    ParamIndex = 0,
                    ExpectedKind = FieldBoundParamKind.Float,
                    DisplayName = "Missing"
                }
            }
        };

        var paramsList = FieldExposedParameters.Build(graph, surface.ExposedControls);
        Assert.Single(paramsList);
        var f = Assert.IsType<FloatParameter>(paramsList[0]);
        f.Value = 0.5; // should not throw
        Assert.Equal(0.5, f.Value);
    }

    [Fact]
    public void CloneIsolatesSurfaceAndGraph()
    {
        var (nodes, _, _) = Registries();
        var graph = MakeInstrumentGraph(nodes);
        var osc = graph.Nodes.OfType<WaveOscNode>().First();
        var host = new FieldInstrument(nodes, buildDefault: false);
        host.ApplyDefinition(new FieldGraphDefinition
        {
            Role = FieldGraphRole.Instrument,
            DisplayName = "Clone Me",
            Surface = new FieldSurfaceDefinition
            {
                Widgets = { new FieldWidget { Kind = FieldWidgetKind.Text, Label = "A" } },
                ExposedControls =
                {
                    new FieldExposedControl
                    {
                        NodeId = osc.Id, ParamIndex = 1, ExpectedKind = FieldBoundParamKind.Float,
                        DisplayName = "C"
                    }
                }
            }
        }, graph);

        var clone = (FieldInstrument)host.Clone();
        Assert.Equal(host.TypeId, clone.TypeId);
        Assert.Equal(host.Name, clone.Name);
        Assert.True(clone.HasCustomSurface);
        clone.Surface.Widgets[0].Label = "B";
        Assert.Equal("A", host.Surface.Widgets[0].Label);
    }

    [Fact]
    public void LegacyInstrumentStateStillLoads()
    {
        var (nodes, _, _) = Registries();
        var legacy = new FieldInstrument(nodes);
        using var ms = new MemoryStream();
        using (var w = new OngenWriter(ms)) FieldGraphSerializer.Write(w, legacy.Graph);
        ms.Position = 0;

        var host = new FieldInstrument(nodes, buildDefault: false);
        using (var r = new OngenReader(ms)) host.ReadProjectState(r);
        Assert.Equal(FieldInstrument.Id, host.TypeId);
        Assert.False(host.HasCustomSurface);
        Assert.NotEmpty(host.Graph.Nodes);
    }

    [Fact]
    public void UserTypeFallbackCreatesShellForMissingLibraryDefinition()
    {
        var (nodes, inst, _) = Registries();
        var typeId = FieldGraphDefinition.InstrumentTypePrefix + Guid.NewGuid().ToString("N");
        var created = inst.Create(typeId);
        var field = Assert.IsType<FieldInstrument>(created);
        Assert.Equal(typeId, field.TypeId);
    }

    [Fact]
    public void ProjectStateRoundTripPreservesUserIdentity()
    {
        var (nodes, inst, fx) = Registries();
        var graph = MakeInstrumentGraph(nodes);
        var defId = Guid.NewGuid();
        var def = new FieldGraphDefinition
        {
            DefinitionId = defId,
            Role = FieldGraphRole.Instrument,
            DisplayName = "Snapshot Lead",
            Surface = new FieldSurfaceDefinition
            {
                Widgets = { new FieldWidget { Kind = FieldWidgetKind.Panel, Label = "Panel" } }
            }
        };

        var host = new FieldInstrument(nodes, buildDefault: false);
        host.ApplyDefinition(def, graph);
        Assert.StartsWith(FieldGraphDefinition.InstrumentTypePrefix, host.TypeId);

        using var ms = new MemoryStream();
        using (var w = new OngenWriter(ms)) host.WriteProjectState(w);
        ms.Position = 0;

        // Load via fallback even if not registered as a library entry.
        var loaded = (FieldInstrument)inst.Create(host.TypeId);
        using (var r = new OngenReader(ms)) loaded.ReadProjectState(r);
        Assert.Equal("Snapshot Lead", loaded.Name);
        Assert.Equal(defId, loaded.DefinitionId);
        Assert.True(loaded.HasCustomSurface);
        Assert.Equal(host.TypeId, loaded.TypeId);
    }

    [Fact]
    public void EffectPresetRoundTripsWithSurface()
    {
        var (nodes, inst, fx) = Registries();
        var graph = MakeEffectGraph();
        var host = new FieldEffect(nodes, buildDefault: false);
        host.ApplyDefinition(new FieldGraphDefinition
        {
            Role = FieldGraphRole.Effect,
            DisplayName = "My FX",
            Surface = new FieldSurfaceDefinition
            {
                Widgets = { new FieldWidget { Kind = FieldWidgetKind.Text, Label = "FX" } }
            }
        }, graph);

        using var ms = new MemoryStream();
        PresetFile.SaveEffect(host, "Bright", "tester", ms);
        ms.Position = 0;

        var result = PresetFile.Load(ms, inst, fx);
        Assert.NotNull(result);
        var loaded = Assert.IsType<FieldEffect>(result!.Effect);
        Assert.Equal("My FX", loaded.Name);
        Assert.True(loaded.HasCustomSurface);
        Assert.StartsWith(FieldGraphDefinition.EffectTypePrefix, loaded.TypeId);
    }

    [Fact]
    public void ValidationRequiresAudioIo()
    {
        var empty = new FieldGraph();
        var failInst = FieldDefinitionValidation.Validate(empty, FieldGraphRole.Instrument, new FieldSurfaceDefinition());
        Assert.False(failInst.Ok);

        var effectOk = FieldDefinitionValidation.Validate(MakeEffectGraph(), FieldGraphRole.Effect,
            new FieldSurfaceDefinition());
        Assert.True(effectOk.Ok);

        var side = MakeEffectGraph();
        side.AddNode(new SidechainInNode { X = 40, Y = 80 });
        var warn = FieldDefinitionValidation.Validate(side, FieldGraphRole.Effect, new FieldSurfaceDefinition());
        Assert.True(warn.Ok);
        Assert.NotEmpty(warn.Warnings);
    }

    [Fact]
    public void InstrumentPresetRoundTripsUserType()
    {
        var (nodes, inst, fx) = Registries();
        var graph = MakeInstrumentGraph(nodes);
        var host = new FieldInstrument(nodes, buildDefault: false);
        host.ApplyDefinition(new FieldGraphDefinition
        {
            Role = FieldGraphRole.Instrument,
            DisplayName = "User Pad",
            Surface = new FieldSurfaceDefinition()
        }, graph);
        host.Prepare(Fmt);

        using var ms = new MemoryStream();
        PresetFile.SaveInstrument(host, "Warm", "tester", ms);
        ms.Position = 0;
        var result = PresetFile.Load(ms, inst, fx);
        Assert.NotNull(result);
        var loaded = Assert.IsType<FieldInstrument>(result!.Instrument);
        Assert.Equal(host.TypeId, loaded.TypeId);
        Assert.Equal("User Pad", loaded.Name);
    }
}
