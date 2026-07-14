using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Modulation;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Persistence;

/// <summary>Serializes registry-backed <see cref="ModulatorSlot"/> chains for modulator preset files.</summary>
public static class ModulatorPresetPersistence
{
    public static void WriteSlots(OngenWriter writer, IReadOnlyList<ModulatorSlot> slots)
    {
        writer.WriteInt(slots.Count);
        foreach (var slot in slots)
        {
            writer.WriteBool(slot.Enabled);
            writer.WriteDouble(slot.Depth);
            writer.WriteString(slot.Source.TypeId);
            writer.WriteBool(slot.Source.Enabled);
            ComponentSerializer.WriteParameters(writer, slot.Source.Parameters);
            writer.WriteInt((int)slot.Target.Kind);
            writer.WriteInt(slot.Target.EffectIndex);
            writer.WriteInt(slot.Target.ParamIndex);
        }
    }

    public static List<ModulatorSlot> ReadSlots(OngenReader reader, IModulatorRegistry modulators)
    {
        var count = reader.ReadInt();
        var list = new List<ModulatorSlot>(count);
        for (var i = 0; i < count; i++)
        {
            var enabled = reader.ReadBool();
            var depth = reader.ReadDouble();
            var typeId = reader.ReadString();
            var modEnabled = reader.ReadBool();
            var persisted = ComponentSerializer.ReadParameters(reader);
            var source = modulators.Create(typeId);
            source.Enabled = modEnabled;
            ComponentSerializer.ApplyParameters(source.Parameters, persisted);
            var target = new AutomationBinding(
                (AutomationTargetKind)reader.ReadInt(),
                reader.ReadInt(),
                reader.ReadInt());
            list.Add(new ModulatorSlot
            {
                Enabled = enabled,
                Depth = depth,
                Source = source,
                Target = target
            });
        }

        return list;
    }
}
