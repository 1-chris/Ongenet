using System;
using System.Collections.Generic;
using System.Globalization;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Audio.Interop;

/// <summary>
/// Windows MIDI input via winmm. Short channel-voice messages arrive already framed in the callback's
/// dwParam1, so they're decoded directly with <see cref="MidiRunningStatusParser.Decode"/> rather than
/// run through the byte parser. SysEx (MIM_LONGDATA) is not handled in v1.
///
/// The callback runs on a winmm-owned thread; per the API contract no midiIn* function may be called
/// from inside it, so teardown happens only from <see cref="Stop"/>. The delegate is held in a field
/// for the open lifetime so the GC cannot collect it out from under native code.
/// </summary>
public sealed class WinMmMidiInput : IMidiInputBackend
{
    private readonly object _lock = new();

    private WinMmMidiNative.MidiInProc? _proc; // rooted for the handle's lifetime
    private Action<MidiMessage>? _onMessage;
    private IntPtr _handle;

    public bool IsCapturing { get; private set; }

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

    public void Start(MidiDeviceInfo device, Action<MidiMessage> onMessage)
    {
        Stop();

        if (!uint.TryParse(device.OpenId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            throw new InvalidOperationException($"Invalid MIDI device id '{device.OpenId}'.");

        lock (_lock)
        {
            _onMessage = onMessage;
            _proc = OnMidiInProc;
            var rc = WinMmMidiNative.midiInOpen(out _handle, id, _proc, IntPtr.Zero,
                WinMmMidiNative.CALLBACK_FUNCTION);
            if (rc != 0)
            {
                _handle = IntPtr.Zero;
                _proc = null;
                _onMessage = null;
                throw new InvalidOperationException($"midiInOpen({id}) failed with code {rc}.");
            }

            WinMmMidiNative.midiInStart(_handle);
            IsCapturing = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_handle != IntPtr.Zero)
            {
                WinMmMidiNative.midiInStop(_handle);
                WinMmMidiNative.midiInReset(_handle);
                WinMmMidiNative.midiInClose(_handle);
                _handle = IntPtr.Zero;
            }

            _proc = null;
            _onMessage = null;
            IsCapturing = false;
        }
    }

    // winmm callback thread. Decode short messages from dwParam1; ignore everything else (SysEx, errors).
    private void OnMidiInProc(IntPtr hMidiIn, uint wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)
    {
        if (wMsg != WinMmMidiNative.MIM_DATA) return;

        var packed = (uint)(dwParam1.ToInt64() & 0xFFFFFF);
        var status = (byte)(packed & 0xFF);
        if (status < 0x80 || status >= 0xF0) return; // only channel-voice messages

        var d1 = (byte)((packed >> 8) & 0x7F);
        var d2 = (byte)((packed >> 16) & 0x7F);

        var cb = _onMessage;
        cb?.Invoke(MidiRunningStatusParser.Decode(status, d1, d2));
    }

    public void Dispose() => Stop();
}
