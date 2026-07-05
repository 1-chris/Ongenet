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
/// (for note-driven effects), and a source track for sidechain/vocoder-style patches.
/// </summary>
public sealed class FieldEffect : IAudioEffect, IContextualEffect, IMidiAwareEffect, ISourceTrackEffect, IProjectStatefulComponent
{
    public const string Id = "field";
    private const int InitialMaxBlock = 2048;

    private readonly IFieldNodeRegistry _registry;
    private readonly FieldGraph _graph = new();
    private readonly object _compileLock = new();
    private readonly MidiEventFifo _fifo = new();
    private readonly List<MidiMessage> _midi = new();

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

    public string Name => "Field";
    public string TypeId => Id;
    public bool Enabled { get; set; } = true;
    public IReadOnlyList<Parameter> Parameters => Array.Empty<Parameter>();
    public Guid? SourceTrackId { get; set; }

    public FieldGraph Graph => _graph;
    public CompiledGraph? Compiled => _compiled;

    public void SetContext(EffectContext context) => _ctx = context;

    /// <summary>The built-in effect decomposition patches (EQ, Filter, Compressor, ...).</summary>
    public static System.Collections.Generic.IReadOnlyList<string> BuiltInPatchNames => Patches.FieldBuiltInPatches.EffectPatchNames;

    /// <summary>Replaces the graph with the built-in decomposition patch at <paramref name="index"/>.</summary>
    public void LoadBuiltInPatch(int index)
    {
        Patches.FieldBuiltInPatches.BuildEffect(index, _graph, _registry);
        Recompile(_maxBlock);
    }

    public void Prepare(AudioFormat format)
    {
        _format = format;
        Recompile(_maxBlock);
    }

    public void Recompile() => Recompile(_maxBlock);

    private void Recompile(int minBlock)
    {
        lock (_compileLock)
        {
            if (minBlock > _maxBlock) _maxBlock = minBlock;
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
        if (compiled is null) return; // prepared by the engine before the first block

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

        // Process in chunks no larger than the compiled block size (no audio-thread recompilation).
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
        using (var w = new OngenWriter(ms)) FieldGraphSerializer.Write(w, _graph);
        ms.Position = 0;
        using (var r = new OngenReader(ms)) FieldGraphSerializer.Read(r, copy._graph, _registry);
        return copy;
    }

    public void WriteProjectState(OngenWriter writer)
    {
        writer.WriteNullableGuid(SourceTrackId);
        FieldGraphSerializer.Write(writer, _graph);
    }

    public void ReadProjectState(OngenReader reader)
    {
        SourceTrackId = reader.ReadNullableGuid();
        FieldGraphSerializer.Read(reader, _graph, _registry);
        Recompile(_maxBlock);
    }
}
