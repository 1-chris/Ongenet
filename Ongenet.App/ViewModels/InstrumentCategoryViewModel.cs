using System.Collections.Generic;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.App.ViewModels
{
    /// <summary>A named group of instrument-library entries (Synth, Sampler, Drum, Plugins, …).</summary>
    public sealed class InstrumentCategoryViewModel
    {
        public InstrumentCategoryViewModel(string name, IReadOnlyList<InstrumentInfo> instruments)
        {
            Name = name;
            Instruments = instruments;
        }

        public string Name { get; }
        public IReadOnlyList<InstrumentInfo> Instruments { get; }
    }
}
