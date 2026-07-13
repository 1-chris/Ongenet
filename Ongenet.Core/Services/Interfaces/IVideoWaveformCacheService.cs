using System;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Services.Interfaces;

public interface IVideoWaveformCacheService
{
    int Revision { get; }

    void Invalidate();

    AudioWaveform? TryGet(Guid trackId);

    AudioWaveform GetOrBuild(Models.Audio.Project project, Guid trackId, double bpm, IProgress<double>? progress = null);
}
