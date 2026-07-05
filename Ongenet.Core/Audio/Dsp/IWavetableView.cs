namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A source of a live wavetable for visualization: the current table, a revision that bumps when the table
/// changes (load/preset), and the current scan position (0..1) for the display cursor. Implemented by the
/// Wavetable instrument and by the Field wavetable-source node, so one 3D view renders both.
/// </summary>
public interface IWavetableView
{
    Wavetable Table { get; }
    int TableRevision { get; }
    float DisplayPosition { get; }
}
