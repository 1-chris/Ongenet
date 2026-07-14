using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.ViewModels.Instruments;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments.Sampler;

namespace Ongenet.App.ViewModels;

/// <summary>Multi-layer sampler editor: layers, keyboard coverage, per-key sample list, zone table.</summary>
public sealed class SamplerZoneEditorViewModel : ViewModelBase
{
    private SamplerInstrument? _instrument;
    private int _selectedKey = 60;
    private string _keyDetailSummary = string.Empty;
    private bool _isAddingLayer;

    public ObservableCollection<SamplerLayerRowViewModel> Layers { get; } = new();
    public ObservableCollection<SamplerZoneRowViewModel> Zones { get; } = new();
    public ObservableCollection<string> KeySamples { get; } = new();
    public ObservableCollection<bool> KeyCoverage { get; } = new();
    public ObservableCollection<uint> KeyColors { get; } = new();

    public string Title { get; private set; } = "Sampler";

    public bool HasZones => Zones.Count > 0;
    public int ZoneCount => Zones.Count;
    public int LayerCount => Layers.Count;
    public bool IsAddingLayer
    {
        get => _isAddingLayer;
        private set => SetField(ref _isAddingLayer, value);
    }

    public int SelectedKey
    {
        get => _selectedKey;
        set
        {
            if (!SetField(ref _selectedKey, Math.Clamp(value, 0, 127))) return;
            RefreshKeyDetail();
        }
    }

    public string KeyDetailSummary
    {
        get => _keyDetailSummary;
        private set => SetField(ref _keyDetailSummary, value);
    }

    public RelayCommand ApplyCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public SamplerZoneEditorViewModel()
    {
        ApplyCommand = new RelayCommand(Apply, () => _instrument is not null);
        RefreshCommand = new RelayCommand(() => { if (_instrument is not null) Load(_instrument); });
        for (var i = 0; i < 128; i++)
        {
            KeyCoverage.Add(false);
            KeyColors.Add(0);
        }
    }

    public void PreviewNoteOn(int key) => _instrument?.NoteOn(Math.Clamp(key, 0, 127), 0.8f);
    public void PreviewNoteOff(int key) => _instrument?.NoteOff(Math.Clamp(key, 0, 127));

    /// <summary>Appends a library / disk soundfont as a new stacked layer.</summary>
    public async Task AddLayerFromPathAsync(string path)
    {
        if (_instrument is null || _isAddingLayer || string.IsNullOrWhiteSpace(path)) return;
        var loader = App.ServiceProvider?.GetService<ISamplerLoadService>();
        if (loader is null) return;

        IsAddingLayer = true;
        try
        {
            var result = await Task.Run(() => loader.Load(path));
            if (result is null) return;
            _instrument.AddLayer(result);
            RebuildFromInstrument();
        }
        finally
        {
            IsAddingLayer = false;
        }
    }

    public void Load(SamplerInstrument instrument)
    {
        _instrument = instrument;
        Title = $"Sampler — {instrument.Name}";
        RebuildFromInstrument();
        OnPropertyChanged(nameof(Title));
        ApplyCommand.RaiseCanExecuteChanged();
    }

    public void RebuildFromInstrument()
    {
        if (_instrument is null) return;
        Layers.Clear();
        foreach (var layer in _instrument.Layers)
            Layers.Add(new SamplerLayerRowViewModel(layer, _instrument, () => RebuildFromInstrument()));

        Zones.Clear();
        foreach (var region in _instrument.Regions)
            Zones.Add(new SamplerZoneRowViewModel(region));

        for (var k = 0; k < 128; k++)
        {
            var hits = _instrument.Regions.Where(r => k >= r.LoKey && k <= r.HiKey).ToList();
            KeyCoverage[k] = hits.Count > 0;
            KeyColors[k] = BlendLayerColors(hits);
        }
        OnPropertyChanged(nameof(KeyCoverage));
        OnPropertyChanged(nameof(KeyColors));
        OnPropertyChanged(nameof(ZoneCount));
        OnPropertyChanged(nameof(LayerCount));
        OnPropertyChanged(nameof(HasZones));
        RefreshKeyDetail();
    }

    private static uint BlendLayerColors(IReadOnlyList<SamplerRegion> hits)
    {
        if (hits.Count == 0) return 0;
        var distinct = hits.Select(r => r.LayerColorArgb).Where(c => c != 0).Distinct().ToList();
        if (distinct.Count == 0) return 0;
        if (distinct.Count == 1) return distinct[0];
        long r = 0, g = 0, b = 0;
        foreach (var c in distinct)
        {
            r += (c >> 16) & 0xFF;
            g += (c >> 8) & 0xFF;
            b += c & 0xFF;
        }
        var n = distinct.Count;
        return 0xFF000000u | ((uint)(r / n) << 16) | ((uint)(g / n) << 8) | (uint)(b / n);
    }

    private void RefreshKeyDetail()
    {
        KeySamples.Clear();
        if (_instrument is null)
        {
            KeyDetailSummary = string.Empty;
            return;
        }

        var matches = _instrument.Regions.Where(r => r.Matches(SelectedKey, 100)).ToList();
        // Also include other velocities briefly — show any region covering the key
        if (matches.Count == 0)
            matches = _instrument.Regions.Where(r => SelectedKey >= r.LoKey && SelectedKey <= r.HiKey).ToList();

        var layerNames = _instrument.Layers.ToDictionary(l => l.Id, l => l.Name);
        foreach (var r in matches)
        {
            layerNames.TryGetValue(r.LayerId, out var layerName);
            layerName ??= "?";
            var sample = r.Sample.DisplayName;
            KeySamples.Add($"{layerName}: {sample}  vel {r.LoVel}-{r.HiVel}  root {r.PitchKeycenter}");
        }

        KeyDetailSummary = matches.Count == 0
            ? $"Key {SelectedKey}: uncovered"
            : $"Key {SelectedKey}: {matches.Count} region(s)";
    }

    private void Apply()
    {
        if (_instrument is null) return;
        var updated = Zones.Select(z => z.ToRegion()).ToArray();
        _instrument.ReplaceRegions(updated);
        RebuildFromInstrument();
    }

    public void RemoveLayer(Guid id)
    {
        _instrument?.RemoveLayer(id);
        RebuildFromInstrument();
    }
}

public sealed class SamplerLayerRowViewModel : ViewModelBase
{
    private readonly SamplerLayer _layer;
    private readonly SamplerInstrument _instrument;
    private readonly Action _refresh;
    private bool _enabled;
    private int _maskLo;
    private int _maskHi;
    private int _selectedSf2Preset;
    private IBrush _colorBrush;

    public SamplerLayerRowViewModel(SamplerLayer layer, SamplerInstrument instrument, Action refresh)
    {
        _layer = layer;
        _instrument = instrument;
        _refresh = refresh;
        _enabled = layer.Enabled;
        _maskLo = layer.KeyMaskLo < 0 ? 0 : layer.KeyMaskLo;
        _maskHi = layer.KeyMaskHi < 0 ? 127 : layer.KeyMaskHi;
        _selectedSf2Preset = layer.PresetIndex;
        _colorBrush = new SolidColorBrush(Color.FromUInt32(layer.ColorArgb));
        PaletteColors = SamplerLayer.Palette
            .Select(argb => new SamplerLayerColorChip(argb, this))
            .ToList();
        RemoveCommand = new RelayCommand(() =>
        {
            _instrument.RemoveLayer(layer.Id);
            _refresh();
        });
        RandomizeColorCommand = new RelayCommand(() => SetColor(SamplerLayer.CreateRandomColor()));
    }

    public Guid Id => _layer.Id;
    public string Name => _layer.Name;
    public string FileName => Path.GetFileName(_layer.SourcePath);
    public string FormatLabel => _layer.Format == SamplerFormat.Sf2 ? "SF2" : "SFZ";
    public bool HasSf2Presets => _layer.Format == SamplerFormat.Sf2 && _layer.Presets.Count > 0;
    public IReadOnlyList<string> Sf2PresetNames =>
        InstrumentSlotViewModel.FormatSf2PresetsHierarchical(_layer.Presets);
    public IReadOnlyList<SamplerLayerColorChip> PaletteColors { get; }
    public IBrush ColorBrush
    {
        get => _colorBrush;
        private set => SetField(ref _colorBrush!, value);
    }

    public void SetColor(uint argb)
    {
        if (!_instrument.SetLayerColor(_layer.Id, argb)) return;
        ColorBrush = new SolidColorBrush(Color.FromUInt32(argb));
        _refresh();
    }

    public int SelectedSf2Preset
    {
        get => _selectedSf2Preset;
        set
        {
            if (!SetField(ref _selectedSf2Preset, value) || value < 0) return;
            if (_instrument.LoadLayerPreset(_layer.Id, value) is not null)
                _refresh();
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!SetField(ref _enabled, value)) return;
            _instrument.SetLayerEnabled(_layer.Id, value);
            _refresh();
        }
    }

    public int MaskLo
    {
        get => _maskLo;
        set
        {
            if (!SetField(ref _maskLo, Math.Clamp(value, 0, 127))) return;
            _instrument.SetLayerKeyMask(_layer.Id, _maskLo, _maskHi);
            _refresh();
        }
    }

    public int MaskHi
    {
        get => _maskHi;
        set
        {
            if (!SetField(ref _maskHi, Math.Clamp(value, 0, 127))) return;
            _instrument.SetLayerKeyMask(_layer.Id, _maskLo, _maskHi);
            _refresh();
        }
    }

    public RelayCommand RemoveCommand { get; }
    public RelayCommand RandomizeColorCommand { get; }
}

public sealed class SamplerLayerColorChip
{
    private readonly SamplerLayerRowViewModel _owner;

    public SamplerLayerColorChip(uint argb, SamplerLayerRowViewModel owner)
    {
        Argb = argb;
        _owner = owner;
        Brush = new SolidColorBrush(Color.FromUInt32(argb));
        PickCommand = new RelayCommand(() => _owner.SetColor(Argb));
    }

    public uint Argb { get; }
    public IBrush Brush { get; }
    public RelayCommand PickCommand { get; }
}

public sealed class SamplerZoneRowViewModel : ViewModelBase
{
    public static readonly string[] FilterModeNames = { "LP", "BP", "HP", "Notch" };

    private readonly SamplerRegion _source;
    private int _loKey, _hiKey, _loVel, _hiVel, _rootKey, _seqLength, _seqPosition, _roundRobinKey;
    private double _gain;

    public SamplerZoneRowViewModel(SamplerRegion region)
    {
        _source = region;
        _loKey = region.LoKey;
        _hiKey = region.HiKey;
        _loVel = region.LoVel;
        _hiVel = region.HiVel;
        _rootKey = region.PitchKeycenter;
        _gain = region.Gain;
        _seqLength = region.SeqLength;
        _seqPosition = region.SeqPosition;
        _roundRobinKey = region.RoundRobinKey;
        _ampEg = region.AmpEg;
        _filEg = region.FilEg;
        _pitchEg = region.PitchEg;
        _hasFilter = region.HasFilter;
        _filterMode = region.FilterMode;
        _cutoff = region.Cutoff > 0 ? region.Cutoff : 1000;
        _filterQ = region.FilterQ > 0 ? region.FilterQ : 0.707;
        _pan = region.Pan;
        SampleName = region.Sample.DisplayName;
        LayerId = region.LayerId;
        LayerColorArgb = region.LayerColorArgb;
    }

    public string SampleName { get; }
    public Guid LayerId { get; }
    public uint LayerColorArgb { get; }

    public int LoKey
    {
        get => _loKey;
        set => SetField(ref _loKey, Math.Clamp(value, 0, 127));
    }

    public int HiKey
    {
        get => _hiKey;
        set => SetField(ref _hiKey, Math.Clamp(value, 0, 127));
    }

    public int LoVel
    {
        get => _loVel;
        set => SetField(ref _loVel, Math.Clamp(value, 0, 127));
    }

    public int HiVel
    {
        get => _hiVel;
        set => SetField(ref _hiVel, Math.Clamp(value, 0, 127));
    }

    public int RootKey
    {
        get => _rootKey;
        set => SetField(ref _rootKey, Math.Clamp(value, 0, 127));
    }

    public double Gain
    {
        get => _gain;
        set => SetField(ref _gain, Math.Max(0, value));
    }

    public int SeqLength
    {
        get => _seqLength;
        set => SetField(ref _seqLength, Math.Max(1, value));
    }

    public int SeqPosition
    {
        get => _seqPosition;
        set => SetField(ref _seqPosition, Math.Max(1, value));
    }

    public int RoundRobinKey
    {
        get => _roundRobinKey;
        set => SetField(ref _roundRobinKey, value);
    }

    public IReadOnlyList<string> FilterModeOptions => FilterModeNames;

    public SamplerEgSpec AmpEg => _ampEg;
    public SamplerEgSpec FilEg => _filEg;
    public SamplerEgSpec PitchEg => _pitchEg;

    public double AmpAttack
    {
        get => _ampEg.Attack;
        set { _ampEg = _ampEg with { Attack = Math.Max(0, value) }; OnPropertyChanged(); }
    }

    public double AmpDecay
    {
        get => _ampEg.Decay;
        set { _ampEg = _ampEg with { Decay = Math.Max(0, value) }; OnPropertyChanged(); }
    }

    public double AmpSustain
    {
        get => _ampEg.Sustain;
        set { _ampEg = _ampEg with { Sustain = Math.Clamp(value, 0, 1) }; OnPropertyChanged(); }
    }

    public double AmpRelease
    {
        get => _ampEg.Release;
        set { _ampEg = _ampEg with { Release = Math.Max(0, value) }; OnPropertyChanged(); }
    }

    public bool HasFilter
    {
        get => _hasFilter;
        set => SetField(ref _hasFilter, value);
    }

    public double Cutoff
    {
        get => _cutoff;
        set => SetField(ref _cutoff, Math.Max(20, value));
    }

    public double FilterQ
    {
        get => _filterQ;
        set => SetField(ref _filterQ, Math.Max(0.05, value));
    }

    public int FilterModeIndex
    {
        get => _filterMode switch
        {
            FilterMode.BandPass => 1,
            FilterMode.HighPass => 2,
            FilterMode.Notch => 3,
            _ => 0
        };
        set
        {
            var mode = value switch
            {
                1 => FilterMode.BandPass,
                2 => FilterMode.HighPass,
                3 => FilterMode.Notch,
                _ => FilterMode.LowPass
            };
            if (_filterMode == mode) return;
            _filterMode = mode;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilterModeIndex));
        }
    }

    public double Pan
    {
        get => _pan;
        set => SetField(ref _pan, Math.Clamp(value, -1, 1));
    }

    private SamplerEgSpec _ampEg, _filEg, _pitchEg;
    private bool _hasFilter;
    private FilterMode _filterMode;
    private double _cutoff, _filterQ, _pan;

    public SamplerRegion ToRegion() => _source.Copy(
        loKey: LoKey,
        hiKey: HiKey,
        loVel: LoVel,
        hiVel: HiVel,
        pitchKeycenter: RootKey,
        gain: Gain,
        pan: Pan,
        seqLength: SeqLength,
        seqPosition: SeqPosition,
        roundRobinKey: RoundRobinKey,
        ampEg: AmpEg,
        filEg: FilEg,
        pitchEg: PitchEg,
        hasFilter: HasFilter,
        filterMode: _filterMode,
        cutoff: Cutoff,
        filterQ: FilterQ);
}
