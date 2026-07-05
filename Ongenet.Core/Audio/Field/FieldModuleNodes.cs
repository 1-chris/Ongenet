using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field.Nodes;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// Registers a Field module-wrapper node for every instrument and effect in the instrument/effect
/// registries (built-ins and discovered CLAP/LV2/VST/AU plugins), so any whole component can be dropped
/// into a Field graph. Re-runs when the registries change (e.g. plugins finish scanning). The Field host
/// itself is excluded to avoid infinite recursion.
/// </summary>
public static class FieldModuleNodes
{
    public const string FieldTypeId = "field";

    public static void RegisterAll(IFieldNodeRegistry nodes, IInstrumentRegistry instruments, IEffectRegistry effects)
    {
        Sync(nodes, instruments, effects);
        instruments.Changed += () => Sync(nodes, instruments, effects);
        effects.Changed += () => Sync(nodes, instruments, effects);
    }

    private static void Sync(IFieldNodeRegistry nodes, IInstrumentRegistry instruments, IEffectRegistry effects)
    {
        foreach (var info in instruments.Available)
        {
            if (info.Id == FieldTypeId) continue;
            var id = info.Id;
            var name = info.DisplayName;
            nodes.Register(new FieldNodeInfo(InstrumentModuleNode.Prefix + id, name, FieldNodeCategories.Modules,
                () => new InstrumentModuleNode(instruments.Create(id))) { Description = $"Instrument: {name}" });
        }

        foreach (var info in effects.Available)
        {
            if (info.Id == FieldTypeId) continue;
            var id = info.Id;
            var name = info.DisplayName;
            nodes.Register(new FieldNodeInfo(EffectModuleNode.Prefix + id, name, FieldNodeCategories.Modules,
                () => new EffectModuleNode(effects.Create(id))) { Description = $"Effect: {name}" });
        }
    }
}
