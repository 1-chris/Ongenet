using System;
using System.Collections.Generic;
using System.IO;
using Ongenet.Core.Audio.Field.Patches;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// The Field modular instrument: hosts an editable <see cref="FieldGraph"/> and runs a compiled snapshot of
/// it as a polyphonic <see cref="IInstrument"/>. Note events are queued and applied on the audio thread; the
/// graph is (re)compiled off the audio path and swapped in via a volatile reference, so editing is safe
/// while playing. The whole graph is persisted through <see cref="IProjectStatefulComponent"/>.
/// </summary>
public sealed class FieldInstrument : IInstrument, IInstrumentVoiceState, IProjectStatefulComponent, IPresetProvider
{
    public const string Id = "field";
    private const int InitialMaxBlock = 2048;

    private enum EvType { On, Off, AllOff, Bend, Cc }
    private readonly struct NoteEvent
    {
        public readonly EvType Type;
        public readonly int Note;
        public readonly float Value;
        public readonly int Controller;
        public NoteEvent(EvType type, int note, float value, int controller = 0)
        {
            Type = type; Note = note; Value = value; Controller = controller;
        }
    }

    private readonly IFieldNodeRegistry _registry;
    private readonly FieldGraph _graph = new();
    private readonly object _compileLock = new();
    private readonly NoteEventQueue<NoteEvent> _events = new();

    private volatile CompiledGraph? _compiled;
    private volatile int _compiledRevision = -1;
    private AudioFormat _format = AudioFormat.Default;
    private int _maxBlock = InitialMaxBlock;

    public FieldInstrument(IFieldNodeRegistry registry, bool buildDefault = true)
    {
        _registry = registry;
        if (buildDefault) FieldPatches.BuildBeginnerInstrument(_graph);
    }

    public string Name => "Field";
    public string TypeId => Id;
    public IReadOnlyList<Parameter> Parameters => Array.Empty<Parameter>();
    public bool HasActiveVoices
    {
        get
        {
            var compiled = _compiled;
            return compiled is not null && AnyVoiceActive(compiled);
        }
    }

    /// <summary>The editable graph. UI edits this then calls <see cref="Recompile"/>.</summary>
    public FieldGraph Graph => _graph;

    /// <summary>The current compiled snapshot (for the UI to read scope taps etc.). May be null before Prepare.</summary>
    public CompiledGraph? Compiled => _compiled;

    public void Prepare(AudioFormat format)
    {
        _format = format;
        Recompile(_maxBlock);
    }

    /// <summary>Rebuilds the compiled snapshot from the current graph and swaps it in atomically.</summary>
    public void Recompile() => Recompile(_maxBlock);

    private void Recompile(int minBlock)
    {
        lock (_compileLock)
        {
            if (minBlock > _maxBlock) _maxBlock = minBlock;
            var compiled = FieldGraphCompiler.Compile(_graph, _format, _maxBlock, isInstrument: true);
            _compiledRevision = _graph.Revision;
            _compiled = compiled;
        }
    }

    public void NoteOn(int midiNote, float velocity) => Enqueue(new NoteEvent(EvType.On, midiNote, velocity));
    public void NoteOff(int midiNote) => Enqueue(new NoteEvent(EvType.Off, midiNote, 0));
    public void AllNotesOff() => Enqueue(new NoteEvent(EvType.AllOff, 0, 0));

    public void PitchBend(int value14)
    {
        var semis = (value14 - 8192) / 8192.0 * 2.0;
        Enqueue(new NoteEvent(EvType.Bend, 0, (float)semis));
    }

    public void ControlChange(int controller, int value)
        => Enqueue(new NoteEvent(EvType.Cc, 0, value, controller));

    private void Enqueue(in NoteEvent ev) => _events.Enqueue(ev);

    public void Render(Span<float> buffer)
    {
        var compiled = _compiled;
        if (compiled is null) return; // prepared by the engine before the first render

        var pending = _events.Drain();
        if (pending.Length > 0)
        {
            foreach (var ev in pending)
            {
                switch (ev.Type)
                {
                    case EvType.On: compiled.NoteOn(ev.Note, ev.Value); break;
                    case EvType.Off: compiled.NoteOff(ev.Note); break;
                    case EvType.AllOff: compiled.AllNotesOff(); break;
                    case EvType.Bend: compiled.PitchBend(ev.Value); break;
                    case EvType.Cc: compiled.ControlChange(ev.Controller, (int)ev.Value); break;
                }
            }
        }
        else if (!AnyVoiceActive(compiled))
        {
            return;
        }

        // Process in chunks no larger than the compiled block size (handles any host buffer length without
        // recompiling on the audio thread — recompilation happens only via Prepare / UI edits).
        var channels = _format.Channels < 1 ? 1 : _format.Channels;
        var frames = buffer.Length / channels;
        var max = compiled.MaxBlock;
        var offset = 0;
        while (offset < frames)
        {
            var n = Math.Min(max, frames - offset);
            compiled.Process(buffer.Slice(offset * channels, n * channels), 120.0, 0.0, false, ReadOnlySpan<float>.Empty, 0);
            offset += n;
        }
    }

    private static bool AnyVoiceActive(CompiledGraph compiled)
    {
        foreach (var v in compiled.Voices.Voices)
            if (v.Active) return true;
        return false;
    }

    public IInstrument Clone()
    {
        var copy = new FieldInstrument(_registry, buildDefault: false);
        using var ms = new MemoryStream();
        using (var w = new OngenWriter(ms)) FieldGraphSerializer.Write(w, _graph);
        ms.Position = 0;
        using (var r = new OngenReader(ms)) FieldGraphSerializer.Read(r, copy._graph, _registry);
        return copy;
    }

    // Built-in decomposition patches, one per built-in instrument (Oscillator, 3x Osc, FM, ...).
    public IReadOnlyList<string> PresetNames => FieldBuiltInPatches.InstrumentPatchNames;

    public void LoadPreset(int index)
    {
        FieldBuiltInPatches.BuildInstrument(index, _graph, _registry);
        Recompile(_maxBlock);
    }

    public void WriteProjectState(OngenWriter writer) => FieldGraphSerializer.Write(writer, _graph);

    public void ReadProjectState(OngenReader reader)
    {
        FieldGraphSerializer.Read(reader, _graph, _registry);
        Recompile(_maxBlock);
    }
}
