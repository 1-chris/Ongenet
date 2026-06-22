using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio;

/// <summary>
/// Selects which <see cref="IAudioBackend"/> is active and switches between them at runtime. The
/// implementation also presents itself as the engine's <see cref="IAudioOutput"/>,
/// <see cref="IAudioInput"/> and <see cref="IAudioDeviceService"/>, forwarding to the active backend —
/// so swapping backends is invisible to everything downstream.
/// </summary>
public interface IAudioBackendManager
{
    /// <summary>All known backends with their support/active state, for the settings picker.</summary>
    IReadOnlyList<AudioBackendInfo> Backends { get; }

    /// <summary>The id of the currently active backend.</summary>
    string ActiveId { get; }

    /// <summary>
    /// Makes the backend with <paramref name="id"/> active: stops the current streams, swaps backends,
    /// and restarts any stream that was running on the new backend. No-op for an unknown/unsupported id
    /// or the already-active backend.
    /// </summary>
    void Switch(string id);

    /// <summary>Raised after the active backend changes.</summary>
    event Action? BackendChanged;
}
