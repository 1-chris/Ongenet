using System;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.App.Services;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels;

public sealed class TempoMapViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private readonly ITransportService _transport;
    private readonly IHistoryService _history;

    public TempoMapViewModel(IProjectService project, ITransportService transport, IHistoryService history)
    {
        _project = project;
        _transport = transport;
        _history = history;
        AddAtPlayheadCommand = new RelayCommand(AddAtPlayhead);
        project.ProjectChanged += Rebuild;
        Rebuild();
    }

    public ObservableCollection<TempoPointRow> Points { get; } = new();
    public RelayCommand AddAtPlayheadCommand { get; }

    private AutomationLane? FindLane() => _project.Current.Master?.AutoLanes
        .FirstOrDefault(l => l.Binding?.Kind == AutomationTargetKind.Tempo);

    private AutomationLane EnsureLane()
    {
        var master = _project.Current.Master ?? throw new InvalidOperationException("Project has no master track.");
        var lane = FindLane();
        if (lane is not null) return lane;
        lane = new AutomationLane(ProjectAutomationTargets.Tempo(_project.Current))
        {
            Binding = new AutomationBinding(AutomationTargetKind.Tempo, -1, -1),
            IsArmed = true
        };
        master.AutoLanes.Add(lane);
        master.CommitAutoLanes();
        return lane;
    }

    private void AddAtPlayhead()
    {
        _history.Capture("Add tempo point");
        var lane = EnsureLane();
        lane.AddPoint(new AutomationPoint(_transport.PlayheadBeats, _project.Current.Tempo.BeatsPerMinute));
        _project.Current.Master?.CommitAutoLanes();
        Rebuild();
    }

    private void Rebuild()
    {
        Points.Clear();
        if (FindLane() is not { } lane) return;
        foreach (var point in lane.Points)
            Points.Add(new TempoPointRow(point, () =>
            {
                lane.Sort();
                _project.Current.Master?.CommitAutoLanes();
            }));
    }
}

public sealed class TempoPointRow : ViewModelBase
{
    private readonly AutomationPoint _point;
    private readonly Action _changed;
    public TempoPointRow(AutomationPoint point, Action changed) { _point = point; _changed = changed; }
    public double Beat { get => _point.Beat; set { _point.Beat = Math.Max(0, value); _changed(); OnPropertyChanged(); } }
    public double Bpm { get => _point.Value; set { _point.Value = Math.Clamp(value, 1, 999); _changed(); OnPropertyChanged(); } }
}
