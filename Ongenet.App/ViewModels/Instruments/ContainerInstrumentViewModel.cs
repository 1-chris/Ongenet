using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio.Containers;
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

/// <summary>Nested instrument editor for container devices (layers, pads, chain voice, etc.).</summary>
public sealed class ContainerInstrumentViewModel : ViewModelBase
{
    private readonly ContainerInstrumentBase _container;
    private readonly IInstrumentRegistry _instruments;
    private readonly IHistoryService _history;
    private readonly IAudioFileService _audioFiles;
    private readonly IEffectRegistry _effects;
    private readonly ITransportService _transport;
    private readonly IPlaybackClock _clock;
    private readonly Action _notifyChanged;

    public ContainerInstrumentViewModel(
        ContainerInstrumentBase container,
        IInstrumentRegistry instruments,
        IHistoryService history,
        IAudioFileService audioFiles,
        IEffectRegistry effects,
        ITransportService transport,
        IPlaybackClock clock,
        Action notifyChanged)
    {
        _container = container;
        _instruments = instruments;
        _history = history;
        _audioFiles = audioFiles;
        _effects = effects;
        _transport = transport;
        _clock = clock;
        _notifyChanged = notifyChanged;

        AddLayerCommand = new RelayCommand(AddLayer, () => CanAddChild);

        RebuildAddable();
        RebuildChildren();

        if (container is ChainInstrument chain)
        {
            ShowPostEffects = true;
            PostEffects = new EffectChainViewModel(chain.EditablePostEffects, () => { }, notifyChanged,
                effects, history, transport, clock);
        }
    }

    public ObservableCollection<ContainerChildSlotViewModel> Children { get; } = new();

    public bool CanAddChild => _container.CanAddChildren &&
        (_container.MaxChildren is not int max || _container.Children.Count < max);

    public bool IsDrumMachine => _container is DrumMachineInstrument;
    public bool IsLayer => _container is InstrumentLayerInstrument;
    public bool IsSelector => _container is InstrumentSelectorInstrument;
    public bool IsXy => _container is XyInstrument;

    public string ChildrenHeader => _container switch
    {
        DrumMachineInstrument => "Drum pads",
        InstrumentLayerInstrument => "Layers",
        InstrumentSelectorInstrument => "Instruments",
        XyInstrument => "XY corners",
        ChainInstrument => "Instrument",
        ReplacerInstrument => "Trigger instrument",
        _ => "Nested instruments"
    };

    public bool ShowPostEffects { get; private set; }
    public EffectChainViewModel? PostEffects { get; private set; }

    public IReadOnlyList<InstrumentCategoryViewModel> AddableCategories { get; private set; } =
        Array.Empty<InstrumentCategoryViewModel>();

    public RelayCommand AddLayerCommand { get; }

    public double XyX
    {
        get => _container is XyInstrument xy ? xy.X : 0;
        set
        {
            if (_container is not XyInstrument xy || Math.Abs(xy.X - value) < 1e-9) return;
            xy.X = value;
            OnPropertyChanged();
            _notifyChanged();
        }
    }

    public double XyY
    {
        get => _container is XyInstrument xy ? xy.Y : 0;
        set
        {
            if (_container is not XyInstrument xy || Math.Abs(xy.Y - value) < 1e-9) return;
            xy.Y = value;
            OnPropertyChanged();
            _notifyChanged();
        }
    }

    public void BeginXyAdjust() => _history.Capture("XY morph");

    public void RebuildAddable()
    {
        const string containers = "Containers";
        AddableCategories = _instruments.Available
            .Where(i => !string.Equals(i.Category, containers, StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => i.Category)
            .OrderBy(g => g.Key)
            .Select(g => new InstrumentCategoryViewModel(g.Key, g.ToList()))
            .ToList();
        OnPropertyChanged(nameof(AddableCategories));
        foreach (var child in Children)
            child.NotifyReplaceCategoriesChanged();
    }

    public void AddLayer()
    {
        if (!CanAddChild) return;
        if (_instruments.Create(InstrumentRegistry.DefaultInstrumentId) is not { } inst) return;
        _history.Capture("Add container layer");
        _container.AddChild(inst);
        _notifyChanged();
        RebuildChildren();
    }

    public void ReplaceChildAt(int index, string instrumentId)
    {
        if (string.IsNullOrEmpty(instrumentId)) return;
        if (index < 0 || index >= _container.Children.Count) return;
        if (_instruments.Create(instrumentId) is not { } inst) return;
        _history.Capture("Replace container instrument");
        _container.ReplaceChildAt(index, inst);
        _notifyChanged();
        RebuildChildren();
    }

    public void LoadChildPresetFromFile(int index, string presetPath)
    {
        if (string.IsNullOrEmpty(presetPath) || index < 0 || index >= _container.Children.Count) return;
        try
        {
            using var fs = File.OpenRead(presetPath);
            if (PresetFile.Load(fs, _instruments, _effects)?.Instrument is not { } inst) return;
            if (!string.Equals(inst.TypeId, _container.Children[index].Instrument.TypeId, StringComparison.OrdinalIgnoreCase))
                return;
            _history.Capture("Load corner preset");
            _container.ReplaceChildAt(index, inst);
            _notifyChanged();
            RebuildChildren();
        }
        catch
        {
            // Ignore unreadable preset files.
        }
    }

    public void RemoveChildAt(int index)
    {
        if (!_container.CanRemoveChildren) return;
        _history.Capture("Remove container layer");
        _container.RemoveChildAt(index);
        _notifyChanged();
        RebuildChildren();
    }

    private void RebuildChildren()
    {
        Children.Clear();
        for (var i = 0; i < _container.Children.Count; i++)
        {
            var slot = _container.Children[i];
            var label = BuildLabel(i, slot);
            var canRemove = _container.CanRemoveChildren && _container.Children.Count > _container.MinChildren;
            var index = i;
            Children.Add(new ContainerChildSlotViewModel(
                this, label, slot, canRemove, _history, _audioFiles, _instruments, _effects, _transport, _clock,
                _notifyChanged,
                id => ReplaceChildAt(index, id),
                path => LoadChildPresetFromFile(index, path),
                () => RemoveChildAt(index)));
        }

        OnPropertyChanged(nameof(CanAddChild));
        AddLayerCommand.RaiseCanExecuteChanged();
    }

    private string BuildLabel(int index, InstrumentSlot slot)
    {
        if (_container is DrumMachineInstrument dm)
        {
            var note = dm.GetPadMidiNote(index);
            return $"Pad {index + 1} ({MidiNoteName(note)})";
        }

        if (_container is InstrumentSelectorInstrument)
            return $"Instrument {index + 1}";

        if (_container is XyInstrument)
        {
            return index switch
            {
                0 => "Corner 1",
                1 => "Corner 2",
                2 => "Corner 3",
                3 => "Corner 4",
                _ => $"Corner {index + 1}"
            };
        }

        if (_container is ChainInstrument or ReplacerInstrument)
            return slot.Instrument.Name;

        return $"Layer {index + 1}";
    }

    private static string MidiNoteName(int note)
    {
        string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        var n = note % 12;
        if (n < 0) n += 12;
        var octave = note / 12 - 1;
        return $"{names[n]}{octave}";
    }
}
