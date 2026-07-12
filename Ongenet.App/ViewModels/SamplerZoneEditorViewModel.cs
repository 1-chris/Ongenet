using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.Core.Audio.Instruments.Sampler;

namespace Ongenet.App.ViewModels;

/// <summary>Editable SFZ/sampler zone list for the zone editor window.</summary>
public sealed class SamplerZoneEditorViewModel : ViewModelBase
{
    private SamplerInstrument? _instrument;
    private SamplerRegion[] _original = Array.Empty<SamplerRegion>();

    public ObservableCollection<SamplerZoneRowViewModel> Zones { get; } = new();

    public string Title { get; private set; } = "Sampler Zones";

    public bool HasZones => Zones.Count > 0;
    public int ZoneCount => Zones.Count;

    public RelayCommand ApplyCommand { get; }

    public SamplerZoneEditorViewModel()
    {
        ApplyCommand = new RelayCommand(Apply, () => _instrument is not null && HasZones);
    }

    public void Load(SamplerInstrument instrument)
    {
        _instrument = instrument;
        _original = instrument.Regions.ToArray();
        Title = $"Zones — {instrument.Name}";
        Zones.Clear();
        for (var i = 0; i < _original.Length; i++)
            Zones.Add(new SamplerZoneRowViewModel(_original[i]));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ZoneCount));
        OnPropertyChanged(nameof(HasZones));
        ApplyCommand.RaiseCanExecuteChanged();
    }

    private void Apply()
    {
        if (_instrument is null) return;
        var updated = Zones.Select(z => z.ToRegion()).ToArray();
        _instrument.ReplaceRegions(updated);
        _original = updated;
    }
}

public sealed class SamplerZoneRowViewModel : ViewModelBase
{
    private readonly SamplerRegion _source;
    private int _loKey, _hiKey, _loVel, _hiVel, _rootKey, _seqLength, _seqPosition;
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
        SampleName = region.Sample.StreamPath ?? "(embedded)";
    }

    public string SampleName { get; }

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

    public SamplerRegion ToRegion() => new()
    {
        Sample = _source.Sample,
        LoKey = LoKey,
        HiKey = HiKey,
        LoVel = LoVel,
        HiVel = HiVel,
        PitchKeycenter = RootKey,
        KeytrackSemisPerKey = _source.KeytrackSemisPerKey,
        TransposeSemis = _source.TransposeSemis,
        TuneCents = _source.TuneCents,
        Gain = Gain,
        Pan = _source.Pan,
        AmpVeltrack = _source.AmpVeltrack,
        AmpEg = _source.AmpEg,
        Offset = _source.Offset,
        End = _source.End,
        LoopMode = _source.LoopMode,
        LoopStart = _source.LoopStart,
        LoopEnd = _source.LoopEnd,
        Reverse = _source.Reverse,
        SeqLength = SeqLength,
        SeqPosition = SeqPosition,
        RoundRobinKey = _source.RoundRobinKey,
        Group = _source.Group,
        OffBy = _source.OffBy,
        HasFilter = _source.HasFilter,
        FilterMode = _source.FilterMode,
        Cutoff = _source.Cutoff,
        FilterQ = _source.FilterQ,
        FilKeytrack = _source.FilKeytrack,
        FilKeycenter = _source.FilKeycenter,
        FilVeltrack = _source.FilVeltrack,
        HasFilEg = _source.HasFilEg,
        FilEgDepth = _source.FilEgDepth,
        FilEg = _source.FilEg,
        HasFilLfo = _source.HasFilLfo,
        FilLfoFreq = _source.FilLfoFreq,
        FilLfoDepth = _source.FilLfoDepth,
        FilLfoDelay = _source.FilLfoDelay,
        HasAmpLfo = _source.HasAmpLfo,
        AmpLfoFreq = _source.AmpLfoFreq,
        AmpLfoDepthDb = _source.AmpLfoDepthDb,
        AmpLfoDelay = _source.AmpLfoDelay,
        HasPitchLfo = _source.HasPitchLfo,
        PitchLfoFreq = _source.PitchLfoFreq,
        PitchLfoDepth = _source.PitchLfoDepth,
        PitchLfoDelay = _source.PitchLfoDelay,
        HasPitchEg = _source.HasPitchEg,
        PitchEgDepth = _source.PitchEgDepth,
        PitchEg = _source.PitchEg,
        EqBands = _source.EqBands,
        Trigger = _source.Trigger,
        SwLast = _source.SwLast,
        SwLoKey = _source.SwLoKey,
        SwHiKey = _source.SwHiKey,
        SwDefault = _source.SwDefault,
        BendUpCents = _source.BendUpCents,
        BendDownCents = _source.BendDownCents,
        CutoffCc = _source.CutoffCc
    };
}
