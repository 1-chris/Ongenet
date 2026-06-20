using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Desktop.ViewModels
{
    /// <summary>
    /// The Instruments tab in the right sidebar: lists the available instruments (built-ins plus
    /// discovered CLAP/LV2 plugins) grouped by category, which the user can drag onto the timeline
    /// or onto an instrument track. Refreshes when the registry changes (e.g. as plugins finish scanning).
    /// </summary>
    public class InstrumentsViewModel : ViewModelBase
    {
        private readonly IInstrumentRegistry _registry;

        // Preferred display order for the instrument-library categories (mirrors EffectsViewModel).
        private static readonly string[] CategoryOrder = { "Synth", "Sampler", "Drum", "Plugins" };

        public InstrumentsViewModel(IInstrumentRegistry registry)
        {
            _registry = registry;
            Refresh();
            // Registry changes may fire from a background scan thread — marshal to the UI thread.
            _registry.Changed += () => Dispatcher.UIThread.Post(Refresh);
        }

        /// <summary>Available instruments grouped by category, in preferred order.</summary>
        public ObservableCollection<InstrumentCategoryViewModel> Categories { get; } = new();

        private void Refresh()
        {
            int Rank(string category)
            {
                var i = Array.IndexOf(CategoryOrder, category);
                return i < 0 ? CategoryOrder.Length : i;
            }

            Categories.Clear();
            var grouped = _registry.Available
                .GroupBy(info => info.Category)
                .OrderBy(g => Rank(g.Key)).ThenBy(g => g.Key)
                .Select(g => new InstrumentCategoryViewModel(g.Key, g.ToList()));
            foreach (var cat in grouped) Categories.Add(cat);
        }
    }
}
