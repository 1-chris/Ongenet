using System.Collections.ObjectModel;
using System.ComponentModel;
using Ongenet.Core.Models.Audio;

namespace Ongenet.App.ViewModels.Timeline
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
            ToggleCollapseCommand = new RelayCommand(() =>
            {
                if (IsGroup) _actions.ToggleGroup(this);
                else _actions.ToggleAutomation(this);
            });
            GroupTracksCommand = new RelayCommand(() => _actions.GroupSelectedTracks());
            DeleteGroupKeepChildrenCommand = new RelayCommand(() => _actions.DeleteGroupKeepChildren(this));
            DeleteGroupAndChildrenCommand = new RelayCommand(() => _actions.DeleteGroupAndChildren(this));
            DetachFromGroupCommand = new RelayCommand(() => _actions.DetachFromGroup(this));

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

        /// <summary>Header chevron: collapses a group's subtree, or a normal track's automation rows.</summary>
        public RelayCommand ToggleCollapseCommand { get; }

        /// <summary>Groups the current multi-selection under a new group bus.</summary>
        public RelayCommand GroupTracksCommand { get; }

        /// <summary>Removes this group but keeps its tracks (moves them up a level).</summary>
        public RelayCommand DeleteGroupKeepChildrenCommand { get; }

        /// <summary>Removes this group and every track nested inside it.</summary>
        public RelayCommand DeleteGroupAndChildrenCommand { get; }

        /// <summary>Moves this track one level out of its group.</summary>
        public RelayCommand DetachFromGroupCommand { get; }

        /// <summary>True when this track is nested inside a group (shows "Detach from group").</summary>
        public bool IsInGroup => Model.ParentId is not null;

        /// <summary>True for a group bus.</summary>
        public bool IsGroup => Model.Kind == TrackKind.Group;

        /// <summary>True for the master bus.</summary>
        public bool IsMaster => Model.Kind == TrackKind.Master;

        /// <summary>True for any bus (group or master).</summary>
        public bool IsBus => Model.IsBus;

        /// <summary>The master can't be deleted or duplicated.</summary>
        public bool CanDeleteOrDuplicate => Model.Kind != TrackKind.Master;

        /// <summary>Nesting depth (0 = top level), set by the timeline when rows are rebuilt.</summary>
        private int _indentLevel;
        public int IndentLevel
        {
            get => _indentLevel;
            set
            {
                if (SetField(ref _indentLevel, value)) OnPropertyChanged(nameof(IndentWidth));
            }
        }

        /// <summary>Left gutter indent in pixels for this row's header, from its nesting depth.</summary>
        public double IndentWidth => _indentLevel * 16.0;

        private System.Collections.Generic.IReadOnlyList<LaneGutterBar> _gutterBars = System.Array.Empty<LaneGutterBar>();

        /// <summary>The stacked colour rails for this row (ancestor group colours + this track's own), set by the timeline.</summary>
        public System.Collections.Generic.IReadOnlyList<LaneGutterBar> GutterBars
        {
            get => _gutterBars;
            set => SetField(ref _gutterBars, value);
        }

        /// <summary>True when the track has one or more automation lanes (shows the collapse chevron).</summary>
        public bool HasAutomation => Model.AutoLanes.Count > 0;

        /// <summary>Whether the header chevron is shown (groups always; normal tracks only with automation).</summary>
        public bool ShowCollapse => IsGroup || HasAutomation;

        /// <summary>Whether this group's nested rows are hidden.</summary>
        public bool GroupCollapsed
        {
            get => Model.GroupCollapsed;
            set
            {
                if (Model.GroupCollapsed == value) return;
                Model.GroupCollapsed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CollapseGlyph));
            }
        }

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
        public string CollapseGlyph => (IsGroup ? Model.GroupCollapsed : Model.AutomationCollapsed) ? "▸" : "▾";

        /// <summary>Re-reads automation-related header state after lanes are added/removed/collapsed.</summary>
        public void RefreshAutomationState()
        {
            OnPropertyChanged(nameof(HasAutomation));
            OnPropertyChanged(nameof(ShowCollapse));
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
