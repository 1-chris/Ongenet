using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Ongenet.App.Services;
using Ongenet.App.ViewModels.Panels;
using Ongenet.App.ViewModels.VideoTimeline;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels.Panels;

public enum VideoResourceKind { Section, Layer, LayerItem, Trigger, LinkedClip }

public sealed class VideoResourceNode
{
    public VideoResourceKind Kind { get; init; }
    public string Label { get; init; } = "";
    public string? Hint { get; init; }
    public VideoLayer? Layer { get; init; }
    public VideoLayerItem? LayerItem { get; init; }
    public VideoTrigger? Trigger { get; init; }
    public ClipSyncOption? LinkedClip { get; init; }
    public ObservableCollection<VideoResourceNode> Children { get; } = new();
    public bool IsMissingFile { get; init; }
}

/// <summary>Left sidebar project bin for video editing mode.</summary>
public sealed class VideoResourcesViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private readonly IVideoSelectionService _selection;
    private readonly VideoTimelineViewModel _timeline;
    private VideoResourceNode? _selectedNode;

    public VideoResourcesViewModel(IProjectService project, IVideoSelectionService selection,
        VideoTimelineViewModel timeline)
    {
        _project = project;
        _selection = selection;
        _timeline = timeline;

        AddLayerCommand = new RelayCommand(() => _timeline.AddLayerCommand.Execute(null),
            () => _timeline.AddLayerCommand.CanExecute(null));
        AddTriggerCommand = new RelayCommand(() => _timeline.AddTriggerCommand.Execute(null),
            () => _timeline.AddTriggerCommand.CanExecute(null));
        DeleteCommand = new RelayCommand(DeleteSelected, CanDelete);
        MoveLayerUpCommand = new RelayCommand(
            () => _timeline.MoveLayerUpCommand.Execute(null),
            () => SelectedNode?.Kind == VideoResourceKind.Layer && _timeline.MoveLayerUpCommand.CanExecute(null));
        MoveLayerDownCommand = new RelayCommand(
            () => _timeline.MoveLayerDownCommand.Execute(null),
            () => SelectedNode?.Kind == VideoResourceKind.Layer && _timeline.MoveLayerDownCommand.CanExecute(null));
        DeleteLayerCommand = new RelayCommand(
            () => _timeline.RemoveLayerCommand.Execute(null),
            () => SelectedNode?.Kind == VideoResourceKind.Layer && _timeline.RemoveLayerCommand.CanExecute(null));

        _timeline.LanesChanged += () =>
        {
            MoveLayerUpCommand.RaiseCanExecuteChanged();
            MoveLayerDownCommand.RaiseCanExecuteChanged();
            DeleteLayerCommand.RaiseCanExecuteChanged();
        };

        _timeline.LanesChanged += Rebuild;
        _project.ProjectChanged += Rebuild;
        _selection.SelectionChanged += SyncSelectionFromService;
        _timeline.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(VideoTimelineViewModel.IsProjectVideoEnabled))
                Rebuild();
        };
        Rebuild();
    }

    public ObservableCollection<VideoResourceNode> Roots { get; } = new();

    public VideoResourceNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetField(ref _selectedNode, value)) return;
            ApplySelection(value);
            DeleteCommand.RaiseCanExecuteChanged();
            MoveLayerUpCommand.RaiseCanExecuteChanged();
            MoveLayerDownCommand.RaiseCanExecuteChanged();
            DeleteLayerCommand.RaiseCanExecuteChanged();
        }
    }

    public RelayCommand AddLayerCommand { get; }
    public RelayCommand AddTriggerCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand MoveLayerUpCommand { get; }
    public RelayCommand MoveLayerDownCommand { get; }
    public RelayCommand DeleteLayerCommand { get; }

    public void SeekLinkedClip(ClipSyncOption clip) => _timeline.SeekToClipCommand.Execute(clip);

    private void Rebuild()
    {
        Roots.Clear();
        if (!_project.Current.VideoEnabled) return;

        var layersSection = new VideoResourceNode
        {
            Kind = VideoResourceKind.Section,
            Label = L("VideoResources_Layers"),
            Hint = L("VideoResources_Layers_hint")
        };
        foreach (var layer in _project.Current.VideoLayers.OrderBy(l => l.ZOrder))
        {
            var layerNode = new VideoResourceNode
            {
                Kind = VideoResourceKind.Layer,
                Layer = layer,
                Label = layer.Name
            };
            foreach (var item in layer.Items)
            {
                var itemLabel = VideoTimelineHelper.LayerItemLabel(item, L("VideoTimeline_Empty_item"));
                layerNode.Children.Add(new VideoResourceNode
                {
                    Kind = VideoResourceKind.LayerItem,
                    Layer = layer,
                    LayerItem = item,
                    Label = itemLabel,
                    IsMissingFile = !string.IsNullOrWhiteSpace(item.SourcePath) && !File.Exists(item.SourcePath)
                });
            }

            if (layer.IsWaveformLayer)
            {
                layerNode.Children.Add(new VideoResourceNode
                {
                    Kind = VideoResourceKind.LayerItem,
                    Layer = layer,
                    Label = VideoTimelineHelper.LayerWaveformLabel(layer, _project.Current,
                        L("VideoTrack_Waveform_none"), L("VideoTrack_Unknown_layer"))
                });
            }

            layersSection.Children.Add(layerNode);
        }
        Roots.Add(layersSection);

        var triggersSection = new VideoResourceNode
        {
            Kind = VideoResourceKind.Section,
            Label = L("VideoResources_Triggers"),
            Hint = L("VideoResources_Triggers_hint")
        };
        var clips = _timeline.ArrangementClips.ToList();
        var layers = _project.Current.VideoLayers;
        foreach (var tr in _project.Current.VideoTriggers)
        {
            triggersSection.Children.Add(new VideoResourceNode
            {
                Kind = VideoResourceKind.Trigger,
                Trigger = tr,
                Label = VideoTimelineHelper.TriggerLabel(tr, _project.Current, clips, layers,
                    L("VideoTrack_Any_clip"), L("VideoTrack_Unknown_layer"),
                    _timeline.TriggerMomentOptions, _timeline.TriggerActionOptions)
            });
        }
        Roots.Add(triggersSection);

        var linkedSection = new VideoResourceNode
        {
            Kind = VideoResourceKind.Section,
            Label = L("VideoResources_Linked_clips"),
            Hint = L("VideoResources_Linked_clips_hint")
        };
        var linkedIds = _project.Current.VideoLayers
            .Where(l => l.SyncClipId is not null).Select(l => l.SyncClipId!.Value)
            .Concat(_project.Current.VideoTriggers.Where(t => t.ClipId is not null).Select(t => t.ClipId!.Value))
            .Distinct();
        foreach (var id in linkedIds)
        {
            var opt = clips.FirstOrDefault(c => c.ClipId == id);
            if (opt is not null)
                linkedSection.Children.Add(new VideoResourceNode
                {
                    Kind = VideoResourceKind.LinkedClip,
                    LinkedClip = opt,
                    Label = opt.Label
                });
        }
        Roots.Add(linkedSection);

        SyncSelectionFromService();
        AddLayerCommand.RaiseCanExecuteChanged();
        AddTriggerCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
    }

    private void SyncSelectionFromService()
    {
        VideoResourceNode? node = null;
        if (_selection.SelectedTrigger is { } tr)
            node = FindNode(n => n.Trigger?.Id == tr.Id);
        else if (_selection.SelectedLayer is { } layer)
            node = FindNode(n => n.Layer?.Id == layer.Id && n.Kind != VideoResourceKind.LayerItem);
        else if (_selection.SelectedLayerItem is { } item)
            node = FindNode(n => n.LayerItem?.Id == item.Id);

        if (node != _selectedNode)
        {
            _selectedNode = node;
            OnPropertyChanged(nameof(SelectedNode));
        }
        DeleteCommand.RaiseCanExecuteChanged();
    }

    private VideoResourceNode? FindNode(Func<VideoResourceNode, bool> pred)
    {
        foreach (var root in Roots)
        {
            foreach (var child in root.Children)
            {
                if (pred(child)) return child;
                foreach (var grand in child.Children)
                {
                    if (pred(grand)) return grand;
                }
            }
        }
        return null;
    }

    private void ApplySelection(VideoResourceNode? node)
    {
        switch (node?.Kind)
        {
            case VideoResourceKind.Layer:
                _selection.SelectedLayer = node.Layer;
                _selection.SelectedLayerItem = node.Layer?.Items.FirstOrDefault();
                _selection.SelectedTrigger = null;
                _selection.SelectedVisibilityRegion = null;
                break;
            case VideoResourceKind.LayerItem:
                _selection.SelectedLayer = node.Layer;
                _selection.SelectedLayerItem = node.LayerItem;
                _selection.SelectedTrigger = null;
                _selection.SelectedVisibilityRegion = null;
                break;
            case VideoResourceKind.Trigger:
                _selection.SelectedTrigger = node.Trigger;
                _selection.SelectedLayer = _project.Current.VideoLayers
                    .FirstOrDefault(l => l.Id == node.Trigger?.TargetLayerId);
                break;
            case VideoResourceKind.LinkedClip when node.LinkedClip is not null:
                _timeline.SeekToClipCommand.Execute(node.LinkedClip);
                break;
        }
    }

    private bool CanDelete() => SelectedNode?.Kind is VideoResourceKind.Layer or VideoResourceKind.Trigger;

    private void DeleteSelected()
    {
        switch (SelectedNode?.Kind)
        {
            case VideoResourceKind.Layer:
                _timeline.RemoveLayerCommand.Execute(null);
                break;
            case VideoResourceKind.Trigger:
                _timeline.RemoveTriggerCommand.Execute(null);
                break;
        }
        Rebuild();
    }
}
