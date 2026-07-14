using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Implemented by effects that convolve against a user-loaded impulse response (e.g. Convolution),
/// so the inspector / Field graph can offer a "Load impulse" action.
/// </summary>
public interface IImpulseHost
{
    /// <summary>Name of the loaded impulse, or null if none.</summary>
    string? ImpulseName { get; }

    /// <summary>The currently loaded impulse buffer, or null — so a project save can embed it.</summary>
    AudioSampleBuffer? CurrentImpulse { get; }

    /// <summary>Loads (or replaces) the impulse response.</summary>
    void LoadImpulse(AudioSampleBuffer impulse, string name);
}
