using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Services;
using Ongenet.App.ViewModels;
using Ongenet.App.ViewModels.Effects;

namespace Ongenet.App.ViewModels.Instruments;

/// <summary>One nested instrument slot inside a container device (layer, pad, chain voice, etc.).</summary>
public sealed class ContainerChildSlotViewModel : ViewModelBase
{
    private readonly ContainerInstrumentViewModel _parent;
    private readonly InstrumentSlot _slot;
    private readonly IHistoryService _history;
    private readonly IAudioFileService _audioFiles;
    private readonly IInstrumentRegistry _instruments;
    private readonly IEffectRegistry _effects;
    private readonly Action<string> _replaceInstrument;
    private readonly Action<string> _loadPresetFromFile;
    private readonly Action _remove;
    private readonly Action _notifyChanged;

    private int _selectedPreset = -1;
    private PresetItem? _selectedLibraryPreset;

    public ContainerChildSlotViewModel(
        ContainerInstrumentViewModel parent,
        string label,
        InstrumentSlot slot,
        bool canRemove,
        IHistoryService history,
        IAudioFileService audioFiles,
        IInstrumentRegistry instruments,
        IEffectRegistry effects,
        ITransportService transport,
        IPlaybackClock clock,
        Action notifyChanged,
        Action<string> replaceInstrument,
        Action<string> loadPresetFromFile,
        Action remove)
    {
        _parent = parent;
        Label = label;
        _slot = slot;
        CanRemove = canRemove;
        _history = history;
        _audioFiles = audioFiles;
        _instruments = instruments;
        _effects = effects;
        _replaceInstrument = replaceInstrument;
        _loadPresetFromFile = loadPresetFromFile;
        _remove = remove;
        _notifyChanged = notifyChanged;

        LibraryPresets = BuildLibraryPresets(slot.Instrument);

        Effects = new EffectChainViewModel(slot.Effects, slot.CommitEffects, notifyChanged,
            effects, history, transport, clock);

        RemoveCommand = new RelayCommand(() => _remove(), () => CanRemove);
        ReplaceCommand = new RelayCommand<string>(id => _replaceInstrument(id));
        ToggleEnabledCommand = new RelayCommand(() => IsEnabled = !IsEnabled);

        RebuildParameters();
    }

    public string Label { get; }
    public IReadOnlyList<InstrumentCategoryViewModel> ReplaceCategories => _parent.AddableCategories;

    public void NotifyReplaceCategoriesChanged() => OnPropertyChanged(nameof(ReplaceCategories));
    public InstrumentSlot Slot => _slot;
    public IInstrument Instrument => _slot.Instrument;
    public string InstrumentName => Instrument.Name;
    public bool CanRemove { get; }
    public bool IsCompact => _parent.IsXy;

    public bool HasParameters => ParameterGroups.Count > 0;
    public bool IsPresetProvider => Instrument is IPresetProvider;
    public bool HasLibraryPresets => LibraryPresets.Count > 0;
    public bool ShowCompactEdit => IsCompact;

    public IReadOnlyList<PresetItem> LibraryPresets { get; }

    public EffectChainViewModel Effects { get; }

    public RelayCommand RemoveCommand { get; }
    public RelayCommand<string> ReplaceCommand { get; }
    public RelayCommand ToggleEnabledCommand { get; }

    public bool IsSampler => Instrument is ISampleHost;

    public string SampleName => Instrument is ISampleHost host ? host.SampleName ?? "(no sample)" : string.Empty;

    public ObservableCollection<ParameterViewModel> Parameters { get; } = new();
    public ObservableCollection<ParameterGroupViewModel> ParameterGroups { get; } = new();

    public IReadOnlyList<string> PresetNames =>
        Instrument is IPresetProvider provider ? provider.PresetNames : Array.Empty<string>();

    public int SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (_selectedPreset == value) return;
            _selectedPreset = value;
            OnPropertyChanged();
            if (value < 0 || Instrument is not IPresetProvider provider) return;
            _history.Capture("Load corner preset");
            provider.LoadPreset(value);
            RebuildParameters();
            _notifyChanged();
        }
    }

    public PresetItem? SelectedLibraryPreset
    {
        get => _selectedLibraryPreset;
        set
        {
            if (_selectedLibraryPreset == value) return;
            _selectedLibraryPreset = value;
            OnPropertyChanged();
            if (value is null) return;
            _loadPresetFromFile(value.FullPath);
        }
    }

    public bool IsEnabled
    {
        get => _slot.Enabled;
        set
        {
            if (_slot.Enabled == value) return;
            _history.Capture(value ? "Enable layer" : "Disable layer");
            _slot.Enabled = value;
            if (!value) Instrument.AllNotesOff();
            OnPropertyChanged();
            _notifyChanged();
        }
    }

    public void LoadSampleFromPath(string path)
    {
        if (Instrument is not ISampleHost host) return;
        var loaded = _audioFiles.Load(path);
        if (loaded is null) return;
        _history.Capture("Load sample");
        host.LoadSample(loaded.Samples, Path.GetFileName(path));
        OnPropertyChanged(nameof(SampleName));
        _notifyChanged();
    }

    public void RebuildParameters()
    {
        Parameters.Clear();
        ParameterGroups.Clear();

        var order = new List<string>();
        var byGroup = new Dictionary<string, List<ParameterViewModel>>();

        foreach (var p in Instrument.Parameters)
        {
            var vm = ParameterViewModel.Create(p);
            Parameters.Add(vm);
            var group = string.IsNullOrWhiteSpace(p.Group) ? "Main" : p.Group!;
            if (!byGroup.TryGetValue(group, out var list))
            {
                list = new List<ParameterViewModel>();
                byGroup[group] = list;
                order.Add(group);
            }
            list.Add(vm);
        }

        foreach (var key in order)
            ParameterGroups.Add(new ParameterGroupViewModel(key, byGroup[key]));

        OnPropertyChanged(nameof(HasParameters));
        OnPropertyChanged(nameof(IsPresetProvider));
        OnPropertyChanged(nameof(PresetNames));
    }

    public void NotifyInstrumentChanged()
    {
        OnPropertyChanged(nameof(InstrumentName));
        OnPropertyChanged(nameof(IsSampler));
        OnPropertyChanged(nameof(SampleName));
        OnPropertyChanged(nameof(LibraryPresets));
        OnPropertyChanged(nameof(HasLibraryPresets));
        _selectedPreset = -1;
        _selectedLibraryPreset = null;
        OnPropertyChanged(nameof(SelectedPreset));
        OnPropertyChanged(nameof(SelectedLibraryPreset));
        RebuildParameters();
    }

    private static IReadOnlyList<PresetItem> BuildLibraryPresets(IInstrument instrument)
    {
        var lib = App.ServiceProvider?.GetService(typeof(IPresetLibrary)) as IPresetLibrary;
        if (lib is null) return Array.Empty<PresetItem>();
        return lib.InstrumentPresets
            .SelectMany(g => g.Items)
            .Where(i => string.Equals(i.TypeId, instrument.TypeId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
