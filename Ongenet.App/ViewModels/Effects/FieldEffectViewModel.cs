using System;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio.Field;
using Ongenet.App.ViewModels.Field;

namespace Ongenet.App.ViewModels.Effects;

/// <summary>
/// The effect-chain card for the Field modular effect: the standard effect header plus an embedded
/// <see cref="FieldEditorViewModel"/> (the node-graph editor over the effect's live graph).
/// </summary>
public sealed class FieldEffectViewModel : EffectViewModel
{
    public FieldEffectViewModel(FieldEffect effect, Action<EffectViewModel> remove,
        Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
        : base(effect, remove, moveUp, moveDown)
    {
        var registry = App.ServiceProvider?.GetService<IFieldNodeRegistry>() ?? new FieldNodeRegistry();
        FieldEditor = new FieldEditorViewModel(effect.Graph, registry, effect.Recompile,
            FieldEffect.BuiltInPatchNames, effect.LoadBuiltInPatch, () => effect.Compiled, isInstrument: false,
            effectHost: () => effect);
    }

    /// <summary>The node-graph editor for this effect's Field graph.</summary>
    public FieldEditorViewModel FieldEditor { get; }
}
