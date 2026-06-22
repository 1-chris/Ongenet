using System;
using System.Runtime.InteropServices;

namespace Ongenet.Audio.Interop;

/// <summary>
/// P/Invoke surface over PulseAudio. Two libraries: the blocking <b>simple</b> API
/// (<c>libpulse-simple.so.0</c>) drives playback/capture in our own thread loop, and the async core
/// API (<c>libpulse.so.0</c>) is used once at startup to enumerate sinks/sources and the server's
/// default device. As with the ALSA interop, no custom resolver is registered — the versioned sonames
/// resolve by default and these are only touched on Linux. On a PipeWire desktop these route through
/// <c>pipewire-pulse</c>, so this backend works there without real PulseAudio installed.
/// </summary>
internal static class PulseAudioNative
{
    private const string Pulse = "libpulse.so.0";
    private const string PulseSimple = "libpulse-simple.so.0";

    // pa_sample_format_t
    public const int PA_SAMPLE_FLOAT32LE = 5;

    // pa_stream_direction_t
    public const int PA_STREAM_PLAYBACK = 1;
    public const int PA_STREAM_RECORD = 2;

    // pa_context_state_t
    public const int PA_CONTEXT_READY = 4;
    public const int PA_CONTEXT_FAILED = 5;
    public const int PA_CONTEXT_TERMINATED = 6;

    // pa_operation_state_t
    public const int PA_OPERATION_RUNNING = 0;

    /// <summary>Mirror of <c>pa_sample_spec</c> { pa_sample_format_t format; uint32 rate; uint8 channels; }.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct pa_sample_spec
    {
        public int format;
        public uint rate;
        public byte channels;
    }

    // --- simple API (blocking I/O) ---------------------------------------------------------------

    // pa_simple_new(server, name, dir, dev, stream_name, &ss, channel_map, attr, &error)
    [DllImport(PulseSimple, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr pa_simple_new(string? server, string name, int dir, string? dev,
        string streamName, ref pa_sample_spec ss, IntPtr channelMap, IntPtr attr, out int error);

    [DllImport(PulseSimple, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pa_simple_free(IntPtr s);

    [DllImport(PulseSimple, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pa_simple_write(IntPtr s, IntPtr data, UIntPtr bytes, out int error);

    [DllImport(PulseSimple, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pa_simple_read(IntPtr s, IntPtr data, UIntPtr bytes, out int error);

    [DllImport(PulseSimple, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pa_simple_drain(IntPtr s, out int error);

    // --- async core API (used only for enumeration) ----------------------------------------------

    [DllImport(Pulse, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pa_mainloop_new();

    [DllImport(Pulse, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pa_mainloop_free(IntPtr m);

    [DllImport(Pulse, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pa_mainloop_get_api(IntPtr m);

    // pa_mainloop_iterate(m, block, &retval) — pumps one loop iteration.
    [DllImport(Pulse, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pa_mainloop_iterate(IntPtr m, int block, IntPtr retval);

    [DllImport(Pulse, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr pa_context_new(IntPtr mainloopApi, string name);

    [DllImport(Pulse, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int pa_context_connect(IntPtr c, string? server, int flags, IntPtr spawnApi);

    [DllImport(Pulse, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pa_context_get_state(IntPtr c);

    [DllImport(Pulse, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pa_context_disconnect(IntPtr c);

    [DllImport(Pulse, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pa_context_unref(IntPtr c);

    // sink/source/server info list callbacks: see PulseAudioDriver for the [UnmanagedCallersOnly] statics.
    [DllImport(Pulse, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pa_context_get_sink_info_list(IntPtr c, IntPtr cb, IntPtr userdata);

    [DllImport(Pulse, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pa_context_get_source_info_list(IntPtr c, IntPtr cb, IntPtr userdata);

    [DllImport(Pulse, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pa_context_get_server_info(IntPtr c, IntPtr cb, IntPtr userdata);

    [DllImport(Pulse, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pa_operation_get_state(IntPtr o);

    [DllImport(Pulse, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pa_operation_unref(IntPtr o);

    [DllImport(Pulse, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr pa_strerror(int error);

    public static string ErrorText(int code)
    {
        var p = pa_strerror(code);
        return p == IntPtr.Zero ? $"error {code}" : Marshal.PtrToStringAnsi(p) ?? $"error {code}";
    }

    public static bool TryProbe()
    {
        try { return NativeLibrary.TryLoad(PulseSimple, out _) && NativeLibrary.TryLoad(Pulse, out _); }
        catch { return false; }
    }
}
