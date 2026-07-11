using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Ongenet.App.ViewModels;

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

        public GroupClipSummaryViewModel(
            double startBeat,
            double lengthBeats,
            IReadOnlyList<ClipViewModel> underlying,
            IReadOnlyList<GroupChildClipBarViewModel> childBars,
            TimelineMetrics metrics,
            TimelineViewModel actions)
        {
            _startBeat = startBeat;
            _lengthBeats = lengthBeats;
            UnderlyingClips = underlying;
            _metrics = metrics;
            _metrics.PropertyChanged += OnMetricsChanged;

            ChildBars = new ObservableCollection<GroupChildClipBarViewModel>(childBars);
            IsAllAudio = underlying.Count > 0 && underlying.All(c => c.IsAudio);

            DuplicateCommand = new RelayCommand(() => actions.DuplicateGroupSummary(this));
            DeleteCommand = new RelayCommand(() => actions.DeleteGroupSummary(this));
            ReverseCommand = new RelayCommand(() => actions.ReverseGroupSummary(this), () => IsAllAudio);
        }

        public ObservableCollection<GroupChildClipBarViewModel> ChildBars { get; }

        public IReadOnlyList<ClipViewModel> UnderlyingClips { get; }

        public bool IsAllAudio { get; }

        public double Left => _metrics.BeatsToPixels(_startBeat);

        public double Width => _metrics.BeatsToPixels(_lengthBeats);

        public RelayCommand DuplicateCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand ReverseCommand { get; }

        private void OnMetricsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(TimelineMetrics.PixelsPerBeat))
            {
                OnPropertyChanged(nameof(Left));
                OnPropertyChanged(nameof(Width));
            }
        }
    }
}
