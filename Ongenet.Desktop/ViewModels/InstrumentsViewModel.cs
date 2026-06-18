using System.Collections.ObjectModel;
using Avalonia.Threading;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Desktop.ViewModels
{
    /// <summary>
    /// The Instruments tab in the right sidebar: lists the available instruments (built-ins plus
    /// discovered CLAP plugins), which the user can drag onto the timeline to create a track.
    /// Refreshes when the registry changes (e.g. as CLAP plugins finish scanning).
    /// </summary>
    public class InstrumentsViewModel : ViewModelBase
    {
        private readonly IInstrumentRegistry _registry;

        public InstrumentsViewModel(IInstrumentRegistry registry)
        {
            _registry = registry;
            Refresh();
            // Registry changes may fire from a background scan thread — marshal to the UI thread.
            _registry.Changed += () => Dispatcher.UIThread.Post(Refresh);
        }

        /// <summary>All available instrument types.</summary>
        public ObservableCollection<InstrumentInfo> Instruments { get; } = new();

        private void Refresh()
        {
            Instruments.Clear();
            foreach (var info in _registry.Available) Instruments.Add(info);
        }
    }
}
