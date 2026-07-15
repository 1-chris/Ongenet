using System;
using System.Collections.Generic;
using System.IO;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field.Patches;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Persistence;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// The Field modular effect: the same graph engine as <see cref="FieldInstrument"/>, but fed the track's
/// incoming audio (via an Audio In node) and processed in place. Supports tempo/sidechain context, MIDI
/// (for note-driven effects), and a source track for sidechain/vocoder-style patches. User definitions
/// may expose a custom control surface and top-level parameters.
/// </summary>
public sealed class FieldEffect : IAudioEffect, IContextualEffect, IMidiAwareEffect, ISourceTrackEffect, IProjectStatefulComponent
{
    public const string Id = "field";
    private const int InitialMaxBlock = 512;

    private readonly IFieldNodeRegistry _registry;
    private readonly FieldGraph _graph = new();
    private readonly object _compileLock = new();
    private readonly MidiEventFifo _fifo = new();
    private readonly List<MidiMessage> _midi = new();

    private string _typeId = Id;
    private string _displayName = "Field";
    private Guid? _definitionId;
    private FieldSurfaceDefinition _surface = new();
    private List<Parameter> _parameters = new();

    private volatile CompiledGraph? _compiled;
    private volatile int _compiledRevision = -1;
    private AudioFormat _format = AudioFormat.Default;
    private int _maxBlock = InitialMaxBlock;
    private EffectContext? _ctx;

    public FieldEffect(IFieldNodeRegistry registry, bool buildDefault = true)
    {
        _registry = registry;
        if (buildDefault) FieldPatches.BuildBeginnerEffect(_graph);
    }

    /// <summary>Creates an empty host with the given type id for project/preset fallback loading.</summary>
    public static FieldEffect CreateShell(IFieldNodeRegistry registry, string typeId, string? displayName = null)
    {
        var fx = new FieldEffect(registry, buildDefault: false);
        fx._typeId = typeId;
        fx._displayName = string.IsNullOrWhiteSpace(displayName) ? "Field Effect" : displayName!;
        fx._definitionId = FieldGraphDefinition.TryParseDefinitionId(typeId);
        return fx;
    }

    public string Name => _displayName;
    public string TypeId => _typeId;
    public bool Enabled { get; set; } = true;
    public IReadOnlyList<Parameter> Parameters => _parameters;
    public Guid? SourceTrackId { get; set; }
    public Guid? DefinitionId => _definitionId;
    public FieldSurfaceDefinition Surface => _surface;
    public bool HasCustomSurface => _surface.Widgets.Count > 0;
    public bool IsUserDefinition => FieldGraphDefinition.IsUserEffectType(_typeId);

    public FieldGraph Graph => _graph;
    public CompiledGraph? Compiled => _compiled;
    public IFieldNodeRegistry Registry => _registry;

    public void SetContext(EffectContext context) => _ctx = context;

    public static IReadOnlyList<string> BuiltInPatchNames => FieldBuiltInPatches.EffectPatchNames;

    public void LoadBuiltInPatch(int index)
    {
        if (IsUserDefinition) return;
        FieldBuiltInPatches.BuildEffect(index, _graph, _registry);
        _surface = FieldBuiltInSurfaces.BuildEffect(index, _graph);
        RebuildExposedParameters();
        Recompile(_maxBlock);
    }

    public void ApplyDefinition(FieldGraphDefinition definition, FieldGraph graph)
    {
        AdoptLibraryIdentity(definition);
        CopyGraph(graph, _graph);
        RebuildExposedParameters();
        Recompile(_maxBlock);
    }

    /// <summary>Marks this live host as a saved library definition without replacing the graph.</summary>
    public void AdoptLibraryIdentity(FieldGraphDefinition definition)
    {
        _typeId = definition.TypeId;
        _displayName = definition.DisplayName;
        _definitionId = definition.DefinitionId;
        _surface = definition.Surface.Clone();
        RebuildExposedParameters();
    }

    public void SetSurface(FieldSurfaceDefinition surface)
    {
        _surface = surface.Clone();
        RebuildExposedParameters();
    }

    public void SetDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        _displayName = name.Trim();
    }

    public void RebuildExposedParameters()
        => _parameters = FieldExposedParameters.Build(_graph, _surface.ExposedControls);

    public void Prepare(AudioFormat format)
    {
        if (_compiled is not null
            && format.SampleRate == _format.SampleRate
            && format.Channels == _format.Channels
            && _compiledRevision == _graph.Revision
            && _compiled.MaxBlock >= _maxBlock)
        {
            _format = format;
            return;
        }

        _format = format;
        Recompile(_maxBlock);
    }

    public void Recompile() => Recompile(_maxBlock);

    private void Recompile(int minBlock)
    {
        lock (_compileLock)
        {
            if (minBlock > _maxBlock) _maxBlock = minBlock;
            if (_compiled is not null
                && _compiledRevision == _graph.Revision
                && _compiled.MaxBlock >= _maxBlock
                && _compiled.Format.SampleRate == _format.SampleRate
                && _compiled.Format.Channels == _format.Channels)
                return;

            var compiled = FieldGraphCompiler.Compile(_graph, _format, _maxBlock, isInstrument: false);
            _compiledRevision = _graph.Revision;
            _compiled = compiled;
        }
    }

    public void HandleMidi(in MidiMessage message) => _fifo.Push(message);
    public void AllNotesOff() => _fifo.Push(new MidiMessage(MidiMessageKind.ControlChange, 0, 123, 0));

    public void Process(Span<float> buffer)
    {
        var compiled = _compiled;
        if (compiled is null) return;

        _fifo.Drain(_midi);
        foreach (var m in _midi)
        {
            switch (m.Kind)
            {
                case MidiMessageKind.NoteOn when m.Data2 > 0: compiled.NoteOn(m.Note, m.Velocity); break;
                case MidiMessageKind.NoteOn: compiled.NoteOff(m.Note); break;
                case MidiMessageKind.NoteOff: compiled.NoteOff(m.Note); break;
                case MidiMessageKind.PitchBend: compiled.PitchBend((m.PitchBend14 - 8192) / 8192.0 * 2.0); break;
                case MidiMessageKind.ControlChange when m.Controller == 123: compiled.AllNotesOff(); break;
                case MidiMessageKind.ControlChange: compiled.ControlChange(m.Controller, m.Data2); break;
            }
        }

        var bpm = _ctx?.Bpm ?? 120.0;
        var playhead = _ctx?.PlayheadBeats ?? 0.0;
        var playing = _ctx?.Playing ?? false;

        ReadOnlySpan<float> sidechain = ReadOnlySpan<float>.Empty;
        var scChannels = 0;
        if (_ctx is { } ctx && SourceTrackId is { } srcId)
        {
            ctx.Sidechain.Request(srcId);
            sidechain = ctx.Sidechain.Read(srcId, out scChannels);
        }

        var channels = _format.Channels < 1 ? 1 : _format.Channels;
        var frames = buffer.Length / channels;
        var max = compiled.MaxBlock;
        var offset = 0;
        while (offset < frames)
        {
            var n = Math.Min(max, frames - offset);
            compiled.Process(buffer.Slice(offset * channels, n * channels), bpm, playhead, playing, sidechain, scChannels);
            offset += n;
        }
    }

    public IAudioEffect Clone()
    {
        var copy = new FieldEffect(_registry, buildDefault: false) { Enabled = Enabled, SourceTrackId = SourceTrackId };
        using var ms = new MemoryStream();
        using (var w = new OngenWriter(ms)) WriteProjectState(w);
        ms.Position = 0;
        using (var r = new OngenReader(ms)) copy.ReadProjectState(r);
        return copy;
    }

    public void WriteProjectState(OngenWriter writer)
        => FieldHostState.WriteEffect(writer, SourceTrackId, _typeId, _displayName, _definitionId, _surface, _graph);

    public void ReadProjectState(OngenReader reader)
    {
        FieldHostState.ReadEffect(reader, out var sourceTrackId, out var typeId, out var displayName,
            out var definitionId, out var surface, _graph, _registry);
        SourceTrackId = sourceTrackId;
        _typeId = string.IsNullOrEmpty(typeId) ? Id : typeId;
        _displayName = string.IsNullOrEmpty(displayName) ? "Field" : displayName;
        _definitionId = definitionId;
        _surface = surface;
        RebuildExposedParameters();
        // Defer compilation until Prepare — undo snapshots must not retain DSP buffers.
        _compiled = null;
        _compiledRevision = -1;
    }

    private void CopyGraph(FieldGraph source, FieldGraph dest)
    {
        using var ms = new MemoryStream();
        using (var w = new OngenWriter(ms)) FieldGraphSerializer.Write(w, source);
        ms.Position = 0;
        using (var r = new OngenReader(ms)) FieldGraphSerializer.Read(r, dest, _registry);
    }
}
