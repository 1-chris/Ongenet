using System;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Field.Patches;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Core.Tests.Field;

/// <summary>
/// Exercises the built-in decomposition patches and the module-wrapper nodes: every instrument and effect
/// patch must compile and process a block to a finite result. This also proves the "field" instrument/effect
/// and one module node per built-in are registered by <see cref="FieldBootstrap"/>.
/// </summary>
public class FieldPatchTests
{
    private static readonly AudioFormat Fmt = new(44100, 2);

    private static (IFieldNodeRegistry nodes, IInstrumentRegistry inst, IEffectRegistry fx) MakeRegistries()
    {
        var nodes = new FieldNodeRegistry();
        var inst = new InstrumentRegistry();
        var fx = new EffectRegistry();
        FieldBootstrap.Initialize(nodes, inst, fx);
        return (nodes, inst, fx);
    }

    [Fact]
    public void FieldIsRegisteredInBothRegistries()
    {
        var (_, inst, fx) = MakeRegistries();
        Assert.Contains(inst.Available, i => i.Id == FieldInstrument.Id);
        Assert.Contains(fx.Available, e => e.Id == FieldEffect.Id);
    }

    [Fact]
    public void ModuleWrapperNodesRegisteredForBuiltIns()
    {
        var (nodes, _, _) = MakeRegistries();
        // A built-in instrument and effect should each have a module-wrapper node.
        Assert.NotNull(nodes.TryCreate("module.inst.oscillator"));
        Assert.NotNull(nodes.TryCreate("module.fx.reverb"));
    }

    [Fact]
    public void EveryInstrumentPatchCompilesAndRenders()
    {
        var (nodes, _, _) = MakeRegistries();
        var inst = new FieldInstrument(nodes);
        inst.Prepare(Fmt);
        var buffer = new float[256 * 2];

        for (var i = 0; i < FieldBuiltInPatches.InstrumentPatchNames.Count; i++)
        {
            inst.LoadPreset(i);
            Assert.True(inst.HasCustomSurface,
                $"instrument patch '{FieldBuiltInPatches.InstrumentPatchNames[i]}' has no editable surface");
            Assert.NotEmpty(inst.Surface.Widgets);
            Assert.NotEmpty(inst.Surface.ExposedControls);
            foreach (var exposed in inst.Surface.ExposedControls)
            {
                var binding = new FieldParameterBinding
                {
                    NodeId = exposed.NodeId,
                    ParamIndex = exposed.ParamIndex,
                    ExpectedKind = exposed.ExpectedKind
                };
                Assert.True(FieldExposedParameters.TryResolve(inst.Graph, binding, out _),
                    $"unresolved control '{exposed.DisplayName}' in '{FieldBuiltInPatches.InstrumentPatchNames[i]}'");
            }
            inst.NoteOn(60, 1.0f);
            for (var b = 0; b < 3; b++)
            {
                Array.Clear(buffer);
                inst.Render(buffer);
            }

            inst.NoteOff(60);
            foreach (var s in buffer)
                Assert.True(float.IsFinite(s), $"non-finite output from instrument patch '{FieldBuiltInPatches.InstrumentPatchNames[i]}'");
        }
    }

    [Fact]
    public void EditingGraphWhileRunningKeepsExistingNodesPreparedAndFinite()
    {
        // Reproduces the crash path: a reverb module node is already prepared; adding a node and recompiling
        // must NOT re-prepare (reallocate) the reverb, and rendering must stay finite.
        var (nodes, _, _) = MakeRegistries();
        var inst = new FieldInstrument(nodes, buildDefault: false);

        var note = nodes.Create(Ongenet.Core.Audio.Field.Nodes.NoteInNode.Type);
        var osc = nodes.Create(Ongenet.Core.Audio.Field.Nodes.WaveOscNode.Type);
        var reverb = nodes.Create("module.fx.reverb");
        var outN = nodes.Create(Ongenet.Core.Audio.Field.Nodes.AudioOutNode.Type);
        inst.Graph.AddNode(note);
        inst.Graph.AddNode(osc);
        inst.Graph.AddNode(reverb);
        inst.Graph.AddNode(outN);
        inst.Graph.Connect(note.Id, "pitch", osc.Id, "pitch");
        inst.Graph.Connect(osc.Id, "out", reverb.Id, "l");
        inst.Graph.Connect(osc.Id, "out", reverb.Id, "r");
        inst.Graph.Connect(reverb.Id, "l", outN.Id, "l");
        inst.Graph.Connect(reverb.Id, "r", outN.Id, "r");

        inst.Prepare(Fmt);
        Assert.True(reverb.IsPreparedFor(Fmt, 2048, FieldGraphCompiler.DefaultMaxVoices));

        inst.NoteOn(60, 1.0f);
        var buffer = new float[512 * 2];
        for (var b = 0; b < 3; b++) { Array.Clear(buffer); inst.Render(buffer); }

        // Hook up a scope inline (like the user did) and recompile mid-session.
        var scope = nodes.Create(Ongenet.Core.Audio.Field.Nodes.ScopeNode.Type);
        inst.Graph.AddNode(scope);
        inst.Recompile();

        for (var b = 0; b < 5; b++)
        {
            Array.Clear(buffer);
            inst.Render(buffer);
            foreach (var s in buffer) Assert.True(float.IsFinite(s));
        }
    }

    [Fact]
    public void EveryEffectPatchCompilesAndProcesses()
    {
        var (nodes, _, _) = MakeRegistries();
        var fx = new FieldEffect(nodes);
        fx.Prepare(Fmt);

        for (var i = 0; i < FieldEffect.BuiltInPatchNames.Count; i++)
        {
            fx.LoadBuiltInPatch(i);
            Assert.True(fx.HasCustomSurface,
                $"effect patch '{FieldEffect.BuiltInPatchNames[i]}' has no editable surface");
            Assert.NotEmpty(fx.Surface.Widgets);
            foreach (var exposed in fx.Surface.ExposedControls)
            {
                var binding = new FieldParameterBinding
                {
                    NodeId = exposed.NodeId,
                    ParamIndex = exposed.ParamIndex,
                    ExpectedKind = exposed.ExpectedKind
                };
                Assert.True(FieldExposedParameters.TryResolve(fx.Graph, binding, out _),
                    $"unresolved control '{exposed.DisplayName}' in '{FieldEffect.BuiltInPatchNames[i]}'");
            }
            var buffer = new float[256 * 2];
            for (var n = 0; n < buffer.Length; n++) buffer[n] = 0.3f * MathF.Sin(n * 0.05f);
            fx.Process(buffer);
            foreach (var s in buffer)
                Assert.True(float.IsFinite(s), $"non-finite output from effect patch '{FieldEffect.BuiltInPatchNames[i]}'");
        }
    }
}
