namespace Ongenet.Core.Audio.Instruments.Sampler;

/// <summary>
/// Live MIDI controller state shared by an <see cref="SamplerInstrument"/> and its voices: CC values,
/// pitch-bend and channel aftertouch. Voices read it each block so held notes respond to the mod wheel,
/// bend and CC-mapped parameters in real time. Written from note-event callbacks (under the instrument
/// lock) and read on the audio thread; values are simple and updated atomically enough for control use.
/// </summary>
public sealed class SamplerModState
{
    /// <summary>Current value (0..127) of each MIDI CC.</summary>
    public readonly int[] Cc = new int[128];

    /// <summary>Pitch-bend position in [-1, 1] (0 = centre).</summary>
    public double Bend;

    /// <summary>Channel aftertouch / pressure, 0..127.</summary>
    public int ChannelAftertouch;

    public void Reset()
    {
        for (var i = 0; i < Cc.Length; i++) Cc[i] = 0;
        Bend = 0;
        ChannelAftertouch = 0;
    }
}
