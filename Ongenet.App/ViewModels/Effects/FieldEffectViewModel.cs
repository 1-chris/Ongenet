using System;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.ViewModels;
using Ongenet.App.ViewModels.Field;
using Ongenet.Core.Audio.Field;

namespace Ongenet.App.ViewModels.Effects;

/// <summary>
/// The effect-chain card for the Field modular effect: header plus either an authored custom surface
/// or the embedded <see cref="FieldEditorViewModel"/> graph editor.
/// </summary>
public sealed class FieldEffectViewModel : EffectViewModel
{
    private bool _forceEditor;
    private FieldSurfaceViewModel? _surface;
    private readonly RelayCommand _toggleFieldEditorCommand;

    public FieldEffectViewModel(FieldEffect effect, Action<EffectViewModel> remove,
        Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
        : base(effect, remove, moveUp, moveDown)
    {
        var registry = App.ServiceProvider?.GetService<IFieldNodeRegistry>() ?? new FieldNodeRegistry();
        FieldEditor = new FieldEditorViewModel(effect.Graph, registry, effect.Recompile,
            FieldEffect.BuiltInPatchNames, i =>
            {
                effect.LoadBuiltInPatch(i);
                _surface = null;
                _forceEditor = false;
                OnPropertyChanged(nameof(HasCustomFieldSurface));
                OnPropertyChanged(nameof(ShowFieldEditor));
                OnPropertyChanged(nameof(ShowFieldSurface));
                OnPropertyChanged(nameof(FieldSurface));
                OnPropertyChanged(nameof(FieldEditorToggleText));
            }, () => effect.Compiled, isInstrument: false,
            effectHost: () => effect);

        _toggleFieldEditorCommand = new RelayCommand(() =>
        {
            _forceEditor = !_forceEditor;
            OnPropertyChanged(nameof(ShowFieldEditor));
            OnPropertyChanged(nameof(ShowFieldSurface));
            OnPropertyChanged(nameof(FieldEditorToggleText));
        });
    }

    public FieldEffect FieldEffect => (FieldEffect)Effect;

    /// <summary>The node-graph editor for this effect's Field graph.</summary>
    public FieldEditorViewModel FieldEditor { get; }

    public override bool HasCustomFieldSurface => FieldEffect.HasCustomSurface;
    public bool ShowFieldEditor => _forceEditor || !HasCustomFieldSurface;
    public bool ShowFieldSurface => HasCustomFieldSurface && !_forceEditor;
    public override string FieldEditorToggleText => _forceEditor ? "Show interface" : "Edit graph";
    public override RelayCommand ToggleFieldEditorCommand => _toggleFieldEditorCommand;

    public FieldSurfaceViewModel? FieldSurface
    {
        get
        {
            if (!FieldEffect.HasCustomSurface) return null;
            return _surface ??= new FieldSurfaceViewModel(FieldEffect.Graph, FieldEffect.Surface,
                () => FieldEffect.SetSurface(_surface!.Surface));
        }
    }
}
