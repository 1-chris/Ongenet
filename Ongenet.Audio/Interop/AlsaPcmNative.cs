using System;
using System.Runtime.InteropServices;

namespace Ongenet.Audio.Interop;

/// <summary>
/// Thin P/Invoke surface over ALSA's PCM API (libasound) for raw audio playback/capture. Only the
/// functions the native backend needs are bound. Like <see cref="AlsaMidiNative"/>, no custom
/// <c>SetDllImportResolver</c> is registered (PortAudio owns the single per-assembly resolver, and
/// <c>libasound.so.2</c> resolves by its versioned soname); these imports are only ever touched on
/// Linux, where <see cref="snd_pcm_uframes_t"/> (<c>unsigned long</c>) is 8 bytes — modelled as
/// <see cref="ulong"/>, and <c>snd_pcm_sframes_t</c> as <see cref="long"/>.
/// </summary>
internal static class AlsaPcmNative
{
    private const string Asound = "libasound.so.2";

    // snd_pcm_stream_t
    public const int SND_PCM_STREAM_PLAYBACK = 0;
    public const int SND_PCM_STREAM_CAPTURE = 1;

    // open mode flags
    public const int SND_PCM_NONBLOCK = 0x00000001;

    // snd_pcm_access_t
    public const int SND_PCM_ACCESS_RW_INTERLEAVED = 3;

    // snd_pcm_format_t — 32-bit float, little-endian (matches the engine's native sample format).
    public const int SND_PCM_FORMAT_FLOAT_LE = 14;

    // errno values returned negated by writei/readi.
    public const int EPIPE = 32;   // xrun (under/overrun)
    public const int ESTRPIPE = 86; // stream suspended

    // --- open / lifecycle ------------------------------------------------------------------------

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int snd_pcm_open(out IntPtr pcm, string name, int stream, int mode);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_close(IntPtr pcm);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_prepare(IntPtr pcm);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_start(IntPtr pcm);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_drop(IntPtr pcm);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_drain(IntPtr pcm);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_recover(IntPtr pcm, int err, int silent);

    // --- I/O (interleaved) -----------------------------------------------------------------------

    // Return value (snd_pcm_sframes_t) is frames written/read, or a negated errno on error.
    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern long snd_pcm_writei(IntPtr pcm, IntPtr buffer, ulong frames);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern long snd_pcm_readi(IntPtr pcm, IntPtr buffer, ulong frames);

    // --- hardware parameters ---------------------------------------------------------------------

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_malloc(out IntPtr ptr);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_pcm_hw_params_free(IntPtr ptr);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_any(IntPtr pcm, IntPtr ptr);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_access(IntPtr pcm, IntPtr ptr, int access);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_format(IntPtr pcm, IntPtr ptr, int format);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_channels_near(IntPtr pcm, IntPtr ptr, ref uint channels);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_rate_near(IntPtr pcm, IntPtr ptr, ref uint rate, ref int dir);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_period_size_near(IntPtr pcm, IntPtr ptr, ref ulong frames, ref int dir);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_buffer_size_near(IntPtr pcm, IntPtr ptr, ref ulong frames);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_get_period_size(IntPtr ptr, out ulong frames, out int dir);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_get_buffer_size(IntPtr ptr, out ulong frames);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params(IntPtr pcm, IntPtr ptr);

    // --- software parameters (start threshold) ---------------------------------------------------

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_sw_params_malloc(out IntPtr ptr);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_pcm_sw_params_free(IntPtr ptr);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_sw_params_current(IntPtr pcm, IntPtr ptr);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_sw_params_set_start_threshold(IntPtr pcm, IntPtr ptr, ulong val);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_sw_params_set_avail_min(IntPtr pcm, IntPtr ptr, ulong val);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_sw_params(IntPtr pcm, IntPtr ptr);

    // --- device enumeration ----------------------------------------------------------------------

    // snd_device_name_hint(card, iface, hints**): card=-1 = all cards; iface="pcm".
    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int snd_device_name_hint(int card, string iface, out IntPtr hints);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_device_name_free_hint(IntPtr hints);

    // Returns a malloc'd C string (NAME/DESC/IOID) that the caller must free(); IOID is null for "both".
    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr snd_device_name_get_hint(IntPtr hint, string id);

    // The hint strings above are malloc'd by libasound, so they must be released with libc free().
    [DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
    public static extern void free(IntPtr ptr);

    /// <summary>Reads one hint string (NAME/DESC/IOID) and frees the native allocation.</summary>
    public static string? ReadHint(IntPtr hint, string id)
    {
        var ptr = snd_device_name_get_hint(hint, id);
        if (ptr == IntPtr.Zero) return null;
        var s = Marshal.PtrToStringAnsi(ptr);
        free(ptr);
        return s;
    }

    // --- diagnostics -----------------------------------------------------------------------------

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr snd_strerror(int errnum);

    public static string ErrorText(int code)
    {
        var ptr = snd_strerror(code);
        return ptr == IntPtr.Zero ? $"error {code}" : Marshal.PtrToStringAnsi(ptr) ?? $"error {code}";
    }

    /// <summary>Whether libasound can be loaded on this machine.</summary>
    public static bool TryProbe()
    {
        try { return NativeLibrary.TryLoad(Asound, out _); }
        catch { return false; }
    }
}
