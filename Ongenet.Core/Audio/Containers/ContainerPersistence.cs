using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.MidiFx;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Containers;

/// <summary>
/// Reads/writes nested instrument slots, effect branches, and MIDI-FX chains for container devices.
/// </summary>
public static class ContainerPersistence
{
    public static void WriteInstrumentSlots(OngenWriter w, IReadOnlyList<InstrumentSlot> slots,
        SampleStore store)
    {
        w.WriteInt(slots.Count);
        foreach (var slot in slots)
        {
            ComponentSerializer.WriteComponent(w, slot.Instrument.TypeId, slot.Instrument,
                slot.Instrument.Parameters, store, slot.Enabled, slot.Instrument as ISampleHost);
            w.WriteInt(slot.Effects.Count);
            foreach (var fx in slot.Effects)
                ComponentSerializer.WriteComponent(w, fx.TypeId, fx, fx.Parameters, store, fx.Enabled, fx as ISampleHost);
            w.WriteInt(slot.OutputBusIndex);
            w.WriteNullableGuid(slot.OutputTrackId);
        }
    }

    public static void ReadInstrumentSlots(OngenReader r, List<InstrumentSlot> slots,
        IInstrumentRegistry instruments, IEffectRegistry effects, IMidiEffectRegistry? midiEffects,
        Func<string, AudioSampleBuffer?> sampleLookup, List<string> warnings)
    {
        slots.Clear();
        var count = r.ReadInt();
        for (var i = 0; i < count; i++)
        {
            var (inst, enabled) = ComponentSerializer.ReadInstrument(r, instruments, effects, midiEffects,
                sampleLookup, warnings);
            var fxCount = r.ReadInt();
            var slotFx = new List<IAudioEffect>();
            for (var j = 0; j < fxCount; j++)
            {
                if (ComponentSerializer.ReadEffect(r, instruments, effects, midiEffects, sampleLookup, warnings)
                    is { } sfx) slotFx.Add(sfx);
            }

            if (inst is null) continue;
            var slot = new InstrumentSlot(inst) { Enabled = enabled };
            foreach (var sfx in slotFx) slot.Effects.Add(sfx);
            slot.CommitEffects();
            slot.OutputBusIndex = r.ReadInt();
            slot.OutputTrackId = r.ReadNullableGuid();
            slots.Add(slot);
        }
    }

    public static void WriteEffectBranches(OngenWriter w, IReadOnlyList<ContainerEffectBranch> branches,
        SampleStore store)
    {
        w.WriteInt(branches.Count);
        foreach (var branch in branches)
        {
            w.WriteInt(branch.Effects.Count);
            foreach (var fx in branch.Effects)
                ComponentSerializer.WriteComponent(w, fx.TypeId, fx, fx.Parameters, store, fx.Enabled, fx as ISampleHost);
        }
    }

    public static void ReadEffectBranches(OngenReader r, List<ContainerEffectBranch> branches,
        IInstrumentRegistry instruments, IEffectRegistry effects, IMidiEffectRegistry? midiEffects,
        Func<string, AudioSampleBuffer?> sampleLookup, List<string> warnings)
    {
        branches.Clear();
        var branchCount = r.ReadInt();
        for (var b = 0; b < branchCount; b++)
        {
            var branch = new ContainerEffectBranch();
            var fxCount = r.ReadInt();
            for (var j = 0; j < fxCount; j++)
            {
                if (ComponentSerializer.ReadEffect(r, instruments, effects, midiEffects, sampleLookup, warnings)
                    is { } fx) branch.Effects.Add(fx);
            }

            branches.Add(branch);
        }
    }

    public static void WriteMidiEffectChain(OngenWriter w, IReadOnlyList<IMidiEffect> chain)
    {
        w.WriteInt(chain.Count);
        foreach (var fx in chain) MidiEffectSerializer.Write(w, fx);
    }

    public static void ReadMidiEffectChain(OngenReader r, List<IMidiEffect> chain,
        IMidiEffectRegistry registry, List<string> warnings)
    {
        chain.Clear();
        var count = r.ReadInt();
        for (var i = 0; i < count; i++)
            if (MidiEffectSerializer.Read(r, registry, warnings) is { } fx) chain.Add(fx);
    }

    public static void WriteEffectChain(OngenWriter w, IReadOnlyList<IAudioEffect> chain, SampleStore store)
    {
        w.WriteInt(chain.Count);
        foreach (var fx in chain)
            ComponentSerializer.WriteComponent(w, fx.TypeId, fx, fx.Parameters, store, fx.Enabled, fx as ISampleHost);
    }

    public static void ReadEffectChain(OngenReader r, List<IAudioEffect> chain,
        IInstrumentRegistry instruments, IEffectRegistry effects, IMidiEffectRegistry? midiEffects,
        Func<string, AudioSampleBuffer?> sampleLookup, List<string> warnings)
    {
        chain.Clear();
        var count = r.ReadInt();
        for (var i = 0; i < count; i++)
        {
            if (ComponentSerializer.ReadEffect(r, instruments, effects, midiEffects, sampleLookup, warnings)
                is { } fx) chain.Add(fx);
        }
    }
}
