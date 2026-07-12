using System;
using System.ComponentModel;
using Ongenet.Core.Models.Audio;
using Ongenet.App.ViewModels.Timeline;

namespace Ongenet.App.ViewModels.Timeline;

/// <summary>
/// View model for a <see cref="PatternClip"/> block on the arrangement timeline.
/// Distinct from regular clips — shows pattern name with a dashed border.
/// </summary>
public sealed class PatternClipViewModel : ViewModelBase
{
    private static readonly string[] PatternColors =
    [
        "CatppuccinMauve", "CatppuccinBlue", "CatppuccinGreen", "CatppuccinPeach",
        "CatppuccinPink", "CatppuccinTeal", "CatppuccinYellow", "CatppuccinRed",
        "CatppuccinLavender", "CatppuccinSky", "CatppuccinSapphire", "CatppuccinMaroon"
    ];

    private readonly TimelineMetrics _metrics;
    private readonly IPatternClipActions? _actions;
    private bool _isSelected;

    public PatternClipViewModel(PatternClip model, Track owner, Pattern? pattern, TimelineMetrics metrics,
        IPatternClipActions? actions = null)
    {
        Model = model;
        Owner = owner;
        Pattern = pattern;
        _metrics = metrics;
        _actions = actions;
        _metrics.PropertyChanged += OnMetricsChanged;
        DeleteCommand = new RelayCommand(() => _actions?.DeletePatternClip(this), () => _actions is not null);
        DuplicateCommand = new RelayCommand(() => _actions?.DuplicatePatternClip(this), () => _actions is not null);
    }

    public PatternClip Model { get; }
    public Track Owner { get; }
    public Pattern? Pattern { get; }

    public string Name => Pattern?.Name ?? "Pattern";

    public string ColorKey => Pattern is null
        ? "CatppuccinOverlay0"
        : PatternColors[Math.Abs(Pattern.ColorIndex) % PatternColors.Length];

    public double Left => _metrics.BeatsToPixels(Model.StartBeat);
    public double Width => _metrics.BeatsToPixels(Model.LengthBeats);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public RelayCommand DeleteCommand { get; }
    public RelayCommand DuplicateCommand { get; }

    public void RefreshFromModel()
    {
        OnPropertyChanged(nameof(Left));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Name));
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
