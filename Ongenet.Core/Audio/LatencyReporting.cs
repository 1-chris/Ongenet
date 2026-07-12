using System.Collections.Generic;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio;

/// <summary>Helpers for summing reported latency through effect/instrument chains.</summary>
public static class LatencyReporting
{
    public static int Of(IAudioEffect effect)
        => effect is ILatencyProvider lp ? System.Math.Max(0, lp.ReportedLatencySamples) : 0;

    public static int Of(IInstrument instrument)
        => instrument is ILatencyProvider lp ? System.Math.Max(0, lp.ReportedLatencySamples) : 0;

    public static int SumEffects(IReadOnlyList<IAudioEffect> effects)
    {
        var sum = 0;
        foreach (var fx in effects)
        {
            if (!fx.Enabled) continue;
            sum += Of(fx);
        }

        return sum;
    }

    public static int SumEffects(IAudioEffect[] effects) => SumEffects((IReadOnlyList<IAudioEffect>)effects);

    /// <summary>Latency from instrument slots (pre-FX + instrument) on a content track.</summary>
    public static int TrackContentLatency(Track track)
    {
        var sum = 0;
        if (track.Kind == TrackKind.Instrument)
        {
            foreach (var slot in track.ActiveInstruments)
            {
                if (!slot.Enabled) continue;
                sum += SumEffects(slot.ActiveEffects);
                sum += Of(slot.Instrument);
            }
        }

        sum += SumEffects(track.ActiveEffects);
        return sum;
    }

    /// <summary>Latency through a bus track's insert chain.</summary>
    public static int BusLatency(Track bus) => bus.IsBus ? SumEffects(bus.ActiveEffects) : 0;
}
