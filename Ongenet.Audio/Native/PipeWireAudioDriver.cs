using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Ongenet.Audio.Interop;
using Ongenet.Core.Audio;
using PW = Ongenet.Audio.Interop.PipeWireNative;

namespace Ongenet.Audio.Native;

/// <summary>
/// PipeWire driver (libpipewire-0.3) for the native backend — the lowest-latency, fully-native Linux
/// path. Enumerates the graph's Audio/Sink and Audio/Source nodes via the registry (a transient
/// context+core+registry with an object listener), and streams via <see cref="PipeWireStream"/>
/// (a <c>pw_thread_loop</c> + <c>pw_stream</c> with a hand-built SPA format POD), targeting the chosen
/// node by name or following the system default. Auto-connects and mixes with other apps.
/// </summary>
internal sealed unsafe class PipeWireAudioDriver : INativeAudioDriver
{
    public string HostApi => "PipeWire";
    public string IdPrefix => "pw:";

    private bool? _available;
    public bool IsAvailable => _available ??= PipeWireNative.TryProbe();

    // Collects nodes seen during the registry scan; reached from the global callback via a GCHandle.
    private const string MonitorPrefix = "monitor:";
    private const string AppPrefix = "app:";

    private sealed class Collector
    {
        public readonly object Lock = new();
        public readonly List<(string name, string label)> Sinks = new();
        public readonly List<(string name, string label)> Sources = new();
        // Application output streams (e.g. a browser) with their live global id, for per-app capture.
        public readonly List<(uint id, string key, string label)> Apps = new();
    }

    public void Enumerate(List<AudioDevice> outputs, List<AudioDevice> inputs)
    {
        if (!IsAvailable) return;

        // Always offer a "default" entry that follows the server's default routing (marked default so —
        // being first in the driver registry — native PipeWire is the preferred auto-selection).
        outputs.Add(new AudioDevice(0, "PipeWire (default output)", HostApi, 0, 2, false, true, IdPrefix + "default"));
        inputs.Add(new AudioDevice(0, "PipeWire (default input)", HostApi, 2, 0, true, false, IdPrefix + "default"));

        var col = ScanRegistry();
        if (col is null) return;

        var index = 1;
        lock (col.Lock)
        {
            foreach (var (name, label) in col.Sinks)
            {
                outputs.Add(new AudioDevice(index++, label, HostApi, 0, 2, false, false, IdPrefix + name));
                // Each sink's monitor is selectable as an input → record whatever plays on it (e.g. a browser).
                inputs.Add(new AudioDevice(index++, $"Monitor: {label}", HostApi, 2, 0, false, false, IdPrefix + "monitor:" + name));
            }

            foreach (var (name, label) in col.Sources)
                inputs.Add(new AudioDevice(index++, label, HostApi, 2, 0, false, false, IdPrefix + name));

            // Per-app capture: each application currently playing audio is selectable as an input that
            // records ONLY that app's output (isolated from the rest of the mix).
            foreach (var (_, key, label) in col.Apps)
                inputs.Add(new AudioDevice(index++, $"App: {label}", HostApi, 2, 0, false, false, IdPrefix + AppPrefix + key));
        }
    }

    // Spins up a transient PipeWire connection, listens to the registry for ~250 ms to collect the
    // existing Audio/Sink and Audio/Source nodes, then tears everything down.
    private Collector? ScanRegistry()
    {
        PW.EnsureInit();
        var loop = PW.pw_thread_loop_new("ongenet-enum", IntPtr.Zero);
        if (loop == IntPtr.Zero) return null;

        var col = new Collector();
        var gch = GCHandle.Alloc(col);
        var hook = Marshal.AllocHGlobal(PW.SPA_HOOK_SIZE);
        var events = Marshal.AllocHGlobal(PW.REGEVENTS_SIZE);
        IntPtr context = IntPtr.Zero, core = IntPtr.Zero, registry = IntPtr.Zero;
        try
        {
            for (var i = 0; i < PW.SPA_HOOK_SIZE; i++) Marshal.WriteByte(hook, i, 0);
            for (var i = 0; i < PW.REGEVENTS_SIZE; i++) Marshal.WriteByte(events, i, 0);
            Marshal.WriteInt32(events, 0, PW.PW_VERSION_REGISTRY_EVENTS);
            Marshal.WriteIntPtr(events, PW.REGEVENTS_OFF_GLOBAL,
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, uint, uint, IntPtr, uint, IntPtr, void>)&GlobalThunk);

            if (PW.pw_thread_loop_start(loop) != 0) return null;

            PW.pw_thread_loop_lock(loop);
            try
            {
                context = PW.pw_context_new(PW.pw_thread_loop_get_loop(loop), IntPtr.Zero, 0);
                if (context == IntPtr.Zero) return null;
                core = PW.pw_context_connect(context, IntPtr.Zero, 0);
                if (core == IntPtr.Zero) return null;
                registry = PW.CoreGetRegistry(core);
                if (registry == IntPtr.Zero) return null;
                PW.pw_proxy_add_object_listener(registry, hook, events, GCHandle.ToIntPtr(gch));
            }
            finally
            {
                PW.pw_thread_loop_unlock(loop);
            }

            Thread.Sleep(250); // existing globals are delivered on the loop thread shortly after listening

            PW.pw_thread_loop_lock(loop);
            try
            {
                if (registry != IntPtr.Zero) PW.pw_proxy_destroy(registry);
                if (core != IntPtr.Zero) PW.pw_core_disconnect(core);
                if (context != IntPtr.Zero) PW.pw_context_destroy(context);
            }
            finally
            {
                PW.pw_thread_loop_unlock(loop);
            }

            return col;
        }
        catch
        {
            return col; // return whatever we collected before any failure
        }
        finally
        {
            PW.pw_thread_loop_stop(loop);
            PW.pw_thread_loop_destroy(loop);
            Marshal.FreeHGlobal(events);
            Marshal.FreeHGlobal(hook);
            gch.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void GlobalThunk(IntPtr data, uint id, uint permissions, IntPtr type, uint version, IntPtr props)
    {
        try
        {
            if (GCHandle.FromIntPtr(data).Target is not Collector col) return;
            if (Marshal.PtrToStringAnsi(type) != "PipeWire:Interface:Node") return;

            var mediaClass = PW.DictLookup(props, "media.class");

            // An application playing audio (e.g. a browser tab) — selectable for isolated per-app capture.
            if (mediaClass == "Stream/Output/Audio")
            {
                var nodeName = PW.DictLookup(props, "node.name");
                var app = PW.DictLookup(props, "application.name");
                var key = !string.IsNullOrEmpty(nodeName) ? nodeName : app;
                if (string.IsNullOrEmpty(key)) return;
                var media = PW.DictLookup(props, "media.name");
                var lbl = app ?? nodeName ?? "App";
                if (!string.IsNullOrEmpty(media) && media != lbl) lbl = $"{lbl} — {media}";
                lock (col.Lock) col.Apps.Add((id, key!, lbl));
                return;
            }

            var isSink = mediaClass == "Audio/Sink";
            var isSource = mediaClass == "Audio/Source";
            if (!isSink && !isSource) return;

            var name = PW.DictLookup(props, "node.name");
            if (string.IsNullOrEmpty(name) || name.EndsWith(".monitor", StringComparison.Ordinal)) return;
            var label = PW.DictLookup(props, "node.description") ?? PW.DictLookup(props, "node.nick") ?? name;

            lock (col.Lock)
                (isSink ? col.Sinks : col.Sources).Add((name, label));
        }
        catch
        {
            // Never throw back into libpipewire.
        }
    }

    public INativeStream OpenOutput(AudioDevice device, int channels, AudioRenderCallback render)
        => PipeWireStream.Open(playback: true, channels, Target(device), captureSink: false, render, null);

    public INativeStream OpenInput(AudioDevice device, int channels, AudioCaptureCallback capture)
    {
        var node = Node(device);
        if (node is not null && node.StartsWith(MonitorPrefix, StringComparison.Ordinal))
            // "monitor:<sink>" → capture that sink's whole mix (record everything playing on it).
            return PipeWireStream.Open(playback: false, channels, node[MonitorPrefix.Length..], captureSink: true, null, capture);

        if (node is not null && node.StartsWith(AppPrefix, StringComparison.Ordinal))
            // "app:<key>" → record ONLY that application, via explicit port links (WirePlumber-policy-proof).
            return PipeWireAppCapture.Open(node[AppPrefix.Length..], channels, capture);

        return PipeWireStream.Open(playback: false, channels, node, captureSink: false, null, capture);
    }

    // The PipeWire node name to pin to, or null to follow the default ("pw:default").
    private string? Target(AudioDevice device)
    {
        var node = Node(device);
        return node is null
               || node.StartsWith(MonitorPrefix, StringComparison.Ordinal)
               || node.StartsWith(AppPrefix, StringComparison.Ordinal)
            ? null
            : node;
    }


    // The id payload after the "pw:" prefix ("default", a node name, or "monitor:<sink>"), or null.
    private string? Node(AudioDevice device)
    {
        if (!device.Id.StartsWith(IdPrefix, StringComparison.Ordinal)) return null;
        var node = device.Id[IdPrefix.Length..];
        return node == "default" ? null : node;
    }
}
