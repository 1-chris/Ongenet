using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// Base class for every component in a Field graph. A node declares its inlets/outlets and its
/// editable <see cref="Parameter"/>s in its constructor (via <see cref="AddInput"/>, <see cref="AddOutput"/>
/// and <see cref="AddParam"/>), then calls <see cref="Build"/>. Every <see cref="FloatParameter"/> that is
/// added as modulatable automatically gains a hidden CV modulation inlet, so any knob can be driven by any
/// generator with no per-node wiring code. The node's DSP runs in <see cref="ProcessBlock"/>, reading and
/// writing block buffers through the supplied <see cref="FieldRenderContext"/>.
/// </summary>
public abstract class FieldNode
{
    private readonly List<FieldPort> _inputs = new();
    private readonly List<FieldPort> _outputs = new();
    private readonly List<Parameter> _parameters = new();
    private int[] _modPortForParam = Array.Empty<int>();

    /// <summary>Stable registry type id (e.g. "osc.wave"). Recreated by <c>FieldNodeRegistry</c> on load.</summary>
    public abstract string TypeId { get; }

    /// <summary>Display title shown in the node header and the component palette.</summary>
    public abstract string DisplayName { get; }

    /// <summary>Palette grouping (e.g. "Oscillators", "Filters", "Modulators").</summary>
    public virtual string Category => "Misc";

    /// <summary>Instance id within a graph. Assigned by the graph; stable across save/load.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Canvas position (persisted with the graph, ignored by the audio engine).</summary>
    public double X { get; set; }

    /// <summary>Canvas position (persisted with the graph, ignored by the audio engine).</summary>
    public double Y { get; set; }

    /// <summary>User-set canvas width; 0 = use the default. Cosmetic (persisted, ignored by audio).</summary>
    public double Width { get; set; }

    /// <summary>User-set extra height for the node's visual area; 0 = default. Cosmetic (persisted).</summary>
    public double VisualHeight { get; set; }

    /// <summary>
    /// True for nodes that show a live visualization on the graph (scope, wavetable). The editor reserves a
    /// visual area and hosts the appropriate GPU visual over it.
    /// </summary>
    public virtual bool HasVisual => false;

    public IReadOnlyList<FieldPort> Inputs => _inputs;
    public IReadOnlyList<FieldPort> Outputs => _outputs;
    public IReadOnlyList<Parameter> Parameters => _parameters;

    /// <summary>The engine format, set by <see cref="Prepare"/>.</summary>
    protected AudioFormat Format { get; private set; } = AudioFormat.Default;

    /// <summary>Maximum block length the node must support, set by <see cref="Prepare"/>.</summary>
    protected int MaxBlock { get; private set; }

    /// <summary>Number of voice slots to allocate per-voice state for, set by <see cref="Prepare"/>.</summary>
    protected int VoiceCount { get; private set; } = 1;

    /// <summary>
    /// True when this node must run once per active voice (it holds per-note state such as an oscillator
    /// phase or an envelope). False for global nodes (reverb, master utility). Note-source nodes and the
    /// primitive DSP nodes are per-voice; the compiler propagates this across the graph but a node can
    /// force itself global by overriding <see cref="ForceGlobal"/>.
    /// </summary>
    public bool PerVoice { get; internal set; }

    /// <summary>Nodes that must always run once globally regardless of their inputs (e.g. reverb tails).</summary>
    public virtual bool ForceGlobal => false;

    /// <summary>Nodes that introduce a note dependency even with no note inlet (Pitch/Gate/Velocity/MIDI In).</summary>
    public virtual bool IsNoteSource => false;

    protected void AddInput(string id, string displayName, FieldSignalKind kind = FieldSignalKind.Audio)
        => _inputs.Add(new FieldPort(id, displayName, kind, FieldPortDirection.Input));

    protected void AddOutput(string id, string displayName, FieldSignalKind kind = FieldSignalKind.Audio)
        => _outputs.Add(new FieldPort(id, displayName, kind, FieldPortDirection.Output));

    /// <summary>Registers a parameter. Float parameters gain a modulation inlet unless <paramref name="modulatable"/> is false.</summary>
    protected void AddParam(Parameter parameter, bool modulatable = true)
    {
        _parameters.Add(parameter);
        _pendingModulatable.Add(parameter is FloatParameter && modulatable);
    }

    private readonly List<bool> _pendingModulatable = new();

    /// <summary>
    /// Finalises the node: appends one CV modulation inlet per modulatable float parameter and builds the
    /// parameter → modulation-port lookup. Call at the end of the constructor after all ports/params are added.
    /// </summary>
    protected void Build()
    {
        _modPortForParam = new int[_parameters.Count];
        for (var i = 0; i < _modPortForParam.Length; i++) _modPortForParam[i] = -1;

        for (var i = 0; i < _parameters.Count; i++)
        {
            if (!_pendingModulatable[i]) continue;
            _modPortForParam[i] = _inputs.Count;
            _inputs.Add(new FieldPort($"mod:{i}", $"{_parameters[i].Name} (mod)", FieldSignalKind.Cv,
                FieldPortDirection.Input) { IsModulation = true, ModParamIndex = i });
        }
    }

    /// <summary>The input-port index of parameter <paramref name="paramIndex"/>'s modulation inlet, or -1 if none.</summary>
    public int ModPortForParam(int paramIndex)
        => paramIndex >= 0 && paramIndex < _modPortForParam.Length ? _modPortForParam[paramIndex] : -1;

    private int _preparedRate = -1;
    private int _preparedChannels = -1;
    private int _preparedBlock = -1;
    private int _preparedVoices = -1;

    /// <summary>
    /// True when the node has already been prepared for exactly this format/block/voice configuration.
    /// The compiler uses this to skip re-preparing (and thus reallocating) existing nodes on a purely
    /// structural recompile — vital because a node instance is shared with the previously compiled graph
    /// that the audio thread may still be running.
    /// </summary>
    public bool IsPreparedFor(AudioFormat format, int maxBlock, int voiceCount)
        => _preparedRate == format.SampleRate && _preparedChannels == format.Channels
           && _preparedBlock == maxBlock && _preparedVoices == (voiceCount < 1 ? 1 : voiceCount);

    /// <summary>Called before processing and whenever the format, block size or voice count changes.</summary>
    public virtual void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        Format = format;
        MaxBlock = maxBlock;
        VoiceCount = voiceCount < 1 ? 1 : voiceCount;
        _preparedRate = format.SampleRate;
        _preparedChannels = format.Channels;
        _preparedBlock = maxBlock;
        _preparedVoices = VoiceCount;
    }

    /// <summary>Processes one block for the current <see cref="FieldRenderContext.Voice"/>.</summary>
    public abstract void ProcessBlock(FieldRenderContext ctx);

    /// <summary>Resets per-voice DSP state when voice <paramref name="voice"/> is (re)started.</summary>
    public virtual void ResetVoice(int voice) { }

    /// <summary>
    /// Reads parameter <paramref name="paramIndex"/>'s value at sample <paramref name="sample"/>, adding any
    /// connected modulation. Modulation is additive in value units scaled by the parameter range, so a unit
    /// CV sweeps the full range; the result is clamped to the parameter's bounds.
    /// </summary>
    protected double ModValue(FieldRenderContext ctx, int paramIndex, double baseValue, int sample)
    {
        var mod = ctx.ModInput(paramIndex);
        if (mod is null) return baseValue;
        if (Parameters[paramIndex] is not FloatParameter p) return baseValue;
        var v = baseValue + mod[sample] * (p.Max - p.Min);
        return v < p.Min ? p.Min : v > p.Max ? p.Max : v;
    }

    /// <summary>True when parameter <paramref name="paramIndex"/> has a live modulation signal this block.</summary>
    protected static bool IsModulated(FieldRenderContext ctx, int paramIndex) => ctx.ModInput(paramIndex) is not null;
}
