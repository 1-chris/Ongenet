using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native;

/// <summary>
/// The set of Linux audio subsystem drivers, in preference order. Only the available ones (their native
/// library present) are exposed; device enumeration and stream opening are routed to the driver that
/// owns a device, identified by its <see cref="AudioDevice.HostApi"/> tag.
/// </summary>
internal sealed class NativeDriverRegistry
{
    private readonly List<INativeAudioDriver> _drivers;

    public NativeDriverRegistry()
    {
        // Order = preference for the default device and enumeration grouping. ALSA is implemented;
        // the others are scaffolded and contribute nothing until their bodies land.
        _drivers = new List<INativeAudioDriver>
        {
            new PipeWireAudioDriver(),
            new PulseAudioDriver(),
            new JackAudioDriver(),
            new AlsaAudioDriver(),
        };
    }

    public IReadOnlyList<INativeAudioDriver> Available => _drivers.Where(d => d.IsAvailable).ToList();

    /// <summary>The driver that owns <paramref name="device"/>, matched by its host-API tag.</summary>
    public INativeAudioDriver? For(AudioDevice device)
        => _drivers.FirstOrDefault(d => d.IsAvailable && d.HostApi == device.HostApi);
}
