using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Implemented by instruments that play a user-loaded audio sample (e.g. the Basic Sampler), so
/// the instrument inspector can offer a "Load sample" action.
/// </summary>
public interface ISampleHost
{
    /// <summary>Name of the loaded sample, or null if none.</summary>
    string? SampleName { get; }

    /// <summary>Loads (or replaces) the sample to play.</summary>
    void LoadSample(AudioSampleBuffer sample, string name);
}
