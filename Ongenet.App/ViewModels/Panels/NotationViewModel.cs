using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Models.Notation;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Services;

namespace Ongenet.App.ViewModels.Panels;

/// <summary>Notation tab — staff view built from project MIDI clips.</summary>
public sealed class NotationViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private readonly ITransportService _transport;
    private readonly IEventAggregator _events;
    private readonly IHistoryService _history;
    private ScoreDocument? _score;
    private double _pixelsPerBeat = 48;
    private int _transposeSemitones;
    private ScoreLayoutMode _layoutMode = ScoreLayoutMode.FullScore;

    public NotationViewModel(IProjectService project, ITransportService transport, IPlaybackClock clock,
        IEventAggregator events, IHistoryService history)
    {
        _project = project;
        _transport = transport;
        _events = events;
        _history = history;
        ExportMusicXmlCommand = new RelayCommand(() => _ = ExportMusicXmlAsync());
        ImportMusicXmlCommand = new RelayCommand(() => _ = ImportMusicXmlAsync());
        ExportPdfCommand = new RelayCommand(() => _ = ExportPdfAsync(), () => HasScore);
        TransposeUpCommand = new RelayCommand(() => Transpose(1), () => HasScore);
        TransposeDownCommand = new RelayCommand(() => Transpose(-1), () => HasScore);
        ApplyToProjectCommand = new RelayCommand(ApplyToProject, () => HasScore);
        AddTupletCommand = new RelayCommand(AddTuplet, () => HasScore);
        ApplyArticulationCommand = new RelayCommand(ApplyArticulation, () => HasScore);
        ApplyDynamicCommand = new RelayCommand(ApplyDynamic, () => HasScore);
        _project.ProjectChanged += Rebuild;
        clock.Tick += () => OnPropertyChanged(nameof(PlayheadBeats));
        Rebuild();
    }

    public ScoreDocument? Score
    {
        get => _score;
        private set
        {
            if (SetField(ref _score, value))
            {
                OnPropertyChanged(nameof(HasScore));
                TransposeUpCommand.RaiseCanExecuteChanged();
                TransposeDownCommand.RaiseCanExecuteChanged();
                ApplyToProjectCommand.RaiseCanExecuteChanged();
                ExportPdfCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasScore => Score is { Staves.Count: > 0 };

    public double PixelsPerBeat
    {
        get => _pixelsPerBeat;
        set => SetField(ref _pixelsPerBeat, value);
    }

    public double PlayheadBeats => _transport.PlayheadBeats;

    public Array LayoutModes => Enum.GetValues<ScoreLayoutMode>();

    public ScoreLayoutMode LayoutMode
    {
        get => _layoutMode;
        set
        {
            if (!SetField(ref _layoutMode, value)) return;
            if (Score is not null)
            {
                Score.LayoutMode = value;
                OnPropertyChanged(nameof(Score));
            }
        }
    }

    public int TransposeSemitones
    {
        get => _transposeSemitones;
        private set => SetField(ref _transposeSemitones, value);
    }

    public ICommand ExportMusicXmlCommand { get; }
    public ICommand ImportMusicXmlCommand { get; }
    public RelayCommand ExportPdfCommand { get; }
    public RelayCommand TransposeUpCommand { get; }
    public RelayCommand TransposeDownCommand { get; }
    public RelayCommand ApplyToProjectCommand { get; }
    private ScoreArticulation _selectedArticulation = ScoreArticulation.Staccato;
    private ScoreDynamic _selectedDynamic = ScoreDynamic.Mf;

    public Array Articulations => Enum.GetValues<ScoreArticulation>();
    public Array Dynamics => Enum.GetValues<ScoreDynamic>();

    public ScoreArticulation SelectedArticulation
    {
        get => _selectedArticulation;
        set => SetField(ref _selectedArticulation, value);
    }

    public ScoreDynamic SelectedDynamic
    {
        get => _selectedDynamic;
        set => SetField(ref _selectedDynamic, value);
    }

    public int TupletCount => Score?.Tuplets.Count ?? 0;

    public RelayCommand AddTupletCommand { get; }
    public RelayCommand ApplyArticulationCommand { get; }
    public RelayCommand ApplyDynamicCommand { get; }

    public Func<Task<string?>>? PickSavePathAsync { get; set; }
    public Func<Task<string?>>? PickOpenPathAsync { get; set; }
    public Func<Task<string?>>? PickSavePdfPathAsync { get; set; }

    private void Rebuild()
    {
        var beatsPerBar = Math.Max(1, _project.Current.TimeSignature.Numerator);
        Score = ScoreDocumentBuilder.FromProject(_project.Current, beatsPerBar);
        if (Score is not null) Score.LayoutMode = _layoutMode;
        TransposeSemitones = 0;
        OnPropertyChanged(nameof(TupletCount));
        OnPropertyChanged(nameof(HasScore));
    }

    private void AddTuplet()
    {
        if (Score is null) return;
        _history.Capture("Add tuplet");
        var beat = _transport.PlayheadBeats;
        var bar = Math.Max(1, _project.Current.TimeSignature.Numerator);
        Score.Tuplets.Add(new ScoreTuplet
        {
            ActualNotes = 3,
            NormalNotes = 2,
            StartBeat = beat,
            LengthBeats = bar / 2.0
        });
        OnPropertyChanged(nameof(Score));
        OnPropertyChanged(nameof(TupletCount));
    }

    private void ApplyArticulation()
    {
        if (Score is null) return;
        _history.Capture("Apply articulation");
        var beat = _transport.PlayheadBeats;
        foreach (var staff in Score.Staves)
        foreach (var note in staff.Notes.Where(n => Math.Abs(n.StartBeat - beat) < 0.125))
            note.Articulation = SelectedArticulation;
        OnPropertyChanged(nameof(Score));
    }

    private void ApplyDynamic()
    {
        if (Score is null) return;
        _history.Capture("Apply dynamic");
        var beat = _transport.PlayheadBeats;
        foreach (var staff in Score.Staves)
        foreach (var note in staff.Notes.Where(n => Math.Abs(n.StartBeat - beat) < 0.5))
            note.Dynamic = SelectedDynamic;
        OnPropertyChanged(nameof(Score));
    }

    private void Transpose(int semitones)
    {
        if (Score is null) return;
        _history.Capture("Transpose score");
        ScoreDocumentApplier.Transpose(Score, semitones);
        TransposeSemitones += semitones;
        OnPropertyChanged(nameof(Score));
    }

    public void OnScoreEdited()
    {
        if (Score is null) return;
        _history.Capture("Edit notation");
        ApplyToProject();
    }

    private void ApplyToProject()
    {
        if (Score is null) return;
        _history.Capture("Apply notation to project");
        ScoreDocumentApplier.ApplyToProject(_project.Current, Score);
        _events.Publish(new TracksChangedEvent());
        Rebuild();
    }

    private async Task ExportPdfAsync()
    {
        if (Score is null || PickSavePdfPathAsync is null) return;
        var path = await PickSavePdfPathAsync();
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            path += ".pdf";

        Score.Title = _project.Current.Name;
        StaffPdfExporter.Export(Score, path, PixelsPerBeat);
    }

    private async Task ExportMusicXmlAsync()
    {
        if (Score is null || PickSavePathAsync is null) return;
        var path = await PickSavePathAsync();
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!path.EndsWith(".musicxml", StringComparison.OrdinalIgnoreCase))
            path += ".musicxml";

        var beatsPerBar = _project.Current.TimeSignature.Numerator;
        MusicXmlExporter.Export(Score, path, beatsPerBar);
    }

    private async Task ImportMusicXmlAsync()
    {
        if (PickOpenPathAsync is null) return;
        var path = await PickOpenPathAsync();
        if (string.IsNullOrWhiteSpace(path)) return;

        _history.Capture("Import MusicXML");
        MusicXmlImporter.ImportToProject(_project.Current, path);
        _events.Publish(new TracksChangedEvent());
        Rebuild();
    }
}
