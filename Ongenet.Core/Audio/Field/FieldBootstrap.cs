using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// One-time Field wiring, run by the app after the DI provider is built (and after plugin scanning starts):
/// registers the "field" instrument and effect into their registries, installs user-definition fallbacks,
/// and registers a module-wrapper node for every instrument/effect/plugin.
/// </summary>
public static class FieldBootstrap
{
    public static void Initialize(IFieldNodeRegistry nodes, IInstrumentRegistry instruments, IEffectRegistry effects)
    {
        instruments.Register(new InstrumentInfo(FieldInstrument.Id, "Field", () => new FieldInstrument(nodes), "Synth"));
        effects.Register(new EffectInfo(FieldEffect.Id, "Field", () => new FieldEffect(nodes), "Field"));

        instruments.SetFallbackCreate(id =>
            FieldGraphDefinition.IsUserInstrumentType(id)
                ? FieldInstrument.CreateShell(nodes, id)
                : null);

        effects.SetFallbackCreate(id =>
            FieldGraphDefinition.IsUserEffectType(id)
                ? FieldEffect.CreateShell(nodes, id)
                : null);

        FieldModuleNodes.RegisterAll(nodes, instruments, effects);
    }
}
