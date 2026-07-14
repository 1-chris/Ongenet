using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.Services;
using Ongenet.App.ViewModels;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Field.Nodes;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Persistence;

namespace Ongenet.App.ViewModels.Field;

/// <summary>
/// View-model backing the Field editor (the node-graph canvas, palette and inspector). It wraps the live
/// <see cref="FieldGraph"/> of a Field instrument or effect plus a recompile callback, and exposes the node
/// palette, the built-in preset list, and the currently selected node's parameters. Structural edits made by
/// the canvas call <see cref="NotifyStructureChanged"/> to recompile the audio graph and repaint.
/// </summary>
public sealed class FieldEditorViewModel : ViewModelBase
{
    private readonly Action _recompile;
    private readonly IReadOnlyList<string> _presetNames;
    private readonly Action<int> _loadPreset;
    private readonly IHistoryService? _history;
    private readonly Func<IInstrument?>? _instrumentHost;
    private readonly Func<FieldEffect?>? _effectHost;
    private FieldNode? _selectedNode;
    private Guid? _navigationGroupId;
    private string _patchName = string.Empty;
    private string _saveDefinitionName = string.Empty;
    private string _editorMode = "Graph";
    private string _statusMessage = string.Empty;

    public FieldEditorViewModel(FieldGraph graph, IFieldNodeRegistry registry, Action recompile,
        IReadOnlyList<string> presetNames, Action<int> loadPreset, Func<CompiledGraph?> compiled, bool isInstrument,
        Func<IInstrument?>? instrumentHost = null, Func<FieldEffect?>? effectHost = null)
    {
        Graph = graph;
        Registry = registry;
        _recompile = recompile;
        _presetNames = presetNames;
        _loadPreset = loadPreset;
        CompiledAccessor = compiled;
        IsInstrument = isInstrument;
        _history = App.ServiceProvider?.GetService<IHistoryService>();
        _instrumentHost = instrumentHost;
        _effectHost = effectHost;
        BuildPalette();

        var surface = InstrumentHost?.Surface.Clone()
                      ?? EffectHost?.Surface.Clone()
                      ?? new FieldSurfaceDefinition();
        Surface = new FieldSurfaceViewModel(graph, surface, PushSurfaceToHost);
        SaveDefinitionName = InstrumentHost?.Name ?? EffectHost?.Name ?? (isInstrument ? "My Instrument" : "My Effect");
    }

    public FieldGraph Graph { get; }
    public IFieldNodeRegistry Registry { get; }
    public Func<CompiledGraph?> CompiledAccessor { get; }
    public bool IsInstrument { get; }
    public FieldSurfaceViewModel Surface { get; }

    public FieldInstrument? InstrumentHost => _instrumentHost?.Invoke() as FieldInstrument;
    public FieldEffect? EffectHost => _effectHost?.Invoke();

    /// <summary>"Graph" or "Interface".</summary>
    public string EditorMode
    {
        get => _editorMode;
        set
        {
            if (!SetField(ref _editorMode, value)) return;
            var design = string.Equals(value, "Interface", StringComparison.OrdinalIgnoreCase);
            Surface.IsDesignMode = design;
            OnPropertyChanged(nameof(IsGraphMode));
            OnPropertyChanged(nameof(IsInterfaceMode));
        }
    }

    public bool IsGraphMode => !string.Equals(_editorMode, "Interface", StringComparison.OrdinalIgnoreCase);
    public bool IsInterfaceMode => string.Equals(_editorMode, "Interface", StringComparison.OrdinalIgnoreCase);

    public string SaveDefinitionName
    {
        get => _saveDefinitionName;
        set => SetField(ref _saveDefinitionName, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool HasUserDefinition => InstrumentHost?.IsUserDefinition == true || EffectHost?.IsUserDefinition == true;

    public string SaveDefinitionButtonText
        => IsInstrument
            ? (HasUserDefinition ? "Update Instrument" : "Save as Instrument")
            : (HasUserDefinition ? "Update Effect" : "Save as Effect");

    /// <summary>Raised when the graph's structure changes so the canvas repaints.</summary>
    public event Action? StructureChanged;

    /// <summary>Grouped node palette (category → available node types).</summary>
    public ObservableCollection<FieldPaletteGroup> PaletteGroups { get; } = new();

    /// <summary>Inspector parameters for <see cref="SelectedNode"/>.</summary>
    public ObservableCollection<ParameterViewModel> SelectedParameters { get; } = new();

    public IReadOnlyList<string> PresetNames => _presetNames;

    /// <summary>When set, the canvas shows only nodes inside this group (sub-patch navigation).</summary>
    public Guid? NavigationGroupId
    {
        get => _navigationGroupId;
        private set
        {
            if (!SetField(ref _navigationGroupId, value)) return;
            OnPropertyChanged(nameof(CanExitGroup));
            OnPropertyChanged(nameof(BreadcrumbText));
            SelectedNode = null;
            RaiseStructureChanged();
        }
    }

    public bool CanExitGroup => NavigationGroupId.HasValue;

    public string BreadcrumbText
    {
        get
        {
            if (NavigationGroupId is not { } gid) return "Root";
            var group = Graph.Groups.FirstOrDefault(g => g.Id == gid);
            return group is null ? "Root" : $"Root › {group.Name}";
        }
    }

    public string PatchName
    {
        get => _patchName;
        set => SetField(ref _patchName, value);
    }

    private int _selectedPreset = -1;
    public int SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (!SetField(ref _selectedPreset, value)) return;
            if (value >= 0 && value < _presetNames.Count)
            {
                CaptureHistory("Load built-in Field patch");
                _loadPreset(value);
                ReloadSurfaceFromHost();
                SelectedNode = null;
                RaiseStructureChanged();
            }
        }
    }

    /// <summary>
    /// Replaces the designer document from the live host. Used after a built-in patch changes both
    /// the graph and its supplied editable interface.
    /// </summary>
    public void ReloadSurfaceFromHost()
    {
        var surface = InstrumentHost?.Surface.Clone()
                      ?? EffectHost?.Surface.Clone()
                      ?? new FieldSurfaceDefinition();
        Surface.ReplaceSurface(surface);
        OnPropertyChanged(nameof(HasUserDefinition));
        OnPropertyChanged(nameof(SaveDefinitionButtonText));
    }

    public FieldNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetField(ref _selectedNode, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedNodeName));
            OnPropertyChanged(nameof(SelectedIsSampleHost));
            OnPropertyChanged(nameof(SelectedIsSoundFont));
            OnPropertyChanged(nameof(SelectedResourceStatus));
            RebuildInspector();
        }
    }

    public bool HasSelection => _selectedNode is not null;
    public string SelectedNodeName => _selectedNode?.DisplayName ?? "";

    /// <summary>True when the selected node loads an audio sample (wavetable / sample player).</summary>
    public bool SelectedIsSampleHost => _selectedNode is ISampleHost;

    /// <summary>True when the selected node is a soundfont source.</summary>
    public bool SelectedIsSoundFont => _selectedNode is SoundFontNode;

    /// <summary>The selected resource-loading node's status line (sample/soundfont name).</summary>
    public string SelectedResourceStatus => _selectedNode switch
    {
        SoundFontNode sf => sf.Status,
        ISampleHost h => h.SampleName ?? "(no sample loaded)",
        _ => ""
    };

    /// <summary>Nodes visible at the current navigation level.</summary>
    public IEnumerable<FieldNode> VisibleNodes()
    {
        if (NavigationGroupId is { } gid)
        {
            var group = Graph.Groups.FirstOrDefault(g => g.Id == gid);
            if (group is null) return Graph.Nodes;
            var ids = group.NodeIds.ToHashSet();
            return Graph.Nodes.Where(n => ids.Contains(n.Id));
        }

        return Graph.Nodes;
    }

    /// <summary>Connections visible at the current navigation level.</summary>
    public IEnumerable<FieldConnection> VisibleConnections()
    {
        var ids = VisibleNodes().Select(n => n.Id).ToHashSet();
        return Graph.Connections.Where(c => ids.Contains(c.SourceNode) && ids.Contains(c.DestNode));
    }

    public void EnterGroup(FieldGroup group) => NavigationGroupId = group.Id;

    public void ExitGroup() => NavigationGroupId = null;

    public void CaptureHistory(string label) => _history?.Capture(label);

    /// <summary>Loads a decoded sample into the selected sample-host node (wavetable/sample player).</summary>
    public void LoadSampleIntoSelected(AudioSampleBuffer buffer, string name)
    {
        if (_selectedNode is not ISampleHost host) return;
        host.LoadSample(buffer, name);
        OnPropertyChanged(nameof(SelectedResourceStatus));
        RaiseStructureChanged();
    }

    /// <summary>Re-reads the selected resource node's status line (after a load completes).</summary>
    public void RefreshSelectedResourceStatus() => OnPropertyChanged(nameof(SelectedResourceStatus));

    /// <summary>Recompiles the audio graph and repaints the canvas after a structural change.</summary>
    public void NotifyStructureChanged()
    {
        _recompile();
        RaiseStructureChanged();
    }

    public void RaiseStructureChanged() => StructureChanged?.Invoke();

    /// <summary>Creates a node of the given type at a canvas position and selects it.</summary>
    public FieldNode? AddNode(string typeId, double x, double y, bool captureHistory = true)
    {
        if (captureHistory) CaptureHistory("Add Field node");
        var node = Registry.TryCreate(typeId);
        if (node is null) return null;
        node.X = x;
        node.Y = y;
        Graph.AddNode(node);
        if (NavigationGroupId is { } gid && Graph.Groups.FirstOrDefault(g => g.Id == gid) is { } group)
            group.NodeIds.Add(node.Id);
        SelectedNode = node;
        NotifyStructureChanged();
        return node;
    }

    public void RemoveNode(FieldNode node, bool captureHistory = true)
    {
        if (captureHistory) CaptureHistory("Delete Field node");
        Graph.RemoveNode(node.Id);
        if (ReferenceEquals(_selectedNode, node)) SelectedNode = null;
        NotifyStructureChanged();
    }

    public void RemoveSelected()
    {
        if (_selectedNode is { } n) RemoveNode(n);
    }

    public void FinishMoveNode(FieldNode node)
    {
        _ = node;
        Graph.Touch();
        NotifyStructureChanged();
    }

    public void FinishWireChange()
    {
        CaptureHistory("Wire Field node");
        NotifyStructureChanged();
    }

    /// <summary>Saves the current graph to a user-selected preset file.</summary>
    public bool SavePatchToFile(string path, string? displayName = null)
    {
        var display = string.IsNullOrWhiteSpace(displayName) ? PatchName.Trim() : displayName.Trim();
        if (display.Length == 0) display = System.IO.Path.GetFileNameWithoutExtension(path);

        try
        {
            using var fs = File.Create(path);
            if (IsInstrument && _instrumentHost?.Invoke() is { } inst)
                PresetFile.SaveFieldPatch(inst, display, Environment.UserName, fs);
            else if (!IsInstrument && _effectHost?.Invoke() is { } fx)
                PresetFile.SaveEffect(fx, display, Environment.UserName, fs);
            else return false;
        }
        catch
        {
            return false;
        }

        App.ServiceProvider?.GetService<IPresetLibrary>()?.Rescan();
        return true;
    }

    /// <summary>Saves the current graph as a user Field patch preset in the library folder.</summary>
    public string? SavePatch(string? name = null)
    {
        var display = string.IsNullOrWhiteSpace(name) ? PatchName.Trim() : name.Trim();
        if (display.Length == 0) display = IsInstrument ? "Field Patch" : "Field FX Patch";

        if (IsInstrument && _instrumentHost?.Invoke() is { } inst)
        {
            var lib = App.ServiceProvider?.GetService<IPresetLibrary>();
            return lib?.SaveFieldPatch(inst, display);
        }

        if (!IsInstrument && _effectHost?.Invoke() is { } fx)
        {
            var lib = App.ServiceProvider?.GetService<IPresetLibrary>();
            return lib?.SaveFieldEffectPatch(fx, display);
        }

        return null;
    }

    /// <summary>Promotes the current graph + surface to a Library instrument or effect definition.</summary>
    public bool SaveAsDefinition()
    {
        PushSurfaceToHost();
        var lib = App.ServiceProvider?.GetService<IFieldDefinitionLibrary>();
        if (lib is null)
        {
            StatusMessage = "Definition library unavailable.";
            return false;
        }

        var name = string.IsNullOrWhiteSpace(SaveDefinitionName)
            ? (IsInstrument ? "My Instrument" : "My Effect")
            : SaveDefinitionName.Trim();

        FieldDefinitionValidation.Result result;
        if (IsInstrument && InstrumentHost is { } inst)
        {
            CaptureHistory(inst.IsUserDefinition ? "Update Field instrument" : "Save Field instrument");
            result = lib.SaveFromInstrument(inst, name, existingDefinitionId: inst.DefinitionId);
            if (result.Ok)
            {
                // Reload identity from library so subsequent updates overwrite the same definition.
                var path = lib.PathFor(inst.TypeId);
                _ = path;
                StatusMessage = inst.IsUserDefinition ? $"Updated '{name}'." : $"Saved instrument '{name}'.";
            }
        }
        else if (!IsInstrument && EffectHost is { } fx)
        {
            CaptureHistory(fx.IsUserDefinition ? "Update Field effect" : "Save Field effect");
            result = lib.SaveFromEffect(fx, name, existingDefinitionId: fx.DefinitionId);
            StatusMessage = result.Ok
                ? (fx.IsUserDefinition ? $"Updated '{name}'." : $"Saved effect '{name}'.")
                : string.Join(" ", result.Errors);
        }
        else
        {
            StatusMessage = "No Field host available.";
            return false;
        }

        if (!result.Ok)
        {
            StatusMessage = string.Join(" ", result.Errors);
            return false;
        }

        if (result.Warnings.Count > 0)
            StatusMessage += " " + string.Join(" ", result.Warnings);

        OnPropertyChanged(nameof(HasUserDefinition));
        OnPropertyChanged(nameof(SaveDefinitionButtonText));
        return true;
    }

    public void ShowGraphMode() => EditorMode = "Graph";
    public void ShowInterfaceMode() => EditorMode = "Interface";

    private void PushSurfaceToHost()
    {
        if (InstrumentHost is { } inst)
        {
            inst.SetSurface(Surface.Surface);
            if (!string.IsNullOrWhiteSpace(SaveDefinitionName))
                inst.SetDisplayName(SaveDefinitionName);
        }
        else if (EffectHost is { } fx)
        {
            fx.SetSurface(Surface.Surface);
            if (!string.IsNullOrWhiteSpace(SaveDefinitionName))
                fx.SetDisplayName(SaveDefinitionName);
        }
    }

    /// <summary>Replaces the live graph from a saved Field patch file.</summary>
    public bool LoadPatchFromFile(string path)
    {
        var instruments = App.ServiceProvider?.GetService<IInstrumentRegistry>();
        var effects = App.ServiceProvider?.GetService<IEffectRegistry>();
        if (instruments is null || effects is null) return false;

        PresetLoadResult? loaded;
        try
        {
            using var fs = File.OpenRead(path);
            loaded = PresetFile.Load(fs, instruments, effects);
        }
        catch
        {
            return false;
        }

        if (loaded is null) return false;

        CaptureHistory("Load Field patch");

        if (IsInstrument && loaded.Instrument is FieldInstrument fi && _instrumentHost?.Invoke() is FieldInstrument hostInst)
        {
            using var ms = new MemoryStream();
            using (var w = new OngenWriter(ms)) FieldGraphSerializer.Write(w, fi.Graph);
            ms.Position = 0;
            using (var r = new OngenReader(ms)) FieldGraphSerializer.Read(r, hostInst.Graph, Registry);
            hostInst.Recompile();
        }
        else if (!IsInstrument && loaded.Effect is FieldEffect fe && _effectHost?.Invoke() is FieldEffect hostFx)
        {
            using var ms = new MemoryStream();
            using (var w = new OngenWriter(ms)) FieldGraphSerializer.Write(w, fe.Graph);
            ms.Position = 0;
            using (var r = new OngenReader(ms)) FieldGraphSerializer.Read(r, hostFx.Graph, Registry);
            hostFx.Recompile();
        }
        else return false;

        NavigationGroupId = null;
        SelectedNode = null;
        NotifyStructureChanged();
        return true;
    }

    /// <summary>Adds an LFO modulator wired into the selected node's parameter modulation inlet.</summary>
    public void AddModulatorTo(FieldNode node, int paramIndex)
    {
        CaptureHistory("Add Field modulator");
        var modPort = node.ModPortForParam(paramIndex);
        if (modPort < 0) return;
        var lfo = new LfoNode { X = node.X - 180, Y = node.Y + 40 + paramIndex * 20 };
        Graph.AddNode(lfo);
        Graph.Connect(lfo.Id, "out", node.Id, node.Inputs[modPort].Id);
        SelectedNode = lfo;
        NotifyStructureChanged();
    }

    private void RebuildInspector()
    {
        SelectedParameters.Clear();
        if (_selectedNode is null) return;
        foreach (var p in _selectedNode.Parameters)
            SelectedParameters.Add(ParameterViewModel.Create(p));
    }

    private void BuildPalette()
    {
        PaletteGroups.Clear();
        foreach (var group in Registry.Available
                     .GroupBy(i => i.Category)
                     .OrderBy(g => CategoryRank(g.Key)).ThenBy(g => g.Key))
        {
            var items = group
                .OrderBy(i => i.DisplayName)
                .Select(i => new FieldPaletteItem(i.Id, i.DisplayName, i.Category))
                .ToList();
            PaletteGroups.Add(new FieldPaletteGroup(group.Key, items));
        }
    }

    private static readonly string[] CategoryOrder =
    {
        FieldNodeCategories.Io, FieldNodeCategories.Oscillators, FieldNodeCategories.Envelopes,
        FieldNodeCategories.Filters, FieldNodeCategories.Modulators, FieldNodeCategories.Shapers,
        FieldNodeCategories.Dynamics, FieldNodeCategories.Time, FieldNodeCategories.Sampler,
        FieldNodeCategories.Math, FieldNodeCategories.Logic, FieldNodeCategories.Modules
    };

    private static int CategoryRank(string category)
    {
        var i = Array.IndexOf(CategoryOrder, category);
        return i < 0 ? CategoryOrder.Length : i;
    }
}

/// <summary>A palette category with its available node types.</summary>
public sealed class FieldPaletteGroup
{
    public FieldPaletteGroup(string name, IReadOnlyList<FieldPaletteItem> items)
    {
        Name = name;
        Items = items;
    }

    public string Name { get; }
    public IReadOnlyList<FieldPaletteItem> Items { get; }
}

/// <summary>A single palette entry (a node type that can be added to the graph).</summary>
public sealed class FieldPaletteItem
{
    public FieldPaletteItem(string typeId, string displayName, string category)
    {
        TypeId = typeId;
        DisplayName = displayName;
        Category = category;
    }

    public string TypeId { get; }
    public string DisplayName { get; }
    public string Category { get; }
}
