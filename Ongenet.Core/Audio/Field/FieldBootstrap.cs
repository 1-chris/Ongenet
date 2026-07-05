using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// One-time Field wiring, run by the app after the DI provider is built (and after plugin scanning starts):
/// registers the "field" instrument and effect into their registries, and registers a module-wrapper node
/// for every instrument/effect/plugin. Kept out of the DI factories to avoid a construction cycle (the
/// field instrument/effect need the node registry; the module nodes need all three registries).
/// </summary>
public static class FieldBootstrap
{
    public static void Initialize(IFieldNodeRegistry nodes, IInstrumentRegistry instruments, IEffectRegistry effects)
    {
        instruments.Register(new InstrumentInfo(FieldInstrument.Id, "Field", () => new FieldInstrument(nodes), "Synth"));
        effects.Register(new EffectInfo(FieldEffect.Id, "Field", () => new FieldEffect(nodes), "Field"));
        FieldModuleNodes.RegisterAll(nodes, instruments, effects);
    }
}
