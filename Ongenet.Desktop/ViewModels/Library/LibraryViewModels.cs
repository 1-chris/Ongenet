using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Ongenet.Core.Audio.Effects;
using Ongenet.Desktop.Services;

namespace Ongenet.Desktop.ViewModels.Library;

/// <summary>Effects library: every registered effect, grouped by category, dragged by type id.</summary>
public sealed class EffectsLibraryViewModel : LibraryListViewModel
{
    public EffectsLibraryViewModel(IEffectRegistry effects)
    {
        EmptyHint = "No effects available.";
        Replace(effects.Available
            .GroupBy(e => e.Category)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new LibrarySection(g.Key, g
                .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(e => new LibraryEntry
                {
                    Title = e.DisplayName,
                    DragFormat = DragFormats.Effect,
                    DragPayload = e.Id
                }).ToList())));
    }
}

/// <summary>Samples library: audio files found under the configured scan folders. Double-click previews;
/// drag adds to the timeline (same payload the timeline already accepts).</summary>
public sealed class SampleLibraryViewModel : LibraryListViewModel
{
    private readonly ILibraryScanService _scan;
    private readonly AudioPreviewViewModel _preview;

    public SampleLibraryViewModel(ILibraryScanService scan, AudioPreviewViewModel preview)
    {
        _scan = scan;
        _preview = preview;
        EmptyHint = "Add sample folders in Settings → Library.";
        _scan.Changed += () => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    private void Refresh() => Replace(_scan.Samples.Select(g => new LibrarySection(g.Name, g.Items
        .Select(i => new LibraryEntry
        {
            Title = i.Name,
            DragFormat = DragFormats.AudioFile,
            DragPayload = i.FullPath,
            Activate = () => _preview.Select(i.FullPath)
        }).ToList())));
}

/// <summary>Soundfonts library: .sf2/.sfz files found under the configured scan folders.</summary>
public sealed class SoundFontLibraryViewModel : LibraryListViewModel
{
    private readonly ILibraryScanService _scan;

    public SoundFontLibraryViewModel(ILibraryScanService scan)
    {
        _scan = scan;
        EmptyHint = "Add sound-font folders in Settings → Library.";
        _scan.Changed += () => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    private void Refresh() => Replace(_scan.SoundFonts.Select(g => new LibrarySection(g.Name, g.Items
        .Select(i => new LibraryEntry
        {
            Title = i.Name,
            DragFormat = DragFormats.SoundFont,
            DragPayload = i.FullPath
        }).ToList())));
}

/// <summary>Instrument presets (factory + user), grouped by instrument, dragged by file path.</summary>
public sealed class InstrumentPresetLibraryViewModel : LibraryListViewModel
{
    private readonly IPresetLibrary _presets;

    public InstrumentPresetLibraryViewModel(IPresetLibrary presets)
    {
        _presets = presets;
        EmptyHint = "Save an instrument as a preset to see it here.";
        _presets.Changed += () => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    private void Refresh() => Replace(_presets.InstrumentPresets.Select(g => new LibrarySection(g.Name, g.Items
        .Select(p => new LibraryEntry
        {
            Title = p.Name,
            DragFormat = DragFormats.Preset,
            DragPayload = p.FullPath
        }).ToList())));
}

/// <summary>Effect presets (user), grouped by effect, dragged by file path.</summary>
public sealed class EffectPresetLibraryViewModel : LibraryListViewModel
{
    private readonly IPresetLibrary _presets;

    public EffectPresetLibraryViewModel(IPresetLibrary presets)
    {
        _presets = presets;
        EmptyHint = "Save an effect as a preset to see it here.";
        _presets.Changed += () => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    private void Refresh() => Replace(_presets.EffectPresets.Select(g => new LibrarySection(g.Name, g.Items
        .Select(p => new LibraryEntry
        {
            Title = p.Name,
            DragFormat = DragFormats.Preset,
            DragPayload = p.FullPath
        }).ToList())));
}

/// <summary>FX-chain presets (whole effect chains saved by the user), dragged onto a chain to append.</summary>
public sealed class EffectChainPresetLibraryViewModel : LibraryListViewModel
{
    private readonly IPresetLibrary _presets;

    public EffectChainPresetLibraryViewModel(IPresetLibrary presets)
    {
        _presets = presets;
        EmptyHint = "Save an effect chain as a preset to see it here.";
        _presets.Changed += () => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    private void Refresh() => Replace(_presets.ChainPresets.Select(g => new LibrarySection(g.Name, g.Items
        .Select(p => new LibraryEntry
        {
            Title = p.Name,
            DragFormat = DragFormats.EffectChain,
            DragPayload = p.FullPath
        }).ToList())));
}
