using System;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio;

/// <summary>Shared aux-send accumulation for live and offline renderers.</summary>
public static class SendMixing
{
    public static void ProcessSends(
        ReadOnlySpan<float> source,
        Track track,
        Func<Guid, Span<float>> getReturnBuffer,
        int channels,
        int frames,
        bool preFader,
        bool silenced,
        bool hasContent)
    {
        if (track.Sends.Count == 0 || silenced || !hasContent) return;

        var (lg, rg) = Mixing.StripGains(track.Volume, track.Pan);
        foreach (var send in track.Sends)
        {
            if (!send.Enabled || send.Level <= 0) continue;
            if (send.PreFader != preFader) continue;

            var dst = getReturnBuffer(send.TargetTrackId);
            if (dst.IsEmpty) continue;

            var gain = (float)send.Level;
            for (var frame = 0; frame < frames; frame++)
            {
                var i = frame * channels;
                dst[i] += source[i] * (preFader ? gain : gain * lg);
                if (channels >= 2)
                    dst[i + 1] += source[i + 1] * (preFader ? gain : gain * rg);
            }
        }
    }
}
