using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ongenet.Audio.Interop;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native;

/// <summary>
/// PulseAudio driver (libpulse) for the native backend. Enumerates the server's sinks/sources and its
/// default device through the async core API (one synchronous pump of a <c>pa_mainloop</c> at startup),
/// then streams through the blocking simple API (<see cref="PulseStream"/>). Streams mix with other
/// apps and follow the chosen device — and because <c>pipewire-pulse</c> implements this protocol, it
/// works on a PipeWire desktop, where (unlike the bare ALSA <c>default</c> PCM) it correctly resolves
/// the real default sink.
/// </summary>
internal sealed unsafe class PulseAudioDriver : INativeAudioDriver
{
    public string HostApi => "PulseAudio";
    public string IdPrefix => "pulse:";

    private bool? _available;
    public bool IsAvailable => _available ??= PulseAudioNative.TryProbe();

    // Collects the enumeration results; reached from the native callbacks via a GCHandle userdata.
    private sealed class Collector
    {
        public readonly List<(string name, string desc)> Sinks = new();
        public readonly List<(string name, string desc)> Sources = new();
        public string? DefaultSink;
        public string? DefaultSource;
    }

    public void Enumerate(List<AudioDevice> outputs, List<AudioDevice> inputs)
    {
        if (!IsAvailable) return;

        var ml = PulseAudioNative.pa_mainloop_new();
        if (ml == IntPtr.Zero) return;

        var col = new Collector();
        var gch = GCHandle.Alloc(col);
        var ud = GCHandle.ToIntPtr(gch);
        try
        {
            var api = PulseAudioNative.pa_mainloop_get_api(ml);
            var ctx = PulseAudioNative.pa_context_new(api, "Ongenet");
            if (ctx == IntPtr.Zero) return;

            try
            {
                if (PulseAudioNative.pa_context_connect(ctx, null, 0, IntPtr.Zero) < 0) return;

                // Pump until the context connects (or fails). The guard bounds a server that never answers.
                var guard = 0;
                while (true)
                {
                    var st = PulseAudioNative.pa_context_get_state(ctx);
                    if (st == PulseAudioNative.PA_CONTEXT_READY) break;
                    if (st is PulseAudioNative.PA_CONTEXT_FAILED or PulseAudioNative.PA_CONTEXT_TERMINATED) return;
                    if (PulseAudioNative.pa_mainloop_iterate(ml, 1, IntPtr.Zero) < 0) return;
                    if (++guard > 5000) return;
                }

                RunOp(ml, PulseAudioNative.pa_context_get_sink_info_list(ctx, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, IntPtr, void>)&SinkCb, ud));
                RunOp(ml, PulseAudioNative.pa_context_get_source_info_list(ctx, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, IntPtr, void>)&SourceCb, ud));
                RunOp(ml, PulseAudioNative.pa_context_get_server_info(ctx, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&ServerCb, ud));
            }
            finally
            {
                PulseAudioNative.pa_context_disconnect(ctx);
                PulseAudioNative.pa_context_unref(ctx);
            }
        }
        finally
        {
            gch.Free();
            PulseAudioNative.pa_mainloop_free(ml);
        }

        var index = 0;
        foreach (var (name, desc) in col.Sinks)
        {
            var isDef = name == col.DefaultSink;
            outputs.Add(new AudioDevice(index++, Friendly(desc, name), HostApi, 0, 2, isDef, isDef, IdPrefix + name));
        }

        foreach (var (name, desc) in col.Sources)
        {
            // Monitor sources (".monitor") capture whatever is playing on a sink — this is how you record
            // another app (e.g. a browser): pick its sink's monitor as the input. Real mics come through too.
            var isDef = name == col.DefaultSource;
            var label = name.EndsWith(".monitor", StringComparison.Ordinal) && !desc.StartsWith("Monitor", StringComparison.OrdinalIgnoreCase)
                ? $"Monitor: {Friendly(desc, name)}"
                : Friendly(desc, name);
            inputs.Add(new AudioDevice(index++, label, HostApi, 2, 0, isDef, isDef, IdPrefix + name));
        }
    }

    public INativeStream OpenOutput(AudioDevice device, int channels, AudioRenderCallback render)
        => PulseStream.Open(DeviceName(device), playback: true, channels, render, null);

    public INativeStream OpenInput(AudioDevice device, int channels, AudioCaptureCallback capture)
        => PulseStream.Open(DeviceName(device), playback: false, channels, null, capture);

    // The Pulse sink/source name behind our tagged id (null → server default).
    private string? DeviceName(AudioDevice device)
        => device.Id.StartsWith(IdPrefix, StringComparison.Ordinal) ? device.Id[IdPrefix.Length..] : device.Name;

    private static string Friendly(string desc, string name) => string.IsNullOrEmpty(desc) ? name : desc;

    // Pumps the mainloop until a listing/info operation completes, then releases it.
    private static void RunOp(IntPtr ml, IntPtr op)
    {
        if (op == IntPtr.Zero) return;
        var guard = 0;
        while (PulseAudioNative.pa_operation_get_state(op) == PulseAudioNative.PA_OPERATION_RUNNING)
        {
            if (PulseAudioNative.pa_mainloop_iterate(ml, 1, IntPtr.Zero) < 0) break;
            if (++guard > 5000) break;
        }

        PulseAudioNative.pa_operation_unref(op);
    }

    // --- native callbacks (read only the name@0 and description@16 fields of the info structs) --------

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void SinkCb(IntPtr c, IntPtr info, int eol, IntPtr ud) => Collect(info, eol, ud, sink: true);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void SourceCb(IntPtr c, IntPtr info, int eol, IntPtr ud) => Collect(info, eol, ud, sink: false);

    private static void Collect(IntPtr info, int eol, IntPtr ud, bool sink)
    {
        if (eol != 0 || info == IntPtr.Zero) return;
        try
        {
            var col = (Collector?)GCHandle.FromIntPtr(ud).Target;
            if (col is null) return;
            var name = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(info, 0));
            if (string.IsNullOrEmpty(name)) return;
            var desc = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(info, 16)) ?? name;
            (sink ? col.Sinks : col.Sources).Add((name, desc));
        }
        catch
        {
            // Never throw back into libpulse.
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void ServerCb(IntPtr c, IntPtr info, IntPtr ud)
    {
        if (info == IntPtr.Zero) return;
        try
        {
            var col = (Collector?)GCHandle.FromIntPtr(ud).Target;
            if (col is null) return;
            // pa_server_info: 4 char* (32) + pa_sample_spec (12, padded to 16) → default_sink_name@48, default_source_name@56.
            col.DefaultSink = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(info, 48));
            col.DefaultSource = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(info, 56));
        }
        catch
        {
            // Never throw back into libpulse.
        }
    }
}
