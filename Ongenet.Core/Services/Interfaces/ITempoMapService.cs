using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>Converts between arrangement beats and wall-clock seconds using the project tempo map.</summary>
public interface ITempoMapService
{
    /// <summary>Seconds elapsed from project beat 0 to <paramref name="beat"/>.</summary>
    double BeatsToSeconds(Project project, double beat);

    /// <summary>Arrangement beat at <paramref name="seconds"/> from project start.</summary>
    double SecondsToBeats(Project project, double seconds);

    /// <summary>BPM in effect at <paramref name="beat"/>.</summary>
    double TempoAtBeat(Project project, double beat);
}
