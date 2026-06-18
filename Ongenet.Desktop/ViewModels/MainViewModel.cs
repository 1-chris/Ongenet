using System.Collections.ObjectModel;
using Ongenet.Core.Models.Logging;
using Ongenet.Core.Services.Implementation;

namespace Ongenet.Desktop.ViewModels
{
    /// <summary>
    /// Root view model for the main window. Composes the panel view models that make up the
    /// DAW layout (transport, timeline, inspectors, file browser); each is injected and owns
    /// its own state.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly ObservableCollection<LogEntry> _logEntries;

        public MainViewModel(
            TransportViewModel transport,
            TimelineViewModel timeline,
            TrackInspectorViewModel trackInspector,
            BottomPanelViewModel bottomPanel,
            FileBrowserViewModel fileBrowser,
            InstrumentsViewModel instruments,
            ObservableCollectionLoggerProvider? logProvider = null)
        {
            Transport = transport;
            Timeline = timeline;
            TrackInspector = trackInspector;
            BottomPanel = bottomPanel;
            FileBrowser = fileBrowser;
            Instruments = instruments;
            _logEntries = logProvider?.LogEntries ?? new ObservableCollection<LogEntry>();
        }

        /// <summary>App name + version for the window title (and OS taskbar), e.g. "Ongenet v0.1.0".</summary>
        public string Title => $"{AppInfo.Name} {Version}";

        /// <summary>Version label shown next to the name in the title bar, e.g. "v0.1.0".</summary>
        public string Version => $"v{AppInfo.Version}";

        /// <summary>Top-bar transport (play/stop, tempo).</summary>
        public TransportViewModel Transport { get; }

        /// <summary>Centre arrange view (ruler + track lanes).</summary>
        public TimelineViewModel Timeline { get; }

        /// <summary>Left-hand selected-track inspector.</summary>
        public TrackInspectorViewModel TrackInspector { get; }

        /// <summary>Bottom-centre tabbed panel (Instrument / Piano Roll / Effects).</summary>
        public BottomPanelViewModel BottomPanel { get; }

        /// <summary>Right-hand file browser.</summary>
        public FileBrowserViewModel FileBrowser { get; }

        /// <summary>Right-hand instruments list.</summary>
        public InstrumentsViewModel Instruments { get; }

        /// <summary>Log entries captured by the in-app logger; surfaced by the Log window.</summary>
        public ObservableCollection<LogEntry> LogEntries => _logEntries;
    }
}
