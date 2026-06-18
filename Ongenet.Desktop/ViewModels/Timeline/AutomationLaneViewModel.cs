using System.ComponentModel;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Desktop.ViewModels.Timeline
{
    /// <summary>
    /// View model for one automation row: an indented curve belonging to the track above it. Wraps a
    /// Core <see cref="AutomationLane"/> (the editable points + its bound control) and the owning track.
    /// The curve control binds directly to <see cref="Lane"/> + <see cref="Metrics"/>; edits made there
    /// call <see cref="CommitEdits"/> so the audio engine re-snapshots the lane without a full row rebuild.
    /// </summary>
    public sealed class AutomationLaneViewModel : LaneViewModel
    {
        /// <summary>Automation rows are shorter than track rows and visibly indented.</summary>
        public const double RowHeight = 56.0;

        private readonly TimelineMetrics _metrics;
        private readonly ITrackActions _actions;
        private int _revision;

        public AutomationLaneViewModel(Track ownerTrack, AutomationLane lane, TimelineMetrics metrics,
            ITrackActions actions)
        {
            OwnerTrack = ownerTrack;
            Lane = lane;
            _metrics = metrics;
            _actions = actions;
            _metrics.PropertyChanged += OnMetricsChanged;

            RemoveCommand = new RelayCommand(() => _actions.RemoveAutomationLane(this));
        }

        /// <summary>The track this automation lane belongs to (its colour/indent come from here).</summary>
        public Track OwnerTrack { get; }

        /// <summary>The underlying automation lane (points, min/max, arm).</summary>
        public AutomationLane Lane { get; }

        public override double Height => RowHeight;

        /// <summary>Shared timeline metrics (time↔pixel mapping for the curve control).</summary>
        public TimelineMetrics Metrics => _metrics;

        /// <summary>The automated parameter's display name.</summary>
        public string Name => Lane.Name;

        /// <summary>Palette key of the owning track, for the indent colour strip.</summary>
        public string ColorKey => OwnerTrack.ColorKey;

        /// <summary>Width of the lane content, in pixels (the full arrange width).</summary>
        public double LaneWidth => _metrics.TotalWidth;

        /// <summary>Whether this lane is armed to capture the control's movements while recording.</summary>
        public bool IsArmed
        {
            get => Lane.IsArmed;
            set
            {
                if (Lane.IsArmed == value) return;
                Lane.IsArmed = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Bumped to force the curve control to repaint (e.g. while a recording fills in points). The
        /// control binds to this so a value change invalidates its visual without rebuilding the row.
        /// </summary>
        public int Revision
        {
            get => _revision;
            private set => SetField(ref _revision, value);
        }

        /// <summary>Forces the bound curve control to redraw.</summary>
        public void BumpRevision() => Revision++;

        public RelayCommand RemoveCommand { get; }

        /// <summary>
        /// Called by the curve control after a point edit: re-snapshots the lane for the audio thread and
        /// repaints. Does not rebuild the rows (the lane set is unchanged), so the control keeps its state.
        /// </summary>
        public void CommitEdits()
        {
            OwnerTrack.CommitAutoLanes();
            BumpRevision();
        }

        private void OnMetricsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(TimelineMetrics.TotalWidth))
            {
                OnPropertyChanged(nameof(LaneWidth));
            }
        }
    }
}
