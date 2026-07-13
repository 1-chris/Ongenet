using System;
using Ongenet.Core.Models.Media;

namespace Ongenet.App.Services;

/// <summary>Shared selection across video resources, timeline, and preview.</summary>
public interface IVideoSelectionService
{
    event Action? SelectionChanged;

    VideoLayer? SelectedLayer { get; set; }
    VideoLayerItem? SelectedLayerItem { get; set; }
    VideoTrigger? SelectedTrigger { get; set; }
    VideoVisibilityRegion? SelectedVisibilityRegion { get; set; }

    void Clear();
}
