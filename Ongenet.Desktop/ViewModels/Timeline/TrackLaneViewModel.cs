using System.Collections.ObjectModel;
using System.ComponentModel;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Desktop.ViewModels.Timeline
{
    /// <summary>
    /// View model for one track row in the timeline: the header data (name, colour, mute/solo,
    /// level meter), the clips laid out on the lane, and the per-track context-menu commands.
    /// </summary>
    public class TrackLaneViewModel : LaneViewModel
    {
        /// <summary>Fixed row height shared by the lane and its header, in pixels.</summary>
        public const double RowHeight = 64.0;

        private readonly TimelineMetrics _metrics;
        private readonly ITrackActions _actions;
        private readonly IClipActions _clipActions;
        private bool _isSelected;
        private bool _isDropTarget;

        public TrackLaneViewModel(Track model, TimelineMetrics metrics, ITrackActions actions, IClipActions clipActions)
        {
            Model = model;
            _metrics = metrics;
            _actions = actions;
            _clipActions = clipActions;
            _metrics.PropertyChanged += OnMetricsChanged;

            DuplicateCommand = new RelayCommand(() => _actions.DuplicateTrack(this));
            DeleteCommand = new RelayCommand(() => _actions.DeleteTrack(this));
            AddInstrumentTrackCommand = new RelayCommand(() => _actions.AddInstrumentTrack());
            AddAudioTrackCommand = new RelayCommand(() => _actions.AddAudioTrack());
            ToggleAutomationCommand = new RelayCommand(() => _actions.ToggleAutomation(this));

            foreach (var clip in model.Clips)
            {
                Clips.Add(new ClipViewModel(clip, model, metrics, clipActions));
            }
        }

        /// <summary>Track rows are a fixed height.</summary>
        public override double Height => RowHeight;

        /// <summary>Clip-action delegate, for clip VMs created after construction.</summary>
        public IClipActions ClipActions => _clipActions;

        /// <summary>The underlying domain track.</summary>
        public Track Model { get; }

        /// <summary>Shared timeline metrics (for the lane's grid background).</summary>
        public TimelineMetrics Metrics => _metrics;

        public string Name => Model.Name;

        /// <summary>Palette key or hex string; resolved to a brush in the view.</summary>
        public string ColorKey => Model.ColorKey;

        public bool IsMuted
        {
            get => Model.IsMuted;
            set
            {
                if (Model.IsMuted == value) return;
                Model.IsMuted = value;
                OnPropertyChanged();
                _actions.NotifyTrackChanged(Model);
            }
        }

        public bool IsSoloed
        {
            get => Model.IsSoloed;
            set
            {
                if (Model.IsSoloed == value) return;
                Model.IsSoloed = value;
                OnPropertyChanged();
                _actions.NotifyTrackChanged(Model);
            }
        }

        /// <summary>Whether this track is armed for recording (captures live MIDI input).</summary>
        public bool IsArmed
        {
            get => Model.IsArmed;
            set
            {
                if (Model.IsArmed == value) return;
                Model.IsArmed = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Live peak output level (0..1) for the header meter; polled from the engine.</summary>
        public float MeterLevel => Model.MeterLevel;

        public RelayCommand DuplicateCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand AddInstrumentTrackCommand { get; }
        public RelayCommand AddAudioTrackCommand { get; }

        /// <summary>Collapses/expands this track's automation rows.</summary>
        public RelayCommand ToggleAutomationCommand { get; }

        /// <summary>True when the track has one or more automation lanes (shows the collapse chevron).</summary>
        public bool HasAutomation => Model.AutoLanes.Count > 0;

        /// <summary>Whether this track's automation rows are hidden.</summary>
        public bool AutomationCollapsed
        {
            get => Model.AutomationCollapsed;
            set
            {
                if (Model.AutomationCollapsed == value) return;
                Model.AutomationCollapsed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CollapseGlyph));
            }
        }

        /// <summary>Chevron glyph for the collapse toggle (▾ expanded / ▸ collapsed).</summary>
        public string CollapseGlyph => Model.AutomationCollapsed ? "▸" : "▾";

        /// <summary>Re-reads automation-related header state after lanes are added/removed/collapsed.</summary>
        public void RefreshAutomationState()
        {
            OnPropertyChanged(nameof(HasAutomation));
            OnPropertyChanged(nameof(AutomationCollapsed));
            OnPropertyChanged(nameof(CollapseGlyph));
        }

        /// <summary>Whether this lane's track is the current selection (highlights the header).</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        /// <summary>Whether a drag is currently hovering this lane (shows a drop highlight).</summary>
        public bool IsDropTarget
        {
            get => _isDropTarget;
            set => SetField(ref _isDropTarget, value);
        }

        /// <summary>Width of the lane content, in pixels (the full arrange width).</summary>
        public double LaneWidth => _metrics.TotalWidth;

        /// <summary>The clips on this lane.</summary>
        public ObservableCollection<ClipViewModel> Clips { get; } = new();

        /// <summary>Re-reads header values after the model changes elsewhere.</summary>
        public void RefreshFromModel()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(ColorKey));
            OnPropertyChanged(nameof(IsMuted));
            OnPropertyChanged(nameof(IsSoloed));
            OnPropertyChanged(nameof(IsArmed));
        }

        /// <summary>Re-reads the live meter level (called on the UI meter timer).</summary>
        public void RaiseMeter() => OnPropertyChanged(nameof(MeterLevel));

        private void OnMetricsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(TimelineMetrics.TotalWidth))
            {
                OnPropertyChanged(nameof(LaneWidth));
            }
        }
    }
}
