using System;
using System.Runtime.InteropServices;

namespace Ongenet.Audio.Interop;

/// <summary>
/// P/Invoke surface over JACK (libjack). JACK is callback-driven: the server (fixed sample rate and
/// buffer size) calls our process callback on its real-time thread, where we read/write one mono float
/// buffer per port. We register stereo ports and auto-connect them to the system's physical
/// playback/capture ports. No custom resolver (versioned soname, Linux-only). On a PipeWire desktop the
/// JACK API is provided by <c>pipewire-jack</c>'s libjack, which may live outside the default loader
/// path — hence this driver only surfaces when libjack is actually loadable.
/// </summary>
internal static class JackNative
{
    private const string Lib = "libjack.so.0";

    // jack_options_t / jack_port flags.
    public const int JackNullOption = 0;
    public const uint JackPortIsOutput = 0x2;
    public const uint JackPortIsInput = 0x1;
    public const uint JackPortIsPhysical = 0x4;

    public const string JACK_DEFAULT_AUDIO_TYPE = "32 bit float mono audio";

    // jack_client_open is variadic; with JackNullOption there are no trailing args, so the cdecl
    // declaration below is ABI-correct (caller cleans the stack).
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr jack_client_open(string name, int options, out int status);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int jack_client_close(IntPtr client);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint jack_get_sample_rate(IntPtr client);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint jack_get_buffer_size(IntPtr client);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr jack_port_register(IntPtr client, string portName, string portType, uint flags, uint bufferSize);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr jack_port_get_buffer(IntPtr port, uint nframes);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr jack_port_name(IntPtr port);

    // process callback: int (*)(jack_nframes_t nframes, void* arg)
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int jack_set_process_callback(IntPtr client, IntPtr callback, IntPtr arg);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int jack_activate(IntPtr client);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int jack_deactivate(IntPtr client);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int jack_connect(IntPtr client, string sourcePort, string destinationPort);

    // jack_get_ports(client, port_name_pattern, type_name_pattern, flags) → NULL-terminated char**.
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr jack_get_ports(IntPtr client, string? portNamePattern, string? typeNamePattern, uint flags);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void jack_free(IntPtr ptr);

    public static bool TryProbe()
    {
        try { return NativeLibrary.TryLoad(Lib, out _); }
        catch { return false; }
    }
}
