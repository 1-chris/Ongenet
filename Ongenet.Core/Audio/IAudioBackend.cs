using System;

namespace Ongenet.Core.Audio;

/// <summary>
/// One low-level audio system (the OS-native stack: ALSA/PipeWire/JACK/Pulse, CoreAudio or WASAPI).
/// Bundles the three device
/// seams — enumeration/selection, output, input — that belong to the same underlying library so they
/// share its lifetime and device-identity scheme. <see cref="AudioBackendManager"/> holds a set of
/// these and forwards the engine's seams to whichever one is active, so a backend can be swapped at
/// runtime without the engine, recording, or DSP ever knowing which library is in use.
/// </summary>
public interface IAudioBackend : IDisposable
{
    /// <summary>Stable identifier persisted in settings and used to switch backends, e.g. "native".</summary>
    string Id { get; }

    /// <summary>Human-readable name shown in the audio-system picker, e.g. "Native".</summary>
    string DisplayName { get; }

    /// <summary>
    /// Whether this backend can run on the current operating system. A supported backend may still
    /// expose no devices if its native library is absent — that degrades to silence, not a crash.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>Device enumeration + selection for this backend.</summary>
    IAudioDeviceService Devices { get; }

    /// <summary>The playback stream for this backend (reads the selected output device).</summary>
    IAudioOutput Output { get; }

    /// <summary>The capture stream for this backend (reads the selected input device).</summary>
    IAudioInput Input { get; }
}
