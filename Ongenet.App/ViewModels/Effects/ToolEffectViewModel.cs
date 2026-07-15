using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.App.ViewModels.Effects;

/// <summary>Tool utility card — gain/pan/mono/phase with live correlation metering via Wave Candy.</summary>
public sealed class ToolEffectViewModel : EffectViewModel
{
    public ToolEffectViewModel(ToolEffect effect, Action<EffectViewModel> remove,
        Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
        : base(effect, remove, moveUp, moveDown)
    {
    }

    public ToolEffect Tool => (ToolEffect)Effect;
    public IAudioAnalyzerSource Analyzer => Tool;
}
