using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.MidiFx;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Containers;

/// <summary>
/// Base class for container instruments with nested <see cref="InstrumentSlot"/> children and
/// standard persistence via <see cref="IProjectStatefulComponent"/>.
/// </summary>
public abstract class ContainerInstrumentBase : IContainerInstrument, IProjectStatefulComponent
{
    private readonly List<InstrumentSlot> _children = new();
    private AudioFormat _format = AudioFormat.Default;
    protected float[] Scratch = Array.Empty<float>();

    public IReadOnlyList<InstrumentSlot> Children => _children;

    public abstract string Name { get; }

    string IInstrument.TypeId => GetTypeId();
    protected abstract string GetTypeId();

    public virtual IReadOnlyList<Parameter> Parameters { get; } = Array.Empty<Parameter>();

    protected List<InstrumentSlot> MutableChildren => _children;

    /// <summary>When true, the UI may append nested instrument slots.</summary>
    public virtual bool CanAddChildren => false;

    /// <summary>When true, the UI may remove nested slots (subject to <see cref="MinChildren"/>).</summary>
    public virtual bool CanRemoveChildren => false;

    /// <summary>Minimum nested slots this container requires.</summary>
    public virtual int MinChildren => 0;

    /// <summary>Maximum nested slots, or null when unbounded.</summary>
    public virtual int? MaxChildren => null;

    public void AddChild(IInstrument instrument)
    {
        if (!CanAddChildren) return;
        if (MaxChildren is int max && _children.Count >= max) return;
        _children.Add(CreateSlot(instrument));
    }

    public void RemoveChildAt(int index)
    {
        if (!CanRemoveChildren) return;
        if (index < 0 || index >= _children.Count) return;
        if (_children.Count <= MinChildren) return;
        _children[index].Instrument.AllNotesOff();
        _children.RemoveAt(index);
    }

    public void ReplaceChildAt(int index, IInstrument instrument)
    {
        if (index < 0 || index >= _children.Count) return;
        _children[index].Instrument.AllNotesOff();
        var old = _children[index];
        var slot = new InstrumentSlot(instrument)
        {
            Enabled = old.Enabled,
            OutputBusIndex = old.OutputBusIndex,
            OutputTrackId = old.OutputTrackId
        };
        foreach (var fx in old.Effects) slot.Effects.Add(fx.Clone());
        slot.CommitEffects();
        _children[index] = slot;
    }

    public void MoveChild(int from, int to)
    {
        if (from < 0 || from >= _children.Count || to < 0 || to >= _children.Count || from == to) return;
        var slot = _children[from];
        _children.RemoveAt(from);
        _children.Insert(to, slot);
    }

    public virtual void Prepare(AudioFormat format)
    {
        _format = format;
        EnsureScratch(format);
        foreach (var slot in _children)
        {
            ContainerRenderer.PrepareInstrument(slot.Instrument, format);
            foreach (var fx in slot.ActiveEffects)
                ContainerRenderer.PrepareEffect(fx, format);
        }
    }

    public virtual void Render(Span<float> buffer)
    {
        EnsureScratch(_format);
        ContainerRenderer.RenderChildren(this, buffer, Scratch);
    }

    public virtual void NoteOn(int midiNote, float velocity)
    {
        if (this is INoteRouter router)
        {
            var indices = router.RouteNote(midiNote, velocity);
            if (indices is null)
            {
                foreach (var slot in _children)
                {
                    if (!slot.Enabled) continue;
                    slot.Instrument.NoteOn(midiNote, velocity);
                }
            }
            else
            {
                foreach (var idx in indices)
                {
                    if (idx < 0 || idx >= _children.Count) continue;
                    var slot = _children[idx];
                    if (!slot.Enabled) continue;
                    slot.Instrument.NoteOn(midiNote, velocity);
                }
            }

            return;
        }

        foreach (var slot in _children)
        {
            if (!slot.Enabled) continue;
            slot.Instrument.NoteOn(midiNote, velocity);
        }
    }

    public virtual void NoteOff(int midiNote)
    {
        if (this is INoteRouter router)
        {
            var indices = router.RouteNote(midiNote, 0f);
            if (indices is null)
            {
                foreach (var slot in _children) slot.Instrument.NoteOff(midiNote);
            }
            else
            {
                foreach (var idx in indices)
                {
                    if (idx < 0 || idx >= _children.Count) continue;
                    _children[idx].Instrument.NoteOff(midiNote);
                }
            }

            return;
        }

        foreach (var slot in _children) slot.Instrument.NoteOff(midiNote);
    }

    public virtual void AllNotesOff() => ContainerRenderer.AllNotesOffInstrument(this);

    public virtual void ControlChange(int controller, int value)
    {
        foreach (var slot in _children)
        {
            if (!slot.Enabled) continue;
            slot.Instrument.ControlChange(controller, value);
        }
    }

    public virtual void PitchBend(int value14)
    {
        foreach (var slot in _children)
        {
            if (!slot.Enabled) continue;
            slot.Instrument.PitchBend(value14);
        }
    }

    public virtual void SetHostTempo(double bpm)
    {
        foreach (var slot in _children) slot.Instrument.SetHostTempo(bpm);
    }

    public virtual void WriteProjectState(OngenWriter writer)
    {
        var store = ContainerWriteContext.Current?.Store ?? new SampleStore();
        ContainerPersistence.WriteInstrumentSlots(writer, _children, store);
    }

    public virtual void ReadProjectState(OngenReader reader)
    {
        // Registries are injected via ContainerReadContext when loading projects.
        if (ContainerReadContext.Current is not { } ctx)
        {
            reader.ReadInt(); // skip slot count
            return;
        }

        ContainerPersistence.ReadInstrumentSlots(reader, _children, ctx.Instruments, ctx.Effects,
            ctx.MidiEffects, ctx.SampleLookup, ctx.Warnings);
        foreach (var slot in _children) slot.CommitEffects();
    }

    public abstract IInstrument Clone();

    protected void CloneChildrenInto(ContainerInstrumentBase dst)
    {
        foreach (var src in _children)
        {
            var inst = src.Instrument.Clone();
            var slot = new InstrumentSlot(inst) { Enabled = src.Enabled, OutputBusIndex = src.OutputBusIndex, OutputTrackId = src.OutputTrackId };
            foreach (var fx in src.Effects)
                slot.Effects.Add(((IAudioEffect)fx).Clone());
            slot.CommitEffects();
            dst._children.Add(slot);
        }
    }

    protected void EnsureScratch(AudioFormat format)
    {
        var channels = format.Channels < 1 ? 1 : format.Channels;
        var len = Math.Max(8192, channels * 4096);
        if (Scratch.Length < len) Scratch = new float[len];
    }

    protected static InstrumentSlot CreateSlot(IInstrument instrument, bool enabled = true)
    {
        var slot = new InstrumentSlot(instrument) { Enabled = enabled };
        slot.CommitEffects();
        return slot;
    }
}

/// <summary>Thread-local context supplying registries while loading container custom state.</summary>
public sealed class ContainerReadContext
{
    public static ContainerReadContext? Current { get; private set; }

    public required IInstrumentRegistry Instruments { get; init; }
    public required IEffectRegistry Effects { get; init; }
    public IMidiEffectRegistry? MidiEffects { get; init; }
    public Func<string, Files.AudioSampleBuffer?> SampleLookup { get; init; } = _ => null;
    public List<string> Warnings { get; init; } = new();

    public static IDisposable Scope(ContainerReadContext ctx)
    {
        var prev = Current;
        Current = ctx;
        return new ScopeToken(prev);
    }

    private sealed class ScopeToken : IDisposable
    {
        private readonly ContainerReadContext? _prev;
        public ScopeToken(ContainerReadContext? prev) => _prev = prev;
        public void Dispose() => Current = _prev;
    }
}
