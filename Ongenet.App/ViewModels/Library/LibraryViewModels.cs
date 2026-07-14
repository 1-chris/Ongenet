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
    public EffectsLibraryViewModel(IEffectRegistry effects, ILibraryOrganizationService org)
    {
        EmptyHint = "No effects available.";
        AttachOrganization(org, LibraryItemKeys.Effect, LibraryItemKeys.Folder);
        SetRoots(effects.Available
            .GroupBy(e => e.Category)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => Folder(g.Key, g
                .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(e => Leaf(e.DisplayName, DragFormats.Effect, e.Id,
                    itemKey: LibraryItemKeys.EffectKey(e.Id))),
                itemKey: LibraryItemKeys.NamedFolderKey("effects", g.Key))));
    }
}

/// <summary>Samples library: audio files found under the configured scan folders, shown as a folder tree
/// (only folders that actually contain scanned samples appear). Double-click previews; drag adds to the
/// timeline (same payload the timeline already accepts).</summary>
public sealed class SampleLibraryViewModel : LibraryListViewModel
{
    private readonly ILibraryScanService _scan;
    private readonly AudioPreviewViewModel _preview;

    public SampleLibraryViewModel(ILibraryScanService scan, AudioPreviewViewModel preview,
        ILibraryOrganizationService org)
    {
        _scan = scan;
        _preview = preview;
        EmptyHint = "Add sample folders in Settings → Library.";
        AttachOrganization(org, LibraryItemKeys.File, LibraryItemKeys.Folder);
        _scan.Changed += () => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    private void Refresh() => SetRoots(_scan.Samples.Select(BuildTree));

    private LibraryNode BuildTree(LibraryGroup group)
    {
        var root = new LibraryNode
        {
            Title = group.Name,
            Icon = "📁",
            IsFolder = true,
            ItemKey = LibraryItemKeys.FolderKey(group.Root)
        };
        foreach (var item in group.Items)
        {
            var parent = root;
            var relativeDir = Path.GetDirectoryName(Path.GetRelativePath(group.Root, item.FullPath));
            var dirPath = group.Root;
            if (!string.IsNullOrEmpty(relativeDir))
            {
                foreach (var segment in relativeDir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                {
                    if (segment.Length == 0 || segment == ".") continue;
                    dirPath = Path.Combine(dirPath, segment);
                    parent = GetOrAddChildFolder(parent, segment, dirPath);
                }
            }

            parent.Children.Add(Leaf(item.Name, DragFormats.AudioFile, item.FullPath,
                activate: () => _preview.Select(item.FullPath),
                itemKey: LibraryItemKeys.FileKey(item.FullPath)));
        }

        SortPathTree(root);
        root.IsExpanded = ShouldAutoExpand(root.Children.Count);
        return root;
    }
}

/// <summary>Soundfonts library: .sf2/.sfz files found under the configured scan folders.</summary>
public sealed class SoundFontLibraryViewModel : LibraryListViewModel
{
    private readonly ILibraryScanService _scan;

    public SoundFontLibraryViewModel(ILibraryScanService scan, ILibraryOrganizationService org)
    {
        _scan = scan;
        EmptyHint = "Add sound-font folders in Settings → Library.";
        AttachOrganization(org, LibraryItemKeys.SoundFont, LibraryItemKeys.Folder);
        _scan.Changed += () => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    private void Refresh() => SetRoots(_scan.SoundFonts.Select(g => BuildSoundFontTree(g)));

    /// <summary>Nest soundfonts by relative directory under the scan root (Factory → Sf2 → GM → …).</summary>
    internal static LibraryNode BuildSoundFontTree(LibraryGroup group)
    {
        var root = new LibraryNode
        {
            Title = group.Name,
            Icon = "📁",
            IsFolder = true,
            ItemKey = LibraryItemKeys.FolderKey(group.Root)
        };
        foreach (var item in group.Items)
        {
            var parent = root;
            var relativeDir = Path.GetDirectoryName(Path.GetRelativePath(group.Root, item.FullPath));
            var dirPath = group.Root;
            if (!string.IsNullOrEmpty(relativeDir))
            {
                foreach (var segment in relativeDir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                {
                    if (segment.Length == 0 || segment == ".") continue;
                    dirPath = Path.Combine(dirPath, segment);
                    parent = GetOrAddChildFolder(parent, segment, dirPath);
                }
            }

            parent.Children.Add(Leaf(item.Name, DragFormats.SoundFont, item.FullPath,
                itemKey: LibraryItemKeys.SoundFontKey(item.FullPath)));
        }

        SortPathTree(root);
        root.IsExpanded = ShouldAutoExpand(root.Children.Count);
        return root;
    }
}

/// <summary>Instruments library: available instruments (built-ins + discovered plugins) grouped by
/// category, dragged onto the timeline or an instrument track by type id.</summary>
public sealed class InstrumentLibraryViewModel : LibraryListViewModel
{
    private static readonly string[] CategoryOrder = { "Synth", "Sampler", "Drum", "CLAP", "LV2", "VST2", "VST3", "AU" };

    private readonly IInstrumentRegistry _registry;

    public InstrumentLibraryViewModel(IInstrumentRegistry registry, ILibraryOrganizationService org)
    {
        _registry = registry;
        EmptyHint = "No instruments available.";
        AttachOrganization(org, LibraryItemKeys.Instrument, LibraryItemKeys.Folder);
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
                .Select(i => Leaf(i.DisplayName, DragFormats.Instrument, i.Id, icon: "🎹",
                    itemKey: LibraryItemKeys.InstrumentKey(i.Id))),
                itemKey: LibraryItemKeys.NamedFolderKey("instruments", g.Key))));
    }
}

/// <summary>Instrument presets (factory + user), grouped by instrument, dragged by file path.</summary>
public sealed class InstrumentPresetLibraryViewModel : LibraryListViewModel
{
    private readonly IPresetLibrary _presets;

    public InstrumentPresetLibraryViewModel(IPresetLibrary presets, ILibraryOrganizationService org)
    {
        _presets = presets;
        EmptyHint = "Save an instrument as a preset to see it here.";
        AttachOrganization(org, LibraryItemKeys.Preset, LibraryItemKeys.Folder);
        _presets.Changed += () => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    private void Refresh() => SetRoots(_presets.InstrumentPresets.Select(g => Folder(g.Name, g.Items
        .Select(p => Leaf(p.Name, DragFormats.Preset, p.FullPath,
            itemKey: LibraryItemKeys.PresetKey(p.FullPath))),
        itemKey: LibraryItemKeys.NamedFolderKey("inst-presets", g.Name))));
}

/// <summary>Effect presets (user), grouped by effect, dragged by file path.</summary>
public sealed class EffectPresetLibraryViewModel : LibraryListViewModel
{
    private readonly IPresetLibrary _presets;

    public EffectPresetLibraryViewModel(IPresetLibrary presets, ILibraryOrganizationService org)
    {
        _presets = presets;
        EmptyHint = "Save an effect as a preset to see it here.";
        AttachOrganization(org, LibraryItemKeys.Preset, LibraryItemKeys.Folder);
        _presets.Changed += () => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    private void Refresh() => SetRoots(_presets.EffectPresets.Select(g => Folder(g.Name, g.Items
        .Select(p => Leaf(p.Name, DragFormats.Preset, p.FullPath,
            itemKey: LibraryItemKeys.PresetKey(p.FullPath))),
        itemKey: LibraryItemKeys.NamedFolderKey("fx-presets", g.Name))));
}

/// <summary>FX-chain presets (whole effect chains saved by the user), dragged onto a chain to append.</summary>
public sealed class EffectChainPresetLibraryViewModel : LibraryListViewModel
{
    private readonly IPresetLibrary _presets;

    public EffectChainPresetLibraryViewModel(IPresetLibrary presets, ILibraryOrganizationService org)
    {
        _presets = presets;
        EmptyHint = "Save an effect chain as a preset to see it here.";
        AttachOrganization(org, LibraryItemKeys.EffectChain, LibraryItemKeys.Folder);
        _presets.Changed += () => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    private void Refresh() => SetRoots(_presets.ChainPresets.Select(g => Folder(g.Name, g.Items
        .Select(p => Leaf(p.Name, DragFormats.EffectChain, p.FullPath,
            itemKey: LibraryItemKeys.EffectChainKey(p.FullPath))),
        itemKey: LibraryItemKeys.NamedFolderKey("fx-chains", g.Name))));
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
        IEffectRegistry effects, IPresetLibrary presets, AudioPreviewViewModel preview,
        ILibraryOrganizationService org)
    {
        _scan = scan;
        _instruments = instruments;
        _effects = effects;
        _presets = presets;
        _preview = preview;
        EmptyHint = "Add content (samples, presets, plugins) to populate the library.";
        AttachOrganization(org);

        _scan.Changed += () => Dispatcher.UIThread.Post(Refresh);
        _instruments.Changed += () => Dispatcher.UIThread.Post(Refresh);
        _presets.Changed += () => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    protected override int LeafCap => 8;

    private void Refresh()
    {
        var roots = new List<LibraryNode>();

        var samples = _scan.Samples.SelectMany(g => g.Items)
            .Select(i => Leaf(i.Name, DragFormats.AudioFile, i.FullPath,
                activate: () => _preview.Select(i.FullPath),
                itemKey: LibraryItemKeys.FileKey(i.FullPath)))
            .ToList();
        if (samples.Count > 0) roots.Add(Folder("Samples", samples, "📁",
            itemKey: LibraryItemKeys.NamedFolderKey("everything", "Samples")));

        var soundFontGroups = _scan.SoundFonts.Select(SoundFontLibraryViewModel.BuildSoundFontTree).ToList();
        if (soundFontGroups.Count > 0)
            roots.Add(Folder("Soundfonts", soundFontGroups, "📁",
                itemKey: LibraryItemKeys.NamedFolderKey("everything", "Soundfonts")));
        var instruments = _instruments.Available
            .OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(i => Leaf(i.DisplayName, DragFormats.Instrument, i.Id, icon: "🎹",
                itemKey: LibraryItemKeys.InstrumentKey(i.Id)))
            .ToList();
        if (instruments.Count > 0) roots.Add(Folder("Instruments", instruments,
            itemKey: LibraryItemKeys.NamedFolderKey("everything", "Instruments")));

        var effects = _effects.Available
            .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(e => Leaf(e.DisplayName, DragFormats.Effect, e.Id,
                itemKey: LibraryItemKeys.EffectKey(e.Id)))
            .ToList();
        if (effects.Count > 0) roots.Add(Folder("Effects", effects,
            itemKey: LibraryItemKeys.NamedFolderKey("everything", "Effects")));

        var instPresets = _presets.InstrumentPresets.SelectMany(g => g.Items)
            .Select(p => Leaf(p.Name, DragFormats.Preset, p.FullPath,
                itemKey: LibraryItemKeys.PresetKey(p.FullPath)))
            .ToList();
        if (instPresets.Count > 0) roots.Add(Folder("Inst Presets", instPresets,
            itemKey: LibraryItemKeys.NamedFolderKey("everything", "Inst Presets")));

        var fxPresets = _presets.EffectPresets.SelectMany(g => g.Items)
            .Select(p => Leaf(p.Name, DragFormats.Preset, p.FullPath,
                itemKey: LibraryItemKeys.PresetKey(p.FullPath)))
            .ToList();
        if (fxPresets.Count > 0) roots.Add(Folder("FX Presets", fxPresets,
            itemKey: LibraryItemKeys.NamedFolderKey("everything", "FX Presets")));

        var chains = _presets.ChainPresets.SelectMany(g => g.Items)
            .Select(p => Leaf(p.Name, DragFormats.EffectChain, p.FullPath,
                itemKey: LibraryItemKeys.EffectChainKey(p.FullPath)))
            .ToList();
        if (chains.Count > 0) roots.Add(Folder("FX Chains", chains,
            itemKey: LibraryItemKeys.NamedFolderKey("everything", "FX Chains")));

        SetRoots(roots);
    }
}
