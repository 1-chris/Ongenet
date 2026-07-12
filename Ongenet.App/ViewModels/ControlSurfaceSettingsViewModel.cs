using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using Ongenet.App.Services;
using Ongenet.App.Services.ControlSurface;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels;

/// <summary>Control surface definition picker, legacy profile fallback, mixer CC learn, and import UI.</summary>
public sealed class ControlSurfaceSettingsViewModel : ViewModelBase
{
    private readonly ControlSurfaceService _controlSurface;
    private readonly IAppSettingsService _settings;
    private readonly ControlSurfaceImportService _import;

    public ControlSurfaceSettingsViewModel(ControlSurfaceService controlSurface, IAppSettingsService settings,
        ControlSurfaceImportService import)
    {
        _controlSurface = controlSurface;
        _settings = settings;
        _import = import;

        DefinitionRows = new ObservableCollection<ControlSurfaceDefinitionRow>();
        LegacyProfiles = new ObservableCollection<ControlSurfaceProfileOption>
        {
            new(null, "Legacy (MCU + HUI only)"),
            new(ControlSurfaceProfile.McuTransport, "MCU Transport"),
            new(ControlSurfaceProfile.McuMixer, "MCU Mixer (8 + bank)"),
            new(ControlSurfaceProfile.HuiTransport, "HUI Transport"),
            new(ControlSurfaceProfile.HuiMixer, "HUI Mixer (8 + bank)"),
            new(ControlSurfaceProfile.Push2, "Ableton Push 2"),
            new(ControlSurfaceProfile.Apc40, "Akai APC40")
        };
        _selectedLegacyProfile = LegacyProfiles.FirstOrDefault(p => p.Profile == _controlSurface.LegacyProfile)
                                 ?? LegacyProfiles[0];
        MappingRows = new ObservableCollection<ControlSurfaceMappingRow>();

        _controlSurface.LearnStateChanged += () => Dispatcher.UIThread.Post(RefreshMappings);
        RefreshDefinitions();
        RefreshMappings();
    }

    public ObservableCollection<ControlSurfaceDefinitionRow> DefinitionRows { get; }
    public ObservableCollection<ControlSurfaceProfileOption> LegacyProfiles { get; }
    public ObservableCollection<ControlSurfaceMappingRow> MappingRows { get; }

    private ControlSurfaceDefinitionRow? _selectedDefinition;
    public ControlSurfaceDefinitionRow? SelectedDefinition
    {
        get => _selectedDefinition;
        set
        {
            if (!SetField(ref _selectedDefinition, value)) return;
            _controlSurface.DefinitionId = value?.Definition?.Id;
            OnPropertyChanged(nameof(ShowLegacyProfile));
            OnPropertyChanged(nameof(ShowMixerMappings));
            RefreshMappings();
        }
    }

    private ControlSurfaceProfileOption _selectedLegacyProfile;
    public ControlSurfaceProfileOption SelectedLegacyProfile
    {
        get => _selectedLegacyProfile;
        set
        {
            if (!SetField(ref _selectedLegacyProfile, value) || value is null) return;
            _controlSurface.LegacyProfile = value.Profile;
            OnPropertyChanged(nameof(ShowMixerMappings));
            RefreshMappings();
        }
    }

    public bool IsEnabled
    {
        get => _controlSurface.IsEnabled;
        set
        {
            if (_controlSurface.IsEnabled == value) return;
            _controlSurface.IsEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool ShowLegacyProfile => SelectedDefinition is null;

    public bool ShowMixerMappings =>
        ShowLegacyProfile && _controlSurface.LegacyProfile is ControlSurfaceProfile.Push2
            or ControlSurfaceProfile.Apc40;

    private string _importReport = "";
    public string ImportReport
    {
        get => _importReport;
        private set => SetField(ref _importReport, value);
    }

    public void LearnMapping(int mixerChannel, string target) => _controlSurface.BeginLearn(mixerChannel, target);

    public void ClearMapping(int mixerChannel, string target)
    {
        if (_controlSurface.LegacyProfile is not { } profile) return;
        var key = profile.ToString();
        _settings.Current.ControlSurfaceMappings.RemoveAll(m =>
            m.Profile == key && m.MixerChannel == mixerChannel && m.Target == target);
        _settings.CaptureAndSave();
        RefreshMappings();
    }

    public void ImportFromFile(string path)
    {
        var result = _import.Import(path);
        ImportReport = string.Join(Environment.NewLine, result.Report.Messages);
        if (result.Success)
        {
            _controlSurface.RescanDefinitions();
            RefreshDefinitions();
            if (result.DefinitionId is { } id)
                SelectedDefinition = DefinitionRows.FirstOrDefault(r => r.Definition?.Id == id);
        }
    }

    private void RefreshDefinitions()
    {
        var selectedId = _settings.Current.ControlSurfaceDefinitionId;
        DefinitionRows.Clear();
        DefinitionRows.Add(new ControlSurfaceDefinitionRow(null, "None (legacy MCU/HUI)"));
        foreach (var def in _controlSurface.AvailableDefinitions)
            DefinitionRows.Add(new ControlSurfaceDefinitionRow(def, def.Name));
        _selectedDefinition = DefinitionRows.FirstOrDefault(r => r.Definition?.Id == selectedId)
                              ?? DefinitionRows.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedDefinition));
        OnPropertyChanged(nameof(ShowLegacyProfile));
        OnPropertyChanged(nameof(ShowMixerMappings));
    }

    private void RefreshMappings()
    {
        MappingRows.Clear();
        if (!ShowMixerMappings) return;

        var profile = _controlSurface.LegacyProfile!.Value.ToString();
        var custom = _settings.Current.ControlSurfaceMappings.Where(m => m.Profile == profile).ToList();

        for (var ch = 1; ch <= 8; ch++)
        {
            foreach (var target in new[] { "Volume", "Pan" })
            {
                var mapping = custom.FirstOrDefault(m => m.MixerChannel == ch && m.Target == target);
                var cc = mapping?.CcNumber ?? (target == "Volume" ? 7 : 10);
                var learning = _controlSurface.LearnTarget is { } learn
                               && learn.MixerChannel == ch
                               && string.Equals(learn.Target, target, StringComparison.OrdinalIgnoreCase);
                MappingRows.Add(new ControlSurfaceMappingRow(ch, target, cc, learning));
            }
        }
    }
}

public sealed class ControlSurfaceDefinitionRow
{
    public ControlSurfaceDefinitionRow(ControlSurfaceDefinition? definition, string label)
    {
        Definition = definition;
        Label = label;
    }

    public ControlSurfaceDefinition? Definition { get; }
    public string Label { get; }
}

public sealed record ControlSurfaceProfileOption(ControlSurfaceProfile? Profile, string Label);

public sealed class ControlSurfaceMappingRow
{
    public ControlSurfaceMappingRow(int mixerChannel, string target, int ccNumber, bool learning)
    {
        MixerChannel = mixerChannel;
        Target = target;
        CcNumber = ccNumber;
        LearnText = learning ? "Listening…" : "Learn";
    }

    public int MixerChannel { get; }
    public string Target { get; }
    public int CcNumber { get; }
    public string Label => $"Ch {MixerChannel} {Target}";
    public string Binding => $"CC {CcNumber}";
    public string LearnText { get; }
}
