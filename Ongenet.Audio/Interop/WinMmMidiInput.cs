using System;
using System.Collections.Generic;
using System.Globalization;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Audio.Interop;

/// <summary>
/// Windows MIDI input via winmm. Supports multiple simultaneous input devices.
/// </summary>
public sealed class WinMmMidiInput : IMidiInputBackend
{
    private readonly object _lock = new();
    private readonly Dictionary<string, DeviceConnection> _connections = new(StringComparer.Ordinal);
    private readonly List<MidiDeviceInfo> _connectedList = new();
    private Action<MidiMessage>? _onMessage;

    public bool IsCapturing { get; private set; }

    public IReadOnlyList<MidiDeviceInfo> ConnectedDevices => _connectedList;

    public IReadOnlyList<MidiDeviceInfo> EnumerateDevices()
    {
        var list = new List<MidiDeviceInfo>();
        var count = WinMmMidiNative.midiInGetNumDevs();
        for (uint i = 0; i < count; i++)
        {
            var caps = default(WinMmMidiNative.MIDIINCAPS);
            if (WinMmMidiNative.midiInGetDevCaps((UIntPtr)i, ref caps,
                    (uint)System.Runtime.InteropServices.Marshal.SizeOf<WinMmMidiNative.MIDIINCAPS>()) != 0)
                continue;
            var name = string.IsNullOrEmpty(caps.szPname) ? $"MIDI input {i}" : caps.szPname;
            list.Add(new MidiDeviceInfo(name, i.ToString(CultureInfo.InvariantCulture)));
        }

        return list;
    }

    public void Connect(MidiDeviceInfo device, Action<MidiMessage> onMessage)
    {
        if (!uint.TryParse(device.OpenId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            throw new InvalidOperationException($"Invalid MIDI device id '{device.OpenId}'.");

        lock (_lock)
        {
            _onMessage = onMessage;
            if (_connections.ContainsKey(device.OpenId)) return;

            var proc = new WinMmMidiNative.MidiInProc((h, w, inst, p1, p2) =>
                OnMidiInProc(device.OpenId, w, p1));
            var rc = WinMmMidiNative.midiInOpen(out var handle, id, proc, IntPtr.Zero,
                WinMmMidiNative.CALLBACK_FUNCTION);
            if (rc != 0)
                throw new InvalidOperationException($"midiInOpen({id}) failed with code {rc}.");

            WinMmMidiNative.midiInStart(handle);
            _connections[device.OpenId] = new DeviceConnection(device, handle, proc);
            _connectedList.Add(device);
            IsCapturing = true;
        }
    }

    public void Disconnect(MidiDeviceInfo device)
    {
        lock (_lock)
        {
            if (!_connections.Remove(device.OpenId, out var conn)) return;
            WinMmMidiNative.midiInStop(conn.Handle);
            WinMmMidiNative.midiInReset(conn.Handle);
            WinMmMidiNative.midiInClose(conn.Handle);
            _connectedList.RemoveAll(d => d.OpenId == device.OpenId);
            IsCapturing = _connections.Count > 0;
        }
    }

    public void DisconnectAll()
    {
        lock (_lock)
        {
            foreach (var conn in _connections.Values)
            {
                WinMmMidiNative.midiInStop(conn.Handle);
                WinMmMidiNative.midiInReset(conn.Handle);
                WinMmMidiNative.midiInClose(conn.Handle);
            }

            _connections.Clear();
            _connectedList.Clear();
            IsCapturing = false;
        }
    }

    private void OnMidiInProc(string openId, uint wMsg, IntPtr dwParam1)
    {
        if (wMsg != WinMmMidiNative.MIM_DATA) return;

        var packed = (uint)(dwParam1.ToInt64() & 0xFFFFFF);
        var status = (byte)(packed & 0xFF);
        if (status < 0x80 || status >= 0xF0) return;

        var d1 = (byte)((packed >> 8) & 0x7F);
        var d2 = (byte)((packed >> 16) & 0x7F);

        var cb = _onMessage;
        cb?.Invoke(MidiRunningStatusParser.Decode(status, d1, d2).WithSource(openId));
    }

    public void Dispose() => DisconnectAll();

    private sealed record DeviceConnection(MidiDeviceInfo Device, IntPtr Handle, WinMmMidiNative.MidiInProc Proc);
}
