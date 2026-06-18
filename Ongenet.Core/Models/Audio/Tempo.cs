namespace Ongenet.Core.Models.Audio;

/// <summary>
/// Musical tempo, expressed in beats per minute.
/// </summary>
public readonly record struct Tempo(double BeatsPerMinute)
{
    /// <summary>Duration of a single beat, in seconds.</summary>
    public double SecondsPerBeat => 60.0 / BeatsPerMinute;
}
