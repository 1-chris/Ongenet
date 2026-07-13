using System;
using System.Collections.Generic;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>Bakes the in-app video composition frame-by-frame and encodes to MP4 via ffmpeg.</summary>
public interface IVideoCompositor
{
    bool IsAvailable { get; }

    void Export(Project project, string wavPath, string outputPath, double durationSeconds,
        IReadOnlyDictionary<Guid, double>? layerOpacities = null,
        IVideoWaveformCacheService? waveformCache = null, double bpm = 120,
        double startBeat = 0, IProgress<double>? progress = null);
}
