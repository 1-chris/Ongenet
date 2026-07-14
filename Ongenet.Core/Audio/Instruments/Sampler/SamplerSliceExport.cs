using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Instruments.Sampler;

namespace Ongenet.Core.Audio.Files;

/// <summary>Exports beat-slice regions from an audio buffer into sampler zones.</summary>
public static class SamplerSliceExport
{
    public static IReadOnlyList<SamplerRegion> BuildRegions(AudioSampleBuffer source,
        IReadOnlyList<AudioSliceRegion> regions, int rootKeyStart = 60, string namePrefix = "Slice")
    {
        if (source.FrameCount <= 0 || regions.Count == 0) return Array.Empty<SamplerRegion>();

        var ordered = new List<AudioSliceRegion>(regions);
        ordered.Sort((a, b) => a.Order.CompareTo(b.Order));

        var built = new List<SamplerRegion>();
        var key = rootKeyStart;
        var layerId = Guid.NewGuid();
        foreach (var region in ordered)
        {
            if (!region.Selected) continue;
            var count = region.EndFrame - region.StartFrame;
            if (count <= 0) continue;

            var segment = SampleEditOps.CopyRange(source, region.StartFrame, count);
            var sample = SamplerSample.FromResident(
                new AudioSampleBuffer(segment.Samples, segment.Channels, segment.SampleRate));
            sample.DisplayName = $"{namePrefix} {built.Count + 1}";

            built.Add(new SamplerRegion
            {
                Sample = sample,
                LayerId = layerId,
                LayerColorArgb = 0xFF6C9EEF,
                LoKey = key,
                HiKey = key,
                LoVel = 0,
                HiVel = 127,
                PitchKeycenter = key,
                KeytrackSemisPerKey = 1.0,
                Gain = 1.0,
                Offset = 0,
                End = count,
                LoopMode = SamplerLoopMode.NoLoop,
                Trigger = SamplerTrigger.Attack,
                AmpEg = default
            });
            key++;
            if (key > 127) break;
        }

        return built;
    }
}
