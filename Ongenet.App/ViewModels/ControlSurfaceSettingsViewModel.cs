using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using Ongenet.App.Services;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels;

/// <summary>Control surface profile picker and mixer CC learn UI for the Settings window.</summary>
public sealed class ControlSurfaceSettingsViewModel : ViewModelBase
{
    private readonly ControlSurfaceService _controlSurface;
    private readonly IAppSettingsService _settings;

    public ControlSurfaceSettingsViewModel(ControlSurfaceService controlSurface, IAppSettingsService settings)
    {
        _controlSurface = controlSurface;
        _settings = settings;
        Profiles = new ObservableCollection<ControlSurfaceProfileOption>
        {
            new(null, "Legacy (MCU + Launchpad)"),
            new(ControlSurfaceProfile.McuTransport, "MCU Transport"),
            new(ControlSurfaceProfile.McuMixer, "MCU Mixer (8 + bank)"),
            new(ControlSurfaceProfile.LaunchpadSession, "Launchpad Session"),
            new(ControlSurfaceProfile.HuiTransport, "HUI Transport"),
            new(ControlSurfaceProfile.HuiMixer, "HUI Mixer (8 + bank)"),
            new(ControlSurfaceProfile.Push2, "Ableton Push 2"),
            new(ControlSurfaceProfile.Apc40, "Akai APC40")
        };
        _selectedProfile = Profiles.FirstOrDefault(p => p.Profile == _controlSurface.Profile) ?? Profiles[0];
        MappingRows = new ObservableCollection<ControlSurfaceMappingRow>();

        _controlSurface.LearnStateChanged += () => Dispatcher.UIThread.Post(RefreshMappings);
        RefreshMappings();
    }

    public ObservableCollection<ControlSurfaceProfileOption> Profiles { get; }
    public ObservableCollection<ControlSurfaceMappingRow> MappingRows { get; }

    private ControlSurfaceProfileOption _selectedProfile;
    public ControlSurfaceProfileOption SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetField(ref _selectedProfile, value) || value is null) return;
            _controlSurface.Profile = value.Profile;
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

    public bool ShowMixerMappings =>
        _controlSurface.Profile is ControlSurfaceProfile.Push2 or ControlSurfaceProfile.Apc40;

    public void LearnMapping(int mixerChannel, string target) => _controlSurface.BeginLearn(mixerChannel, target);

    public void ClearMapping(int mixerChannel, string target)
    {
        if (_controlSurface.Profile is not { } profile) return;
        var key = profile.ToString();
        _settings.Current.ControlSurfaceMappings.RemoveAll(m =>
            m.Profile == key && m.MixerChannel == mixerChannel && m.Target == target);
        _settings.CaptureAndSave();
        RefreshMappings();
    }

    private void RefreshMappings()
    {
        MappingRows.Clear();
        if (!ShowMixerMappings) return;

        var profile = _controlSurface.Profile!.Value.ToString();
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
