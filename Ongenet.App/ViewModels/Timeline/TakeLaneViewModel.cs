using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Ongenet.Core.Models.Audio;

namespace Ongenet.App.ViewModels.Timeline
{
    /// <summary>Comping take lane row shown under its parent track in the timeline.</summary>
    public sealed class TakeLaneViewModel : LaneViewModel
    {
        public const double RowHeight = 36.0;

        private readonly TimelineMetrics _metrics;
        private readonly Track _owner;
        private readonly ITakeLaneActions? _actions;
        private readonly Action? _layoutChanged;

        public TakeLaneViewModel(TakeLane model, Track owner, TimelineMetrics metrics,
            ITakeLaneActions? actions = null, Action? layoutChanged = null)
        {
            Model = model;
            _owner = owner;
            _metrics = metrics;
            _actions = actions;
            _layoutChanged = layoutChanged;
            _metrics.PropertyChanged += OnMetricsChanged;

            ToggleExpandCommand = new RelayCommand(() =>
            {
                IsExpanded = !IsExpanded;
                _layoutChanged?.Invoke();
            });
            PromoteTakeCommand = new RelayCommand(() => _actions?.PromoteTake(this), () => _actions is not null && Takes.Count > 0);
            FlattenCompCommand = new RelayCommand(() => _actions?.FlattenComp(this), () => _actions is not null && Takes.Count > 0);
            SplitCompAtPlayheadCommand = new RelayCommand(() => _actions?.SplitCompAtPlayhead(this), () => _actions is not null && Takes.Count > 0);
            ArmForRecordCommand = new RelayCommand(() => IsArmedForRecord = !IsArmedForRecord);
            AddTakeLaneCommand = new RelayCommand(() => _actions?.AddTakeLane(_owner), () => _actions is not null);

            foreach (var take in model.Takes)
                Takes.Add(new TakeViewModel(take, owner, metrics, SelectTake));
        }

        public TakeLane Model { get; }
        public Track OwnerTrack => _owner;
        public TimelineMetrics Metrics => _metrics;

        public override double DefaultHeight => RowHeight;

        public override double Height => IsExpanded ? ResolveHeight(Model.LaneHeight, DefaultHeight) : 0;

        public override bool SupportsResize => IsExpanded;

        /// <summary>Vertical inset for take clips inside the row.</summary>
        public double TakeTopInset => Math.Max(2, Height * 8.0 / DefaultHeight);

        /// <summary>Rendered height of take clips inside the row.</summary>
        public double TakeClipHeight => Math.Max(10, Height - TakeTopInset - 4);

        public override void SetHeight(double height)
        {
            if (!IsExpanded) return;
            height = SnapHeight(height, HalfHeight, DefaultHeight);
            var stored = Math.Abs(height - DefaultHeight) < 0.5 ? 0 : height;
            if (Math.Abs(Model.LaneHeight - stored) < 0.5) return;
            Model.LaneHeight = stored;
            OnPropertyChanged(nameof(Height));
            OnPropertyChanged(nameof(IsCompact));
            OnPropertyChanged(nameof(TakeTopInset));
            OnPropertyChanged(nameof(TakeClipHeight));
        }

        public string Name => Model.Name;

        public bool IsExpanded
        {
            get => Model.IsExpanded;
            set
            {
                if (Model.IsExpanded == value) return;
                Model.IsExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Height));
                OnPropertyChanged(nameof(SupportsResize));
                OnPropertyChanged(nameof(IsCompact));
                OnPropertyChanged(nameof(TakeTopInset));
                OnPropertyChanged(nameof(TakeClipHeight));
                OnPropertyChanged(nameof(CollapseGlyph));
            }
        }

        public string CollapseGlyph => IsExpanded ? "▾" : "▸";

        public string CompSummary
        {
            get
            {
                var selected = Model.Takes.Count(t => t.IsSelected);
                return selected > 0 ? $"Comp: {selected}/{Model.Takes.Count} takes active" : $"{Model.Takes.Count} takes";
            }
        }

        public double LaneWidth => _metrics.TotalWidth;

        public RelayCommand ToggleExpandCommand { get; }
        public RelayCommand PromoteTakeCommand { get; }
        public RelayCommand FlattenCompCommand { get; }
        public RelayCommand SplitCompAtPlayheadCommand { get; }

        public ObservableCollection<TakeViewModel> Takes { get; } = new();

        public void RefreshTakes()
        {
            Takes.Clear();
            foreach (var take in Model.Takes)
                Takes.Add(new TakeViewModel(take, _owner, _metrics, SelectTake));
            PromoteTakeCommand.RaiseCanExecuteChanged();
            FlattenCompCommand.RaiseCanExecuteChanged();
            SplitCompAtPlayheadCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(CompSummary));
        }

        private void SelectTake(TakeViewModel takeVm)
        {
            takeVm.Model.IsSelected = !takeVm.Model.IsSelected;
            foreach (var vm in Takes)
                vm.RefreshSelection();
            OnPropertyChanged(nameof(CompSummary));
        }

        public RelayCommand ArmForRecordCommand { get; }
        public RelayCommand AddTakeLaneCommand { get; }

        public bool IsArmedForRecord
        {
            get => Model.IsArmedForRecord;
            set
            {
                if (Model.IsArmedForRecord == value) return;
                if (value)
                {
                    foreach (var lane in _owner.TakeLanes)
                        lane.IsArmedForRecord = ReferenceEquals(lane, Model);
                    _owner.ActiveTakeLaneId = Model.Id;
                }
                else
                {
                    Model.IsArmedForRecord = false;
                    if (_owner.ActiveTakeLaneId == Model.Id)
                        _owner.ActiveTakeLaneId = null;
                }
                OnPropertyChanged();
            }
        }

        private void OnMetricsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(TimelineMetrics.TotalWidth))
                OnPropertyChanged(nameof(LaneWidth));
        }
    }

    /// <summary>A single comp take shown in a take lane.</summary>
    public sealed class TakeViewModel : ViewModelBase
    {
        private readonly Track _owner;
        private readonly TimelineMetrics _metrics;
        private readonly System.Action<TakeViewModel> _select;

        public TakeViewModel(Take model, Track owner, TimelineMetrics metrics, System.Action<TakeViewModel> select)
        {
            Model = model;
            _owner = owner;
            _metrics = metrics;
            _select = select;
            _metrics.PropertyChanged += OnMetricsChanged;
            SelectCommand = new RelayCommand(() => _select(this));
        }

        public Take Model { get; }

        public RelayCommand SelectCommand { get; }

        public string Label => Model.IsSelected ? $"● {TakeName}" : TakeName;

        public string TakeName
        {
            get
            {
                var clip = _owner.Clips.FirstOrDefault(c => c.Id == Model.ClipId);
                return clip?.Name ?? "Take";
            }
        }

        public bool IsSelected => Model.IsSelected;

        public double Left => _metrics.BeatsToPixels(Model.StartBeat);
        public double Width => _metrics.BeatsToPixels(Model.LengthBeats);

        public void RefreshSelection()
        {
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(Label));
            OnPropertyChanged(nameof(TakeName));
        }

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
