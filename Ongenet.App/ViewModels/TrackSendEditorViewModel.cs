using System;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels;

/// <summary>Editable view of one <see cref="TrackSend"/> — shared by the mixer strip and track inspector.</summary>
public sealed class TrackSendEditorViewModel : ViewModelBase
{
    private readonly Track _owner;
    private readonly IProjectService _project;
    private readonly IHistoryService? _history;
    private readonly Action _notify;
    private readonly Action<TrackSendEditorViewModel> _onRemove;

    public TrackSendEditorViewModel(Track owner, TrackSend send, IProjectService project,
        IHistoryService? history, Action notify, Action<TrackSendEditorViewModel> onRemove)
    {
        _owner = owner;
        Send = send;
        _project = project;
        _history = history;
        _notify = notify;
        _onRemove = onRemove;

        ReturnTargets = new ObservableCollection<Track>();
        RefreshReturnTargets();

        RemoveCommand = new RelayCommand(RemoveSend);
    }

    public TrackSend Send { get; }

    public ObservableCollection<Track> ReturnTargets { get; }

    public RelayCommand RemoveCommand { get; }

    public string TargetName
    {
        get
        {
            var target = _project.Current.Tracks.FirstOrDefault(t => t.Id == Send.TargetTrackId);
            return target?.Name ?? "(missing)";
        }
    }

    public Track? SelectedTarget
    {
        get => ReturnTargets.FirstOrDefault(t => t.Id == Send.TargetTrackId);
        set
        {
            if (value is null || Send.TargetTrackId == value.Id) return;
            _history?.Capture("Change send target");
            Send.TargetTrackId = value.Id;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedTarget));
            OnPropertyChanged(nameof(TargetName));
            _notify();
        }
    }

    public double Level
    {
        get => Send.Level;
        set
        {
            if (Send.Level == value) return;
            Send.Level = value;
            OnPropertyChanged();
            _notify();
        }
    }

    public bool PreFader
    {
        get => Send.PreFader;
        set
        {
            if (Send.PreFader == value) return;
            Send.PreFader = value;
            OnPropertyChanged();
            _notify();
        }
    }

    public bool Enabled
    {
        get => Send.Enabled;
        set
        {
            if (Send.Enabled == value) return;
            Send.Enabled = value;
            OnPropertyChanged();
            _notify();
        }
    }

    public void RefreshReturnTargets()
    {
        ReturnTargets.Clear();
        foreach (var track in _project.Current.Tracks.Where(t => t.Kind == TrackKind.Return))
            ReturnTargets.Add(track);
        OnPropertyChanged(nameof(SelectedTarget));
        OnPropertyChanged(nameof(TargetName));
    }

    public void RefreshFromModel()
    {
        RefreshReturnTargets();
        OnPropertyChanged(nameof(Level));
        OnPropertyChanged(nameof(PreFader));
        OnPropertyChanged(nameof(Enabled));
    }

    private void RemoveSend()
    {
        _history?.Capture("Remove send");
        _onRemove(this);
    }
}
