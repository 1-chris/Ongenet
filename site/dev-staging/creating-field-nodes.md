# The Field modular system & creating nodes

**Field** is Ongenet's modular node-graph instrument and effect — a patchable "grid" where you wire small components together to build synths, effects and modulation from
scratch. It ships as both an **instrument** (`TypeId "field"`, appears in the Instruments list) and an
**effect** (appears in the add-effect menu under *Field*), sharing one engine.

This guide explains how Field works and how to add your own node type.

---

## 1. How Field runs

Field is one shared engine surfaced two ways:

- **`FieldInstrument`** — an `IInstrument`. Its output is *added* into the mix (polyphonic).
- **`FieldEffect`** — an `IAudioEffect`, `IContextualEffect`, `IMidiAwareEffect`, `ISourceTrackEffect`. It
  reads the track's incoming audio (an **Audio In** node) and processes it in place.

Both host an editable [`FieldGraph`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Field/FieldGraph.cs) and run an immutable
[`CompiledGraph`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Field/CompiledGraph.cs) snapshot produced by the
[`FieldGraphCompiler`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Field/FieldGraphCompiler.cs). Editing the graph recompiles off
the audio path and swaps the snapshot in via a `volatile` reference, so it is safe to edit while playing.

### Signals

Most wires are a **mono block of `float`** — audio and control/modulation share the same representation
(exactly like Grid), so any output can patch into any input. The `FieldSignalKind` (Audio / Cv / Note) drives
wire colour. Stereo is explicit: use a **Pan** node (mono → L/R) or the **Voice Sum** node's L/R.

**Asset ports.** A fourth kind, `Asset`, carries a *resource* (a loaded soundfont, a wavetable) rather than
a per-sample buffer. Asset wires allocate no audio buffers and never run on the audio thread: the compiler
resolves them once by pushing the provider's asset into the consumer
(`IFieldAssetProvider.GetAsset` → `IFieldAssetConsumer.SetAsset`). This is how the **SoundFont** node feeds
the **Sampler** node. Add new port kinds here as new resource types appear.

### Modulation of any knob

Every `FloatParameter` on a node automatically gains a hidden **CV modulation inlet**. Patch any generator
(LFO, Drift, Random, Macro, an envelope, or another node's output) into it and the node reads
`base + Σ(CV) × range` per sample. Generators modulating generators is just CV → CV — no special case.

### Polyphony (instrument)

The compiler partitions the graph into a **per-voice region** (nodes reachable from note sources —
oscillators, envelopes, per-voice filters) and a **global region** (post-mix FX). Per-voice nodes run once
per active voice; global nodes run once and *sum* their per-voice inputs — that summation is the
voice-collecting boundary (the **Audio Out** and **Voice Sum** nodes are always global). Voices are
allocated/stolen by [`FieldVoiceManager`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Field/FieldVoiceManager.cs) and freed a few
blocks after they fall silent.

Feedback loops are allowed: the compiler drops the back-edge from the processing order, so it reads the
previous block's buffer (a one-block delay).

---

## 2. The node library

Built-in node types are registered in
[`FieldNodeCatalog`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Field/FieldNodeCatalog.cs), grouped by category
(`FieldNodeCategories`): I/O, Oscillators, Envelopes, Filters, Modulators, Shapers, Dynamics, Delay & Space,
Sampler, Math, Logic, Modules. Almost every node wraps a primitive from
[`Ongenet.Core/Audio/Dsp`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Dsp).

**Module wrappers.** Every built-in instrument and effect — and every discovered CLAP/LV2/VST/AU plugin — is
also available as a single **module node** (`module.inst.<id>` / `module.fx.<id>`), registered at startup by
[`FieldModuleNodes`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Field/FieldModuleNodes.cs). So you can drop a whole Reverb, a
whole Padda, or a plugin into a graph and modulate its parameters.

**Built-in patches.** [`FieldBuiltInPatches`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Field/Patches/FieldBuiltInPatches.cs)
reconstructs every built-in instrument and effect as a Field graph — the tractable ones from primitive nodes,
the irreducible cores (grain scheduler, SFZ mapping, freeverb, pitch engines, Stuttero) via their faithful
module node. Pick them from the **Preset** dropdown in the Field editor.

---

## 3. The editor UI

- **Middle-drag** to pan, **scroll** to zoom (about the cursor).
- **Left-drag a node** to move it; click to select (its parameters show in the right inspector).
- **Drag the bottom-right handle** to resize a node (widen it, or enlarge its visualization area).
- **Drag from a port** to another port to connect; drop a wire off an input to disconnect it.
- **Right-click empty space** to add a node (categorised menu); **right-click a node** to delete/disconnect.
- **Delete/Backspace** removes the selected node.
- **Pop out** opens the full editor in its own resizable window (shares the same live graph).

**On-graph visualizations.** Nodes that expose a visual (`FieldNode.HasVisual`) render a live GPU
visualization directly on the node body: the **Scope** node shows the 3D waveform-trail oscilloscope, and the
**Wavetable Osc** node shows the morphing 3D table. These are real `Engine3DVisualHost` views positioned over
the canvas by the editor (`FieldNodeVisuals` maps a node to its `IEngine3DVisualization`), tracking pan/zoom
and node resize. Where no GPU is available they degrade to a placeholder, like the 3D Scope effect.

**Resource nodes.** The **SoundFont** node loads an `.sfz`/`.sf2` and outputs it on an asset port; the
**Sampler** node consumes that asset and plays it (its own polyphony). The **Wavetable Osc** node is a
self-contained wavetable source: pick a built-in table (Basic/Harmonics/Random) or **Load sample…** to slice
an audio file into a table — both rebuild the table (and its 3D view) live. Load buttons appear in the
inspector when a resource node is selected.

---

## 4. Writing a new node

Subclass [`FieldNode`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Field/FieldNode.cs). Declare ports and parameters in the
constructor, then call `Build()` (which appends the modulation inlets). Do per-voice DSP in `ProcessBlock`,
indexing per-voice state by `ctx.Voice`.

```csharp
public sealed class TremoloCvNode : FieldNode
{
    public const string Type = "example.tremolo";
    public override string TypeId => Type;
    public override string DisplayName => "Tremolo CV";
    public override string Category => FieldNodeCategories.Modulators;

    public double Rate { get; set; } = 5.0;
    private Lfo[] _lfo = System.Array.Empty<Lfo>();

    public TremoloCvNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Rate", 0.1, 20, () => Rate, v => Rate = v, "0.0", "Hz"));
        Build(); // appends a modulation inlet for "Rate"
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _lfo = new Lfo[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) _lfo[i] = new Lfo();
    }

    public override void ResetVoice(int voice) { /* reset per-voice state if needed */ }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var lfo = _lfo[ctx.Voice];
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++)
        {
            lfo.SetRate(ModValue(ctx, 0, Rate, i), Format.SampleRate); // reads Rate + any modulation
            outBuf[i] = (float)(lfo.Next() * 0.5 + 0.5);
        }
    }
}
```

Then register it in `FieldNodeCatalog.BuiltIns()`:

```csharp
yield return new FieldNodeInfo(TremoloCvNode.Type, "Tremolo CV",
    FieldNodeCategories.Modulators, () => new TremoloCvNode());
```

Rules of thumb:

- **Allocate in `Prepare`, never in `ProcessBlock`** — the audio thread must not allocate. Per-voice state is
  an array sized `VoiceCount`; index it by `ctx.Voice` (global nodes only ever use slot 0).
- **Read parameters with `ModValue(ctx, paramIndex, baseValue, sample)`** so knobs pick up modulation.
- Set `ForceGlobal => true` for nodes that must run once and sum voices (reverbs, master utilities).
- Set `IsNoteSource => true` for nodes that originate per-voice note data.
- Extra state that isn't a parameter (e.g. a loaded sample, a curve) is persisted by also implementing
  `IProjectStatefulComponent` / `ISampleHost`; the graph serializer captures it.

---

## 5. Persistence

The whole graph (nodes, parameters, connections, groups, canvas view, and any embedded samples) is written by
[`FieldGraphSerializer`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Persistence/FieldGraphSerializer.cs) as the Field component's
`IProjectStatefulComponent` blob, so Field instruments/effects save and reload inside `.ongen` projects and
`.ongenpreset` presets like any other component.
