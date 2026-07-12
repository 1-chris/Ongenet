namespace Ongenet.Core.Models.Audio;

/// <summary>A MIDI CC event within a clip, positioned relative to the clip start (beats).</summary>
public sealed class MidiControlChange
{
    public int Controller { get; set; }
    public int Value { get; set; }
    public double StartBeat { get; set; }
    public double LengthBeats { get; set; } = 0.25;
}
