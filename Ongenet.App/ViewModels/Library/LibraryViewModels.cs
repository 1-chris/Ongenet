using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.App.Services;

namespace Ongenet.App.ViewModels.Library;

/// <summary>Effects library: every registered effect, grouped by category, dragged by type id.</summary>
public sealed class EffectsLibraryViewModel : LibraryListViewModel
{
    public EffectsLibraryViewModel(IEffectRegistry effects)
    {
        EmptyHint = "No effects available.";
        SetRoots(effects.Available
            .GroupBy(e => e.Category)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => Folder(g.Key, g
                .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(e => Leaf(e.DisplayName, DragFormats.Effect, e.Id)))));
    }
}

/// <summary>Samples library: audio files found under the configured scan folders, shown as a folder tree
/// (only folders that actually contain scanned samples appear). Double-click previews; drag adds to the
/// timeline (same payload the timeline already accepts).</summary>
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

    private void Refresh() => SetRoots(_scan.Samples.Select(BuildTree));

    // One folder tree per scan folder; folder nodes are created only along the path to an accepted file,
    // so empty/irrelevant subfolders never show up.
    private LibraryNode BuildTree(LibraryGroup group)
    {
        var root = new LibraryNode { Title = group.Name, Icon = "📁", IsFolder = true };
        foreach (var item in group.Items)
        {
            var parent = root;
            var relativeDir = Path.GetDirectoryName(Path.GetRelativePath(group.Root, item.FullPath));
            if (!string.IsNullOrEmpty(relativeDir))
            {
                foreach (var segment in relativeDir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                {
                    if (segment.Length == 0 || segment == ".") continue;
                    parent = GetOrAddFolder(parent, segment);
                }
            }

            parent.Children.Add(Leaf(item.Name, DragFormats.AudioFile, item.FullPath,
                activate: () => _preview.Select(item.FullPath)));
        }

        Sort(root);
        // Reveal the first level when it's small; deeper folders stay collapsed (set by Sort) so opening a
        // huge pack never forces the tree to realise thousands of rows. Users dig in by clicking or via the
        // folder right-click "Expand recursively".
        root.IsExpanded = ShouldAutoExpand(root.Children.Count);
        return root;
    }

    private static LibraryNode GetOrAddFolder(LibraryNode parent, string name)
    {
        foreach (var child in parent.Children)
            if (child.IsFolder && string.Equals(child.Title, name, StringComparison.OrdinalIgnoreCase))
                return child;

        var folder = new LibraryNode { Title = name, Icon = "📁", IsFolder = true };
        parent.Children.Add(folder);
        return folder;
    }

    // Folders first, then alphabetical; recurse into subfolders. Folders start collapsed so a deep pack
    // realises lazily as the user expands.
    private static void Sort(LibraryNode folder)
    {
        var ordered = folder.Children
            .OrderBy(c => c.IsFolder ? 0 : 1)
            .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        folder.Children.Clear();
        foreach (var c in ordered) folder.Children.Add(c);
        folder.IsExpanded = false;
        foreach (var c in ordered) if (c.IsFolder) Sort(c);
    }
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

    private void Refresh() => SetRoots(_scan.SoundFonts.Select(g => Folder(g.Name, g.Items
        .Select(i => Leaf(i.Name, DragFormats.SoundFont, i.FullPath)), icon: "📁")));
}

/// <summary>Instruments library: available instruments (built-ins + discovered plugins) grouped by
/// category, dragged onto the timeline or an instrument track by type id.</summary>
public sealed class InstrumentLibraryViewModel : LibraryListViewModel
{
    // Preferred display order for the instrument-library categories (mirrors the rack's add menu).
    // Plugin formats each get their own group so it's clear what each instrument is.
    private static readonly string[] CategoryOrder = { "Synth", "Sampler", "Drum", "CLAP", "LV2", "VST2", "VST3" };

    private readonly IInstrumentRegistry _registry;

    public InstrumentLibraryViewModel(IInstrumentRegistry registry)
    {
        _registry = registry;
        EmptyHint = "No instruments available.";
        _registry.Changed += () => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    private void Refresh()
    {
        int Rank(string category)
        {
            var i = Array.IndexOf(CategoryOrder, category);
            return i < 0 ? CategoryOrder.Length : i;
        }

        SetRoots(_registry.Available
            .GroupBy(info => info.Category)
            .OrderBy(g => Rank(g.Key)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => Folder(g.Key, g
                .OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(i => Leaf(i.DisplayName, DragFormats.Instrument, i.Id, icon: "🎹")))));
    }
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

    private void Refresh() => SetRoots(_presets.InstrumentPresets.Select(g => Folder(g.Name, g.Items
        .Select(p => Leaf(p.Name, DragFormats.Preset, p.FullPath)))));
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

    private void Refresh() => SetRoots(_presets.EffectPresets.Select(g => Folder(g.Name, g.Items
        .Select(p => Leaf(p.Name, DragFormats.Preset, p.FullPath)))));
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

    private void Refresh() => SetRoots(_presets.ChainPresets.Select(g => Folder(g.Name, g.Items
        .Select(p => Leaf(p.Name, DragFormats.EffectChain, p.FullPath)))));
}

/// <summary>
/// The "Everything" tab: a single overview of every content type (Samples, Soundfonts, Instruments,
/// Effects, Inst Presets, FX Presets, FX Chains — never Files). At rest each type shows a capped sample;
/// typing in the search box filters across all types at once.
/// </summary>
public sealed class EverythingLibraryViewModel : LibraryListViewModel
{
    private readonly ILibraryScanService _scan;
    private readonly IInstrumentRegistry _instruments;
    private readonly IEffectRegistry _effects;
    private readonly IPresetLibrary _presets;
    private readonly AudioPreviewViewModel _preview;

    public EverythingLibraryViewModel(ILibraryScanService scan, IInstrumentRegistry instruments,
        IEffectRegistry effects, IPresetLibrary presets, AudioPreviewViewModel preview)
    {
        _scan = scan;
        _instruments = instruments;
        _effects = effects;
        _presets = presets;
        _preview = preview;
        EmptyHint = "Add content (samples, presets, plugins) to populate the library.";

        _scan.Changed += () => Dispatcher.UIThread.Post(Refresh);
        _instruments.Changed += () => Dispatcher.UIThread.Post(Refresh);
        _presets.Changed += () => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    // Show a small sample of each type at rest; search reveals everything.
    protected override int LeafCap => 8;

    private void Refresh()
    {
        var roots = new List<LibraryNode>();

        var samples = _scan.Samples.SelectMany(g => g.Items)
            .Select(i => Leaf(i.Name, DragFormats.AudioFile, i.FullPath, activate: () => _preview.Select(i.FullPath)))
            .ToList();
        if (samples.Count > 0) roots.Add(Folder("Samples", samples, "📁"));

        var soundFonts = _scan.SoundFonts.SelectMany(g => g.Items)
            .Select(i => Leaf(i.Name, DragFormats.SoundFont, i.FullPath))
            .ToList();
        if (soundFonts.Count > 0) roots.Add(Folder("Soundfonts", soundFonts, "📁"));

        var instruments = _instruments.Available
            .OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(i => Leaf(i.DisplayName, DragFormats.Instrument, i.Id, icon: "🎹"))
            .ToList();
        if (instruments.Count > 0) roots.Add(Folder("Instruments", instruments));

        var effects = _effects.Available
            .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(e => Leaf(e.DisplayName, DragFormats.Effect, e.Id))
            .ToList();
        if (effects.Count > 0) roots.Add(Folder("Effects", effects));

        var instPresets = _presets.InstrumentPresets.SelectMany(g => g.Items)
            .Select(p => Leaf(p.Name, DragFormats.Preset, p.FullPath))
            .ToList();
        if (instPresets.Count > 0) roots.Add(Folder("Inst Presets", instPresets));

        var fxPresets = _presets.EffectPresets.SelectMany(g => g.Items)
            .Select(p => Leaf(p.Name, DragFormats.Preset, p.FullPath))
            .ToList();
        if (fxPresets.Count > 0) roots.Add(Folder("FX Presets", fxPresets));

        var chains = _presets.ChainPresets.SelectMany(g => g.Items)
            .Select(p => Leaf(p.Name, DragFormats.EffectChain, p.FullPath))
            .ToList();
        if (chains.Count > 0) roots.Add(Folder("FX Chains", chains));

        SetRoots(roots);
    }
}
