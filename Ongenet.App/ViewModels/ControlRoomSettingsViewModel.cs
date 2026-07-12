using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels;

/// <summary>Control Room monitor/cue profiles for broadcast and film workflows.</summary>
public sealed class ControlRoomSettingsViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private ControlRoomProfile? _selected;

    public ControlRoomSettingsViewModel(IProjectService project)
    {
        _project = project;
        AddProfileCommand = new RelayCommand(AddProfile);
        RemoveProfileCommand = new RelayCommand(RemoveProfile, () => Selected is not null);
        _project.ProjectChanged += Rebuild;
        Rebuild();
    }

    public ObservableCollection<ControlRoomProfile> Profiles { get; } = new();
    public RelayCommand AddProfileCommand { get; }
    public RelayCommand RemoveProfileCommand { get; }

    public ControlRoomProfile? Selected
    {
        get => _selected;
        set
        {
            if (!SetField(ref _selected, value)) return;
            RemoveProfileCommand.RaiseCanExecuteChanged();
        }
    }

    private void Rebuild()
    {
        Profiles.Clear();
        foreach (var p in _project.Current.ControlRoomProfiles)
            Profiles.Add(p);
        if (Profiles.Count == 0)
            AddProfile();
        Selected ??= Profiles.FirstOrDefault();
    }

    private void AddProfile()
    {
        var profile = new ControlRoomProfile { Name = $"Profile {Profiles.Count + 1}" };
        _project.Current.ControlRoomProfiles.Add(profile);
        Profiles.Add(profile);
        Selected = profile;
    }

    private void RemoveProfile()
    {
        if (Selected is not { } profile) return;
        _project.Current.ControlRoomProfiles.Remove(profile);
        Profiles.Remove(profile);
        Selected = Profiles.FirstOrDefault();
        RemoveProfileCommand.RaiseCanExecuteChanged();
    }
}
