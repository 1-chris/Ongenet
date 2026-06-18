using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ongenet.Audio.Interop;
using Ongenet.Core.Audio;

namespace Ongenet.Audio;

/// <summary>
/// <see cref="IAudioDeviceService"/> backed by PortAudio. Enumerates the machine's devices once at
/// startup (and on <see cref="Refresh"/>) and remembers the user's input/output choice. The output
/// and input streams read the current selection from here, so changing a device reopens its stream.
/// </summary>
public sealed class PortAudioDeviceService : IAudioDeviceService, IDisposable
{
    private readonly object _lock = new();
    private List<AudioDevice> _inputs = new();
    private List<AudioDevice> _outputs = new();
    private AudioDevice? _selectedInput;
    private AudioDevice? _selectedOutput;
    private AudioInputChannelMode _inputChannelMode = AudioInputChannelMode.Stereo;
    private bool _paReferenced;
    private bool _disposed;

    public PortAudioDeviceService()
    {
        // Hold a PortAudio reference for the service's lifetime so device queries stay valid. If
        // PortAudio is missing/unusable, degrade to empty device lists rather than crashing the app
        // (the engine likewise runs silently without a device).
        try
        {
            PortAudioNative.PaRef();
            _paReferenced = true;
            Enumerate();
        }
        catch
        {
            // No PortAudio: leave the device lists empty.
        }
    }

    public IReadOnlyList<AudioDevice> InputDevices { get { lock (_lock) return _inputs; } }
    public IReadOnlyList<AudioDevice> OutputDevices { get { lock (_lock) return _outputs; } }

    public AudioDevice? SelectedOutput
    {
        get { lock (_lock) return _selectedOutput; }
        set
        {
            lock (_lock)
            {
                if (Equals(_selectedOutput, value)) return;
                _selectedOutput = value;
            }

            OutputChanged?.Invoke();
        }
    }

    public AudioDevice? SelectedInput
    {
        get { lock (_lock) return _selectedInput; }
        set
        {
            lock (_lock)
            {
                if (Equals(_selectedInput, value)) return;
                _selectedInput = value;
            }

            InputChanged?.Invoke();
        }
    }

    public AudioInputChannelMode InputChannelMode
    {
        get { lock (_lock) return _inputChannelMode; }
        set
        {
            lock (_lock)
            {
                if (_inputChannelMode == value) return;
                _inputChannelMode = value;
            }

            InputChanged?.Invoke();
        }
    }

    public event Action? DevicesChanged;
    public event Action? OutputChanged;
    public event Action? InputChanged;

    public void Refresh()
    {
        if (!_paReferenced)
        {
            try { PortAudioNative.PaRef(); _paReferenced = true; }
            catch { return; }
        }

        try { Enumerate(); } catch { /* keep prior lists */ }
        DevicesChanged?.Invoke();
    }

    private void Enumerate()
    {
        var inputs = new List<AudioDevice>();
        var outputs = new List<AudioDevice>();

        var count = PortAudioNative.Pa_GetDeviceCount();
        var defIn = PortAudioNative.Pa_GetDefaultInputDevice();
        var defOut = PortAudioNative.Pa_GetDefaultOutputDevice();

        for (var i = 0; i < count; i++)
        {
            var ptr = PortAudioNative.Pa_GetDeviceInfo(i);
            if (ptr == IntPtr.Zero) continue;

            var info = Marshal.PtrToStructure<PortAudioNative.PaDeviceInfo>(ptr);
            var name = Marshal.PtrToStringUTF8(info.name) ?? $"Device {i}";
            var device = new AudioDevice(
                i, name, HostApiName(info.hostApi),
                info.maxInputChannels, info.maxOutputChannels,
                i == defIn, i == defOut);

            if (device.SupportsInput) inputs.Add(device);
            if (device.SupportsOutput) outputs.Add(device);
        }

        lock (_lock)
        {
            _inputs = inputs;
            _outputs = outputs;
            // Default the selection to the system default device (kept stable across refreshes by index).
            _selectedOutput = Reconcile(_selectedOutput, outputs, defOut);
            _selectedInput = Reconcile(_selectedInput, inputs, defIn);
        }
    }

    // Keeps a prior selection if it still exists; otherwise falls back to the default device.
    private static AudioDevice? Reconcile(AudioDevice? current, List<AudioDevice> devices, int defaultIndex)
    {
        if (current is not null)
        {
            foreach (var d in devices)
                if (d.Index == current.Index && d.Name == current.Name) return d;
        }

        foreach (var d in devices)
            if (d.Index == defaultIndex) return d;

        return devices.Count > 0 ? devices[0] : null;
    }

    private static string HostApiName(int hostApi)
    {
        var ptr = PortAudioNative.Pa_GetHostApiInfo(hostApi);
        if (ptr == IntPtr.Zero) return string.Empty;
        var info = Marshal.PtrToStructure<PortAudioNative.PaHostApiInfo>(ptr);
        return Marshal.PtrToStringUTF8(info.name) ?? string.Empty;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_paReferenced) PortAudioNative.PaUnref();
    }
}
