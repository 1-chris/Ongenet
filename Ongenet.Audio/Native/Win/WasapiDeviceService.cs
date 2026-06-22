using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Ongenet.Core.Audio;
using W = Ongenet.Audio.Interop.WasapiNative;

namespace Ongenet.Audio.Native.Win;

/// <summary>
/// <see cref="IAudioDeviceService"/> for Windows WASAPI. Enumerates active render/capture endpoints via
/// <c>IMMDeviceEnumerator</c> (tagging each "WASAPI" with a "wasapi:&lt;endpointId&gt;" id) and marks the
/// system defaults. Each render endpoint also gets a "Loopback: &lt;name&gt;" input
/// ("wasapi:loopback:&lt;id&gt;") to record whatever is playing on it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WasapiDeviceService : IAudioDeviceService
{
    private readonly object _lock = new();
    private List<AudioDevice> _inputs = new();
    private List<AudioDevice> _outputs = new();
    private AudioDevice? _selectedInput;
    private AudioDevice? _selectedOutput;
    private AudioInputChannelMode _inputChannelMode = AudioInputChannelMode.Stereo;

    public WasapiDeviceService()
    {
        try { Enumerate(); } catch { /* no WASAPI / wrong COM apartment → empty lists, app stays alive */ }
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

        var enumPtr = IntPtr.Zero;
        object? enumObj = null;
        try
        {
            if (W.CoCreateInstance(ref W.CLSID_MMDeviceEnumerator, IntPtr.Zero, W.CLSCTX_ALL, ref W.IID_IMMDeviceEnumerator, out enumPtr) < 0)
                return;
            enumObj = Marshal.GetObjectForIUnknown(enumPtr);
            var devEnum = (W.IMMDeviceEnumerator)enumObj;

            var defRender = DefaultId(devEnum, W.eRender);
            var defCapture = DefaultId(devEnum, W.eCapture);
            var index = 0;

            foreach (var (id, name) in Endpoints(devEnum, W.eRender))
            {
                var isDef = id == defRender;
                outputs.Add(new AudioDevice(index++, name, "WASAPI", 0, 2, false, isDef, "wasapi:" + id));
                // Loopback input: record whatever is playing on this output (e.g. a browser).
                inputs.Add(new AudioDevice(index++, $"Loopback: {name}", "WASAPI", 2, 0, false, false, "wasapi:loopback:" + id));
            }

            foreach (var (id, name) in Endpoints(devEnum, W.eCapture))
            {
                var isDef = id == defCapture;
                inputs.Add(new AudioDevice(index++, name, "WASAPI", 2, 0, isDef, false, "wasapi:" + id));
            }
        }
        finally
        {
            if (enumObj is not null) Marshal.ReleaseComObject(enumObj);
            if (enumPtr != IntPtr.Zero) Marshal.Release(enumPtr);
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

    private static IEnumerable<(string id, string name)> Endpoints(W.IMMDeviceEnumerator devEnum, int dataFlow)
    {
        var result = new List<(string, string)>();
        if (devEnum.EnumAudioEndpoints(dataFlow, W.DEVICE_STATE_ACTIVE, out var collPtr) < 0 || collPtr == IntPtr.Zero)
            return result;

        var collObj = Marshal.GetObjectForIUnknown(collPtr);
        try
        {
            var coll = (W.IMMDeviceCollection)collObj;
            if (coll.GetCount(out var count) < 0) return result;
            for (uint i = 0; i < count; i++)
            {
                if (coll.Item(i, out var devPtr) < 0 || devPtr == IntPtr.Zero) continue;
                var devObj = Marshal.GetObjectForIUnknown(devPtr);
                try
                {
                    var device = (W.IMMDevice)devObj;
                    var id = ReadId(device);
                    if (id is null) continue;
                    result.Add((id, ReadFriendlyName(device) ?? id));
                }
                finally
                {
                    Marshal.ReleaseComObject(devObj);
                    Marshal.Release(devPtr);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(collObj);
            Marshal.Release(collPtr);
        }

        return result;
    }

    private static string? DefaultId(W.IMMDeviceEnumerator devEnum, int dataFlow)
    {
        if (devEnum.GetDefaultAudioEndpoint(dataFlow, W.eConsole, out var devPtr) < 0 || devPtr == IntPtr.Zero) return null;
        var devObj = Marshal.GetObjectForIUnknown(devPtr);
        try { return ReadId((W.IMMDevice)devObj); }
        finally { Marshal.ReleaseComObject(devObj); Marshal.Release(devPtr); }
    }

    private static string? ReadId(W.IMMDevice device)
    {
        if (device.GetId(out var p) < 0 || p == IntPtr.Zero) return null;
        var s = Marshal.PtrToStringUni(p);
        W.CoTaskMemFree(p);
        return s;
    }

    private static string? ReadFriendlyName(W.IMMDevice device)
    {
        if (device.OpenPropertyStore(W.STGM_READ, out var psPtr) < 0 || psPtr == IntPtr.Zero) return null;
        var psObj = Marshal.GetObjectForIUnknown(psPtr);
        try
        {
            var ps = (W.IPropertyStore)psObj;
            var key = new W.PROPERTYKEY { fmtid = W.PKEY_Device_FriendlyName_fmtid, pid = W.PKEY_Device_FriendlyName_pid };
            if (ps.GetValue(ref key, out var pv) < 0) return null;
            var name = pv.vt == W.VT_LPWSTR && pv.data != IntPtr.Zero ? Marshal.PtrToStringUni(pv.data) : null;
            W.PropVariantClear(ref pv);
            return name;
        }
        finally
        {
            Marshal.ReleaseComObject(psObj);
            Marshal.Release(psPtr);
        }
    }
}
