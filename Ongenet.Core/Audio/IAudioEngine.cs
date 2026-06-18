using System;

namespace Ongenet.Core.Audio;

/// <summary>
/// The real-time audio engine: owns the device, mixes the project's sources, and renders to
/// the output. A live runtime object distinct from the project data model.
/// </summary>
public interface IAudioEngine : IDisposable
{
    /// <summary>Whether the engine is started and streaming.</summary>
    bool IsRunning { get; }

    /// <summary>The format the engine/device runs at.</summary>
    AudioFormat Format { get; }

    /// <summary>Master output peak level (0..1, with release) for the left channel.</summary>
    float MasterLevelLeft { get; }

    /// <summary>Master output peak level (0..1, with release) for the right channel.</summary>
    float MasterLevelRight { get; }

    /// <summary>Opens the device and starts rendering the current project.</summary>
    void Start();

    /// <summary>Stops rendering and closes the device.</summary>
    void Stop();
}
