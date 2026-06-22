using Ongenet.Core.Audio;

namespace Ongenet.Audio;

/// <summary>
/// The PortAudio <see cref="IAudioBackend"/>: bundles the existing PortAudio device service, output and
/// input (which already share a process-wide PortAudio reference via <c>PaRef</c>/<c>PaUnref</c>). This
/// is the default backend on every platform until the native backends are fully shaken out.
/// </summary>
public sealed class PortAudioBackend : IAudioBackend
{
    private readonly PortAudioDeviceService _devices;
    private readonly PortAudioOutput _output;
    private readonly PortAudioInput _input;

    public PortAudioBackend()
    {
        _devices = new PortAudioDeviceService();
        _output = new PortAudioOutput(_devices);
        _input = new PortAudioInput(_devices);
    }

    public string Id => "portaudio";
    public string DisplayName => "PortAudio";
    public bool IsSupported => true;

    public IAudioDeviceService Devices => _devices;
    public IAudioOutput Output => _output;
    public IAudioInput Input => _input;

    public void Dispose()
    {
        _output.Dispose();
        _input.Dispose();
        _devices.Dispose();
    }
}
