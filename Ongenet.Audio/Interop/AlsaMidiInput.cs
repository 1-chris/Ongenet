using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Audio.Interop;

/// <summary>
/// Linux MIDI input via ALSA rawmidi. Supports multiple simultaneous input ports.
/// </summary>
public sealed class AlsaMidiInput : IMidiInputBackend
{
    private const int BufSize = 256;
    private const int PollTimeoutMs = 100;

    private readonly object _lock = new();
    private readonly Dictionary<string, DeviceConnection> _connections = new(StringComparer.Ordinal);
    private readonly List<MidiDeviceInfo> _connectedList = new();
    private Action<MidiMessage>? _onMessage;

    public bool IsCapturing { get; private set; }

    public IReadOnlyList<MidiDeviceInfo> ConnectedDevices => _connectedList;

    public IReadOnlyList<MidiDeviceInfo> EnumerateDevices()
    {
        var list = new List<MidiDeviceInfo>();
        var card = -1;
        while (AlsaMidiNative.snd_card_next(ref card) == 0 && card >= 0)
        {
            var ctlName = $"hw:{card}";
            if (AlsaMidiNative.snd_ctl_open(out var ctl, ctlName, 0) < 0) continue;
            try
            {
                var cardName = CardName(ctl, ctlName);
                var dev = -1;
                while (AlsaMidiNative.snd_ctl_rawmidi_next_device(ctl, ref dev) == 0 && dev >= 0)
                {
                    var port = InputPortName(ctl, dev);
                    if (port is null) continue;
                    var display = port.Length == 0 || port == cardName ? cardName : $"{cardName} — {port}";
                    list.Add(new MidiDeviceInfo(display, $"hw:{card},{dev}"));
                }
            }
            finally
            {
                AlsaMidiNative.snd_ctl_close(ctl);
            }
        }

        return list;
    }

    private static string CardName(IntPtr ctl, string fallback)
    {
        if (AlsaMidiNative.snd_ctl_card_info_malloc(out var info) != 0) return fallback;
        try
        {
            if (AlsaMidiNative.snd_ctl_card_info(ctl, info) != 0) return fallback;
            var p = AlsaMidiNative.snd_ctl_card_info_get_name(info);
            return p == IntPtr.Zero ? fallback : Marshal.PtrToStringAnsi(p) ?? fallback;
        }
        finally
        {
            AlsaMidiNative.snd_ctl_card_info_free(info);
        }
    }

    private static string? InputPortName(IntPtr ctl, int dev)
    {
        if (AlsaMidiNative.snd_rawmidi_info_malloc(out var info) != 0) return null;
        try
        {
            AlsaMidiNative.snd_rawmidi_info_set_device(info, (uint)dev);
            AlsaMidiNative.snd_rawmidi_info_set_subdevice(info, 0);
            AlsaMidiNative.snd_rawmidi_info_set_stream(info, AlsaMidiNative.SND_RAWMIDI_STREAM_INPUT);
            if (AlsaMidiNative.snd_ctl_rawmidi_info(ctl, info) != 0) return null;
            var p = AlsaMidiNative.snd_rawmidi_info_get_name(info);
            return p == IntPtr.Zero ? $"MIDI {dev}" : Marshal.PtrToStringAnsi(p) ?? $"MIDI {dev}";
        }
        finally
        {
            AlsaMidiNative.snd_rawmidi_info_free(info);
        }
    }

    public void Connect(MidiDeviceInfo device, Action<MidiMessage> onMessage)
    {
        lock (_lock)
        {
            _onMessage = onMessage;
            if (_connections.ContainsKey(device.OpenId)) return;

            var rc = AlsaMidiNative.snd_rawmidi_open(out var handle, out _, device.OpenId,
                AlsaMidiNative.SND_RAWMIDI_NONBLOCK);
            if (rc < 0)
                throw new InvalidOperationException(
                    $"snd_rawmidi_open({device.OpenId}) failed: {AlsaMidiNative.ErrorText(rc)}");

            var parser = new MidiRunningStatusParser();
            var conn = new DeviceConnection(device, handle, parser);
            conn.Running = true;
            conn.Thread = new Thread(() => ReadLoop(conn)) { IsBackground = true, Name = $"ALSA MIDI In ({device.DisplayName})" };
            conn.Thread.Start();
            _connections[device.OpenId] = conn;
            _connectedList.Add(device);
            IsCapturing = true;
        }
    }

    public void Disconnect(MidiDeviceInfo device)
    {
        lock (_lock)
        {
            if (!_connections.Remove(device.OpenId, out var conn)) return;
            conn.Running = false;
            conn.Thread?.Join(1000);
            if (conn.Handle != IntPtr.Zero)
            {
                AlsaMidiNative.snd_rawmidi_close(conn.Handle);
                conn.Handle = IntPtr.Zero;
            }

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
                conn.Running = false;
                conn.Thread?.Join(1000);
                if (conn.Handle != IntPtr.Zero)
                {
                    AlsaMidiNative.snd_rawmidi_close(conn.Handle);
                    conn.Handle = IntPtr.Zero;
                }
            }

            _connections.Clear();
            _connectedList.Clear();
            IsCapturing = false;
        }
    }

    private unsafe void ReadLoop(DeviceConnection conn)
    {
        var handle = conn.Handle;
        var openId = conn.Device.OpenId;
        if (handle == IntPtr.Zero) return;

        var count = AlsaMidiNative.snd_rawmidi_poll_descriptors_count(handle);
        if (count < 1) count = 1;
        var pfdSize = Marshal.SizeOf<AlsaMidiNative.PollFd>();
        var pfds = Marshal.AllocHGlobal(count * pfdSize);
        var buf = Marshal.AllocHGlobal(BufSize);

        try
        {
            if (AlsaMidiNative.snd_rawmidi_poll_descriptors(handle, pfds, (uint)count) < 0) return;

            while (conn.Running)
            {
                var pr = AlsaMidiNative.poll(pfds, (nuint)count, PollTimeoutMs);
                if (pr < 0)
                {
                    if (Marshal.GetLastPInvokeError() == AlsaMidiNative.EINTR) continue;
                    break;
                }

                if (pr == 0) continue;

                while (true)
                {
                    var n = AlsaMidiNative.snd_rawmidi_read(handle, buf, (nuint)BufSize);
                    if (n > 0)
                    {
                        var span = new ReadOnlySpan<byte>((void*)buf, (int)n);
                        var cb = _onMessage;
                        if (cb is not null)
                            conn.Parser.Push(span, m => cb(m.WithSource(openId)));
                        if ((int)n < BufSize) break;
                        continue;
                    }

                    if (n == -AlsaMidiNative.EAGAIN) break;
                    if (n == -AlsaMidiNative.EINTR) continue;
                    conn.Running = false;
                    break;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
            Marshal.FreeHGlobal(pfds);
            lock (_lock)
            {
                if (conn.Handle != IntPtr.Zero)
                {
                    AlsaMidiNative.snd_rawmidi_close(conn.Handle);
                    conn.Handle = IntPtr.Zero;
                }
            }
        }
    }

    public void Dispose() => DisconnectAll();

    private sealed class DeviceConnection
    {
        public DeviceConnection(MidiDeviceInfo device, IntPtr handle, MidiRunningStatusParser parser)
        {
            Device = device;
            Handle = handle;
            Parser = parser;
        }

        public MidiDeviceInfo Device { get; }
        public IntPtr Handle { get; set; }
        public MidiRunningStatusParser Parser { get; }
        public Thread? Thread { get; set; }
        public volatile bool Running;
    }
}
