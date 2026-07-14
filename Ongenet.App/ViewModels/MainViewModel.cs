using System.Collections.ObjectModel;
using Ongenet.Core.Models.Logging;
using Ongenet.Core.Services.Implementation;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels
{
    public enum CenterTab
    {
        Arrangement = 0,
        Mixer = 1,
        Session = 2,
        Notation = 3,
        Video = 4,
        Scripting = 5
    }

    /// <summary>
    /// Root view model for the main window. Composes the panel view models that make up the
    /// DAW layout (transport, timeline, inspectors, file browser); each is injected and owns
    /// its own state.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly ObservableCollection<LogEntry> _logEntries;
        private readonly IProjectFileService _projectFile;
        private readonly Services.IHistoryService _history;
        private readonly Services.IAppSettingsService _settings;
        private int _selectedCenterTabIndex;
        private int _savedBottomTabIndex;
        private bool _inVideoMode;

        public MainViewModel(
            TransportViewModel transport,
            TimelineViewModel timeline,
            TrackInspectorViewModel trackInspector,
            BottomPanelViewModel bottomPanel,
            FileBrowserViewModel fileBrowser,
            AudioPreviewViewModel preview,
            LibraryOptionsViewModel libraryOptions,
            Library.EverythingLibraryViewModel everything,
            Library.EffectsLibraryViewModel effectsLibrary,
            Library.SampleLibraryViewModel sampleLibrary,
            Library.SoundFontLibraryViewModel soundFontLibrary,
            Library.InstrumentLibraryViewModel instrumentLibrary,
            Library.InstrumentPresetLibraryViewModel instrumentPresets,
            Library.EffectPresetLibraryViewModel effectPresets,
            Library.EffectChainPresetLibraryViewModel chainPresets,
            Library.ModulatorPresetLibraryViewModel modulatorPresets,
            Library.ProjectsLibraryViewModel projectsLibrary,
            ProjectClipsViewModel projectClips,
            Panels.MixerViewModel mixer,
            Panels.SessionViewModel session,
            Panels.NotationViewModel notation,
            Panels.VideoTrackViewModel video,
            Panels.VideoResourcesViewModel videoResources,
            Panels.ScriptingPanelViewModel scripting,
            IProjectFileService projectFile,
            Services.IHistoryService history,
            Services.IAppSettingsService settings,
            ObservableCollectionLoggerProvider? logProvider = null)
        {
            Transport = transport;
            Timeline = timeline;
            TrackInspector = trackInspector;
            BottomPanel = bottomPanel;
            FileBrowser = fileBrowser;
            Preview = preview;
            LibraryOptions = libraryOptions;
            Everything = everything;
            EffectsLibrary = effectsLibrary;
            SampleLibrary = sampleLibrary;
            SoundFontLibrary = soundFontLibrary;
            InstrumentLibrary = instrumentLibrary;
            InstrumentPresets = instrumentPresets;
            EffectPresets = effectPresets;
            EffectChainPresets = chainPresets;
            ModulatorPresets = modulatorPresets;
            ProjectsLibrary = projectsLibrary;
            ProjectClips = projectClips;
            Mixer = mixer;
            Session = session;
            Notation = notation;
            Video = video;
            VideoResources = videoResources;
            Scripting = scripting;
            _projectFile = projectFile;
            _history = history;
            _settings = settings;
            _settings.VideoEnabledChanged += () =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    OnPropertyChanged(nameof(ShowVideoTab));
                    OnPropertyChanged(nameof(ShowExportVideo));
                });
            _history.Changed += () =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    OnPropertyChanged(nameof(CanUndo));
                    OnPropertyChanged(nameof(CanRedo));
                });
            _projectFile.Changed += () =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    OnPropertyChanged(nameof(Title));
                    OnPropertyChanged(nameof(IsBusy));
                    OnPropertyChanged(nameof(BusyStatus));
                });
            _logEntries = logProvider?.LogEntries ?? new ObservableCollection<LogEntry>();
            Video.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Panels.VideoTrackViewModel.IsProjectVideoEnabled))
                {
                    UpdateVideoEditingMode();
                    OnPropertyChanged(nameof(ShowExportVideo));
                }
            };
        }

        /// <summary>Window title: project name (with a "*" when there are unsaved changes) + app version.</summary>
        public string Title =>
            $"{_projectFile.DisplayName}{(_projectFile.IsDirty ? "*" : "")} — {AppInfo.Name} {Version}";

        /// <summary>True while a save/load is running (shows the title-bar progress indicator).</summary>
        public bool IsBusy => _projectFile.IsBusy;

        /// <summary>Busy indicator caption ("Saving…"/"Loading…").</summary>
        public string BusyStatus => _projectFile.BusyStatus;

        /// <summary>Version label shown next to the name in the title bar, e.g. "v0.1.0".</summary>
        public string Version => $"v{AppInfo.Version}";

        /// <summary>Whether undo/redo are available (drives the title-bar buttons' enabled state).</summary>
        public bool CanUndo => _history.CanUndo;
        public bool CanRedo => _history.CanRedo;

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

        /// <summary>Shared audio preview (waveform + stats + audition) docked under the library tabs.</summary>
        public AudioPreviewViewModel Preview { get; }

        /// <summary>Sample-tab options (auto-stretch + pitch correction) docked under the library tabs.</summary>
        public LibraryOptionsViewModel LibraryOptions { get; }

        /// <summary>Library tabs.</summary>
        public Library.EverythingLibraryViewModel Everything { get; }
        public Library.EffectsLibraryViewModel EffectsLibrary { get; }
        public Library.SampleLibraryViewModel SampleLibrary { get; }
        public Library.SoundFontLibraryViewModel SoundFontLibrary { get; }
        public Library.InstrumentLibraryViewModel InstrumentLibrary { get; }
        public Library.InstrumentPresetLibraryViewModel InstrumentPresets { get; }
        public Library.EffectPresetLibraryViewModel EffectPresets { get; }
        public Library.EffectChainPresetLibraryViewModel EffectChainPresets { get; }
        public Library.ModulatorPresetLibraryViewModel ModulatorPresets { get; }
        public Library.ProjectsLibraryViewModel ProjectsLibrary { get; }

        /// <summary>Left sidebar's Project Clips tab (unique clips across the project).</summary>
        public ProjectClipsViewModel ProjectClips { get; }

        /// <summary>Dedicated mixer panel with channel strips and sends.</summary>
        public Panels.MixerViewModel Mixer { get; }

        /// <summary>Session view clip launcher grid.</summary>
        public Panels.SessionViewModel Session { get; }

        /// <summary>Staff notation view with MusicXML export.</summary>
        public Panels.NotationViewModel Notation { get; }

        /// <summary>Video track sync panel.</summary>
        public Panels.VideoTrackViewModel Video { get; }

        /// <summary>Left sidebar resources bin when video editing mode is active.</summary>
        public Panels.VideoResourcesViewModel VideoResources { get; }

        /// <summary>Selected centre tab index (Arrangement, Mixer, …).</summary>
        public int SelectedCenterTabIndex
        {
            get => _selectedCenterTabIndex;
            set
            {
                if (!SetField(ref _selectedCenterTabIndex, value)) return;
                OnPropertyChanged(nameof(IsVideoEditingMode));
                UpdateVideoEditingMode();
            }
        }

        /// <summary>True when the Video centre tab is selected and video is enabled.</summary>
        public bool IsVideoEditingMode => ShowVideoTab && SelectedCenterTabIndex == (int)CenterTab.Video;

        private void UpdateVideoEditingMode()
        {
            BottomPanel.IsVideoEditingMode = IsVideoEditingMode;
            if (IsVideoEditingMode)
            {
                if (!_inVideoMode)
                    _savedBottomTabIndex = BottomPanel.SelectedTabIndex;
                BottomPanel.ShowVideoTimelineTab();
                _inVideoMode = true;
            }
            else if (_inVideoMode)
            {
                BottomPanel.SelectedTabIndex = _savedBottomTabIndex;
                _inVideoMode = false;
            }
        }

        /// <summary>When false, the Video centre tab is hidden (app setting).</summary>
        public bool ShowVideoTab => _settings.Current.VideoEnabled;

        /// <summary>When true, the title bar shows Export video (app + project video enabled).</summary>
        public bool ShowExportVideo => _settings.Current.VideoEnabled && Video.IsProjectVideoEnabled;

        /// <summary>In-app scripting IDE (script list, editor, output).</summary>
        public Panels.ScriptingPanelViewModel Scripting { get; }

        /// <summary>Log entries captured by the in-app logger; surfaced by the Log window.</summary>
        public ObservableCollection<LogEntry> LogEntries => _logEntries;
    }
}
