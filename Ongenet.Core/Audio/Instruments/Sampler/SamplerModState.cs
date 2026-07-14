namespace Ongenet.Core.Audio.Instruments.Sampler;

/// <summary>
/// Live MIDI controller state shared by an <see cref="SamplerInstrument"/> and its voices.
/// </summary>
public sealed class SamplerModState
{
    /// <summary>Current value (0..127) of each MIDI CC.</summary>
    public readonly int[] Cc = new int[128];

    /// <summary>Pitch-bend position in [-1, 1] (0 = centre).</summary>
    public double Bend;

    /// <summary>Pitch-bend as SFZ raw units roughly -8192..8191 for gates.</summary>
    public int BendRaw;

    /// <summary>Channel aftertouch / pressure, 0..127.</summary>
    public int ChannelAftertouch;

    /// <summary>Per-note poly aftertouch, 0..127.</summary>
    public readonly int[] PolyAft = new int[128];

    /// <summary>MIDI channel 1..16.</summary>
    public int Channel = 1;

    /// <summary>Current program 0..127.</summary>
    public int Program;

    /// <summary>Host tempo in BPM (for lobpm/delay_beats).</summary>
    public double HostBpm = 120;

    /// <summary>Curve bank attached at load (instrument-owned reference).</summary>
    public SamplerCurveBank? Curves;

    public int PolyAftertouch(int key)
        => key is >= 0 and <= 127 ? PolyAft[key] : 0;

    public void Reset()
    {
        for (var i = 0; i < Cc.Length; i++) Cc[i] = 0;
        for (var i = 0; i < PolyAft.Length; i++) PolyAft[i] = 0;
        Bend = 0;
        BendRaw = 0;
        ChannelAftertouch = 0;
        Channel = 1;
        Program = 0;
        // Keep HostBpm / Curves across reset of articulation notes.
    }

    public void SeedCc(System.Collections.Generic.IReadOnlyDictionary<int, int>? initial)
    {
        if (initial is null) return;
        foreach (var kv in initial)
        {
            if (kv.Key is >= 0 and <= 127)
                Cc[kv.Key] = kv.Value < 0 ? 0 : kv.Value > 127 ? 127 : kv.Value;
        }
    }
}
