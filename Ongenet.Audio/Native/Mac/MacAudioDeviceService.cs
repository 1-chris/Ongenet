using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ongenet.Audio.Interop;
using Ongenet.Core.Audio;
using CA = Ongenet.Audio.Interop.CoreAudioNative;

namespace Ongenet.Audio.Native.Mac;

/// <summary>
/// <see cref="IAudioDeviceService"/> for macOS CoreAudio. Enumerates the system's audio devices and the
/// default in/out via the HAL property API, tagging each "CoreAudio" with a "ca:&lt;AudioDeviceID&gt;"
/// id. Mirrors the other backends' device services (selection + change events; streams reopen on change).
/// </summary>
internal sealed class MacAudioDeviceService : IAudioDeviceService
{
    private readonly object _lock = new();
    private List<AudioDevice> _inputs = new();
    private List<AudioDevice> _outputs = new();
    private AudioDevice? _selectedInput;
    private AudioDevice? _selectedOutput;
    private AudioInputChannelMode _inputChannelMode = AudioInputChannelMode.Stereo;

    public MacAudioDeviceService()
    {
        try { Enumerate(); } catch { /* no CoreAudio → empty lists, app stays alive */ }
    }

    public IReadOnlyList<AudioDevice> InputDevices { get { lock (_lock) return _inputs; } }
    public IReadOnlyList<AudioDevice> OutputDevices { get { lock (_lock) return _outputs; } }

    public AudioDevice? SelectedOutput
    {
        get { lock (_lock) return _selectedOutput; }
        set { lock (_lock) { if (Equals(_selectedOutput, value)) return; _selectedOutput = value; } OutputChanged?.Invoke(); }
    }

    public AudioDevice? SelectedInput
    {
        get { lock (_lock) return _selectedInput; }
        set { lock (_lock) { if (Equals(_selectedInput, value)) return; _selectedInput = value; } InputChanged?.Invoke(); }
    }

    public AudioInputChannelMode InputChannelMode
    {
        get { lock (_lock) return _inputChannelMode; }
        set { lock (_lock) { if (_inputChannelMode == value) return; _inputChannelMode = value; } InputChanged?.Invoke(); }
    }

    public event Action? DevicesChanged;
    public event Action? OutputChanged;
    public event Action? InputChanged;

    public void Refresh()
    {
        try { Enumerate(); } catch { /* keep prior lists */ }
        DevicesChanged?.Invoke();
    }

    private void Enumerate()
    {
        var inputs = new List<AudioDevice>();
        var outputs = new List<AudioDevice>();

        var ids = ReadUInt32Array(CA.kAudioObjectSystemObject, CA.kAudioHardwarePropertyDevices, CA.kAudioObjectPropertyScopeGlobal);
        var defOut = ReadUInt32(CA.kAudioObjectSystemObject, CA.kAudioHardwarePropertyDefaultOutputDevice, CA.kAudioObjectPropertyScopeGlobal);
        var defIn = ReadUInt32(CA.kAudioObjectSystemObject, CA.kAudioHardwarePropertyDefaultInputDevice, CA.kAudioObjectPropertyScopeGlobal);

        foreach (var id in ids)
        {
            var name = ReadDeviceName(id) ?? $"Device {id}";
            var outCh = CountChannels(id, CA.kAudioObjectPropertyScopeOutput);
            var inCh = CountChannels(id, CA.kAudioObjectPropertyScopeInput);
            var device = new AudioDevice((int)id, name, "CoreAudio", inCh, outCh, id == defIn, id == defOut, Id: $"ca:{id}");
            if (device.SupportsOutput) outputs.Add(device);
            if (device.SupportsInput) inputs.Add(device);
        }

        lock (_lock)
        {
            _outputs = outputs;
            _inputs = inputs;
            _selectedOutput = Reconcile(_selectedOutput, outputs);
            _selectedInput = Reconcile(_selectedInput, inputs);
        }
    }

    private static AudioDevice? Reconcile(AudioDevice? current, List<AudioDevice> devices)
    {
        if (current is not null)
            foreach (var d in devices)
                if (d.Id == current.Id) return d;
        foreach (var d in devices)
            if (d.IsDefaultOutput || d.IsDefaultInput) return d;
        return devices.Count > 0 ? devices[0] : null;
    }

    // --- HAL property helpers --------------------------------------------------------------------

    private static byte[]? ReadProperty(uint obj, uint selector, uint scope)
    {
        var addr = new CA.AudioObjectPropertyAddress { mSelector = selector, mScope = scope, mElement = CA.kAudioObjectPropertyElementMain };
        if (CA.AudioObjectGetPropertyDataSize(obj, ref addr, 0, IntPtr.Zero, out var size) != 0 || size == 0) return null;
        var data = new byte[size];
        var io = size;
        var h = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            return CA.AudioObjectGetPropertyData(obj, ref addr, 0, IntPtr.Zero, ref io, h.AddrOfPinnedObject()) == 0 ? data : null;
        }
        finally { h.Free(); }
    }

    private static uint[] ReadUInt32Array(uint obj, uint selector, uint scope)
    {
        var data = ReadProperty(obj, selector, scope);
        if (data is null) return Array.Empty<uint>();
        var result = new uint[data.Length / 4];
        for (var i = 0; i < result.Length; i++) result[i] = BitConverter.ToUInt32(data, i * 4);
        return result;
    }

    private static uint ReadUInt32(uint obj, uint selector, uint scope)
    {
        var data = ReadProperty(obj, selector, scope);
        return data is { Length: >= 4 } ? BitConverter.ToUInt32(data, 0) : 0;
    }

    // The device name property returns a CFStringRef (one pointer); marshal it to a managed string.
    private static string? ReadDeviceName(uint id)
    {
        var data = ReadProperty(id, CA.kAudioDevicePropertyDeviceNameCFString, CA.kAudioObjectPropertyScopeGlobal);
        if (data is null || data.Length < IntPtr.Size) return null;
        var cfStr = (IntPtr)BitConverter.ToInt64(data, 0);
        if (cfStr == IntPtr.Zero) return null;

        var buffer = new byte[512];
        return CoreMidiNative.CFStringGetCString(cfStr, buffer, buffer.Length, CoreMidiNative.kCFStringEncodingUTF8)
            ? System.Text.Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0) is var z && z >= 0 ? z : buffer.Length).Trim()
            : null;
    }

    // Sums the channels across an AudioBufferList returned by the StreamConfiguration property for a scope.
    private static int CountChannels(uint id, uint scope)
    {
        var data = ReadProperty(id, CA.kAudioDevicePropertyStreamConfiguration, scope);
        if (data is null || data.Length < 4) return 0;
        var numBuffers = BitConverter.ToUInt32(data, 0);
        var total = 0;
        // AudioBufferList: mNumberBuffers (4) + 4 pad, then AudioBuffer[16] { mNumberChannels, mDataByteSize, void* }.
        for (var b = 0; b < numBuffers; b++)
        {
            var off = 8 + b * 16;
            if (off + 4 > data.Length) break;
            total += (int)BitConverter.ToUInt32(data, off);
        }

        return total;
    }
}
