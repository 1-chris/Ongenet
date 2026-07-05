using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Field.Nodes;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;

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
    private FieldNode? _selectedNode;

    public FieldEditorViewModel(FieldGraph graph, IFieldNodeRegistry registry, Action recompile,
        IReadOnlyList<string> presetNames, Action<int> loadPreset, Func<CompiledGraph?> compiled, bool isInstrument)
    {
        Graph = graph;
        Registry = registry;
        _recompile = recompile;
        _presetNames = presetNames;
        _loadPreset = loadPreset;
        CompiledAccessor = compiled;
        IsInstrument = isInstrument;
        BuildPalette();
    }

    public FieldGraph Graph { get; }
    public IFieldNodeRegistry Registry { get; }
    public Func<CompiledGraph?> CompiledAccessor { get; }
    public bool IsInstrument { get; }

    /// <summary>Raised when the graph's structure changes so the canvas repaints.</summary>
    public event Action? StructureChanged;

    /// <summary>Grouped node palette (category → available node types).</summary>
    public ObservableCollection<FieldPaletteGroup> PaletteGroups { get; } = new();

    public IReadOnlyList<string> PresetNames => _presetNames;

    private int _selectedPreset = -1;
    public int SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (!SetField(ref _selectedPreset, value)) return;
            if (value >= 0 && value < _presetNames.Count)
            {
                _loadPreset(value);
                SelectedNode = null;
                RaiseStructureChanged();
            }
        }
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

    /// <summary>Parameters of the selected node, shown in the inspector.</summary>
    public ObservableCollection<ParameterViewModel> SelectedParameters { get; } = new();

    /// <summary>Recompiles the audio graph and repaints the canvas after a structural change.</summary>
    public void NotifyStructureChanged()
    {
        _recompile();
        RaiseStructureChanged();
    }

    public void RaiseStructureChanged() => StructureChanged?.Invoke();

    /// <summary>Creates a node of the given type at a canvas position and selects it.</summary>
    public FieldNode? AddNode(string typeId, double x, double y)
    {
        var node = Registry.TryCreate(typeId);
        if (node is null) return null;
        node.X = x;
        node.Y = y;
        Graph.AddNode(node);
        SelectedNode = node;
        NotifyStructureChanged();
        return node;
    }

    public void RemoveNode(FieldNode node)
    {
        Graph.RemoveNode(node.Id);
        if (ReferenceEquals(_selectedNode, node)) SelectedNode = null;
        NotifyStructureChanged();
    }

    public void RemoveSelected()
    {
        if (_selectedNode is { } n) RemoveNode(n);
    }

    /// <summary>Adds an LFO modulator wired into the selected node's parameter modulation inlet.</summary>
    public void AddModulatorTo(FieldNode node, int paramIndex)
    {
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
