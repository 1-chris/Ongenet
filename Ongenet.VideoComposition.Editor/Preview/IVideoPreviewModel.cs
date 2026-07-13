using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Media;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.VideoComposition.Editor.Preview;

/// <summary>Narrow preview contract for the composition canvas (implemented by program monitor VM).</summary>
public interface IVideoPreviewModel : INotifyPropertyChanged
{
    IImage? Frame { get; }
    int CanvasWidth { get; }
    int CanvasHeight { get; }
    IReadOnlyList<VideoLayer> Layers { get; }
    int PreviewTick { get; }
    int WaveformRevision { get; }
    double PlayheadBeats { get; }
    IVideoAudioScopeService AudioScope { get; }
    double GetLayerOpacity(Guid layerId);
    bool IsItemSelected(VideoLayer layer, VideoLayerItem item);
    bool IsWaveformLayerSelected(VideoLayer layer);
    void SelectItem(VideoLayer layer, VideoLayerItem item);
    void SelectWaveformLayer(VideoLayer layer);
    void SetWaveformBounds(VideoLayer layer, double x, double y, double width, double height);
    void MoveElement(Guid itemId, double x, double y);
    void ResizeElement(Guid itemId, double width, double height);
    bool ShowSafeAreaOverlay { get; }
    AudioWaveform? GetWaveformForLayer(VideoLayer layer);
    IImage? GetOverlayFrame(VideoLayer layer, VideoLayerItem item);
    IVideoEngine3DLayerRenderer? Engine3DRenderer { get; }
    double PreviewDtSeconds { get; }
}
