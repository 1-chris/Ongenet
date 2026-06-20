using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Audio.Interop;

/// <summary>
/// Linux MIDI input via ALSA rawmidi. Enumerates hardware/USB input ports through the control API,
/// then opens the chosen port non-blocking and drains it on a dedicated thread that waits with
/// <c>poll()</c>. Raw bytes are fed through a <see cref="MidiRunningStatusParser"/>.
///
/// Shutdown is race-free: <see cref="Stop"/> flips a flag and joins; the read thread notices on its
/// next poll timeout, exits, and closes the handle on its own thread (closing a handle another thread
/// is blocked in is unsafe, hence the non-blocking + poll-timeout design rather than a blocking read).
/// </summary>
public sealed class AlsaMidiInput : IMidiInputBackend
{
    private const int BufSize = 256;
    private const int PollTimeoutMs = 100;

    private readonly object _lock = new();
    private readonly MidiRunningStatusParser _parser = new();

    private IntPtr _handle;
    private Thread? _thread;
    private Action<MidiMessage>? _onMessage;
    private volatile bool _running;

    public bool IsCapturing { get; private set; }

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
                    if (port is null) continue; // device has no input stream
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

    // Returns the input port name for the device, or null when the device has no input stream.
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

    public void Start(MidiDeviceInfo device, Action<MidiMessage> onMessage)
    {
        Stop(); // tear down any previous session (joins its thread) before reopening

        lock (_lock)
        {
            var rc = AlsaMidiNative.snd_rawmidi_open(out _handle, IntPtr.Zero, device.OpenId,
                AlsaMidiNative.SND_RAWMIDI_NONBLOCK);
            if (rc < 0)
            {
                _handle = IntPtr.Zero;
                throw new InvalidOperationException(
                    $"snd_rawmidi_open({device.OpenId}) failed: {AlsaMidiNative.ErrorText(rc)}");
            }

            _onMessage = onMessage;
            _parser.Reset();
            _running = true;
            _thread = new Thread(ReadLoop) { IsBackground = true, Name = "ALSA MIDI In" };
            _thread.Start();
            IsCapturing = true;
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_lock)
        {
            if (!_running && _thread is null) return;
            _running = false;
            thread = _thread;
            _thread = null;
        }

        thread?.Join(1000);

        lock (_lock)
        {
            // The read thread normally closes the handle itself on exit; close defensively if it didn't.
            if (_handle != IntPtr.Zero)
            {
                AlsaMidiNative.snd_rawmidi_close(_handle);
                _handle = IntPtr.Zero;
            }

            _onMessage = null;
            IsCapturing = false;
        }
    }

    private unsafe void ReadLoop()
    {
        IntPtr handle;
        Action<MidiMessage>? onMessage;
        lock (_lock)
        {
            handle = _handle;
            onMessage = _onMessage;
        }

        if (handle == IntPtr.Zero || onMessage is null) return;

        var count = AlsaMidiNative.snd_rawmidi_poll_descriptors_count(handle);
        if (count < 1) count = 1;
        var pfdSize = Marshal.SizeOf<AlsaMidiNative.PollFd>();
        var pfds = Marshal.AllocHGlobal(count * pfdSize);
        var buf = Marshal.AllocHGlobal(BufSize);

        try
        {
            if (AlsaMidiNative.snd_rawmidi_poll_descriptors(handle, pfds, (uint)count) < 0) return;

            while (_running)
            {
                var pr = AlsaMidiNative.poll(pfds, (nuint)count, PollTimeoutMs);
                if (pr < 0)
                {
                    if (Marshal.GetLastPInvokeError() == AlsaMidiNative.EINTR) continue;
                    break;
                }

                if (pr == 0) continue; // timeout — re-check _running

                // Drain everything currently available before polling again.
                while (true)
                {
                    var n = AlsaMidiNative.snd_rawmidi_read(handle, buf, (nuint)BufSize);
                    if (n > 0)
                    {
                        var span = new ReadOnlySpan<byte>((void*)buf, (int)n);
                        _parser.Push(span, onMessage);
                        if ((int)n < BufSize) break; // likely drained
                        continue;                     // buffer was full — read more
                    }

                    if (n == -AlsaMidiNative.EAGAIN) break;  // nothing more right now
                    if (n == -AlsaMidiNative.EINTR) continue;

                    // Any other negative result (e.g. -ENODEV on unplug) ends the session.
                    _running = false;
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
                if (_handle != IntPtr.Zero)
                {
                    AlsaMidiNative.snd_rawmidi_close(_handle);
                    _handle = IntPtr.Zero;
                }
            }
        }
    }

    public void Dispose() => Stop();
}
