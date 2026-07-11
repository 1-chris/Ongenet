using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Ongenet.App.ViewModels;
using Ongenet.Core.Models.Audio;

namespace Ongenet.App.ViewModels.Timeline
{
    /// <summary>
    /// Virtual aggregate clip shown on a group track lane, spanning all descendant clips.
    /// Does not correspond to a domain <see cref="Core.Models.Audio.Clip"/>.
    /// </summary>
    public sealed class GroupClipSummaryViewModel : ViewModelBase
    {
        private readonly TimelineMetrics _metrics;
        private readonly double _startBeat;
        private readonly double _lengthBeats;
        private bool _isRendering;
        private double _renderProgress;

        public GroupClipSummaryViewModel(
            double startBeat,
            double lengthBeats,
            Track ownerGroup,
            IReadOnlyList<ClipViewModel> underlying,
            IReadOnlyList<GroupChildClipBarViewModel> childBars,
            TimelineMetrics metrics,
            TimelineViewModel actions)
        {
            _startBeat = startBeat;
            _lengthBeats = lengthBeats;
            OwnerGroup = ownerGroup;
            UnderlyingClips = underlying;
            _metrics = metrics;
            _metrics.PropertyChanged += OnMetricsChanged;

            ChildBars = new ObservableCollection<GroupChildClipBarViewModel>(childBars);
            IsAllAudio = underlying.Count > 0 && underlying.All(c => c.IsAudio);

            DuplicateCommand = new RelayCommand(() => actions.DuplicateGroupSummary(this));
            DeleteCommand = new RelayCommand(() => actions.DeleteGroupSummary(this));
            ReverseCommand = new RelayCommand(() => actions.ReverseGroupSummary(this), () => IsAllAudio);
            RenderToNewTrackCommand = new RelayCommand(
                () => _ = actions.RenderGroupSummaryToNewTrackAsync(this),
                () => !actions.IsRenderingClip);
        }

        public Track OwnerGroup { get; }

        public ObservableCollection<GroupChildClipBarViewModel> ChildBars { get; }

        public IReadOnlyList<ClipViewModel> UnderlyingClips { get; }

        public bool IsAllAudio { get; }

        public double StartBeat => _startBeat;

        public double LengthBeats => _lengthBeats;

        public double Left => _metrics.BeatsToPixels(_startBeat);

        public double Width => _metrics.BeatsToPixels(_lengthBeats);

        /// <summary>True while this group summary is being rendered to a new track.</summary>
        public bool IsRendering
        {
            get => _isRendering;
            private set => SetField(ref _isRendering, value);
        }

        /// <summary>Width in pixels of the render-progress sweep.</summary>
        public double RenderProgressWidth => Width * _renderProgress;

        /// <summary>Updates the render-progress overlay; values are clamped to 0..1.</summary>
        public void SetRenderProgress(double progress)
        {
            _renderProgress = System.Math.Clamp(progress, 0.0, 1.0);
            IsRendering = _renderProgress < 1.0;
            OnPropertyChanged(nameof(RenderProgressWidth));
        }

        public RelayCommand DuplicateCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand ReverseCommand { get; }
        public RelayCommand RenderToNewTrackCommand { get; }

        private void OnMetricsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(TimelineMetrics.PixelsPerBeat))
            {
                OnPropertyChanged(nameof(Left));
                OnPropertyChanged(nameof(Width));
                OnPropertyChanged(nameof(RenderProgressWidth));
            }
        }
    }
}
