using System.Collections.Generic;

namespace Ongenet.App.ViewModels.Effects
{
    /// <summary>A named group of add-effect menu entries (Dynamics, Modulation, Plugins, …).</summary>
    public sealed class EffectCategoryViewModel
    {
        public EffectCategoryViewModel(string name, IReadOnlyList<AvailableEffectViewModel> effects)
        {
            Name = name;
            Effects = effects;
        }

        public string Name { get; }
        public IReadOnlyList<AvailableEffectViewModel> Effects { get; }
    }
}
