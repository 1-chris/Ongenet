using System;
using System.Runtime.InteropServices;

namespace Ongenet.Audio.Interop;

/// <summary>
/// Thin P/Invoke surface over ALSA's rawmidi + control API (libasound) and the libc <c>poll</c> used
/// to wait for input. Only the handful of functions <see cref="AlsaMidiInput"/> needs are bound.
///
/// No custom <c>SetDllImportResolver</c> is registered for this assembly: <c>libasound.so.2</c> is a
/// ubiquitous system library that the runtime's default resolution finds by its versioned soname.
/// This type is only ever touched on Linux (the factory guards by OS), so these imports are never
/// resolved on Windows/macOS.
/// </summary>
internal static class AlsaMidiNative
{
    private const string Asound = "libasound.so.2";
    private const string LibC = "libc";

    // snd_rawmidi open mode flags.
    public const int SND_RAWMIDI_NONBLOCK = 0x0002;

    // snd_rawmidi_stream enum.
    public const int SND_RAWMIDI_STREAM_INPUT = 1;

    // poll() event flag and the errno values snd_rawmidi_read returns negated.
    public const short POLLIN = 0x001;
    public const int EAGAIN = 11;
    public const int EINTR = 4;

    // --- Card / control enumeration ------------------------------------------------------

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_card_next(ref int card);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int snd_ctl_open(out IntPtr ctl, string name, int mode);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_ctl_close(IntPtr ctl);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_ctl_card_info_malloc(out IntPtr info);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_ctl_card_info_free(IntPtr info);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_ctl_card_info(IntPtr ctl, IntPtr info);

    // Returns a const char* owned by the info struct (no free).
    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr snd_ctl_card_info_get_name(IntPtr info);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_ctl_rawmidi_next_device(IntPtr ctl, ref int device);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_ctl_rawmidi_info(IntPtr ctl, IntPtr info);

    // --- Opaque snd_rawmidi_info accessors (never mirror the struct; use malloc + getters) -

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_rawmidi_info_malloc(out IntPtr info);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_rawmidi_info_free(IntPtr info);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_rawmidi_info_set_device(IntPtr info, uint val);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_rawmidi_info_set_subdevice(IntPtr info, uint val);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_rawmidi_info_set_stream(IntPtr info, int stream);

    // Returns a const char* owned by the info struct (no free).
    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr snd_rawmidi_info_get_name(IntPtr info);

    // --- rawmidi open / read / poll ------------------------------------------------------

    // outputp is passed IntPtr.Zero (NULL) to open input only.
    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int snd_rawmidi_open(out IntPtr inputp, IntPtr outputp, string name, int mode);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_rawmidi_close(IntPtr rmidi);

    // ssize_t snd_rawmidi_read(snd_rawmidi_t*, void* buffer, size_t size). Returns bytes read or -errno.
    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint snd_rawmidi_read(IntPtr rmidi, IntPtr buffer, nuint size);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_rawmidi_poll_descriptors_count(IntPtr rmidi);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_rawmidi_poll_descriptors(IntPtr rmidi, IntPtr pfds, uint space);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr snd_strerror(int errnum);

    public static string ErrorText(int code)
    {
        var ptr = snd_strerror(code);
        return ptr == IntPtr.Zero ? $"ALSA error {code}" : Marshal.PtrToStringAnsi(ptr) ?? $"ALSA error {code}";
    }

    /// <summary>struct pollfd { int fd; short events; short revents; } — 8 bytes on LP64.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int fd;
        public short events;
        public short revents;
    }

    // int poll(struct pollfd *fds, nfds_t nfds, int timeout); nfds_t is unsigned long on Linux.
    [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern int poll(IntPtr fds, nuint nfds, int timeout);

    // --- ALSA sequencer (snd_seq_*) ------------------------------------------------------
    // The universal Linux MIDI path: the kernel exposes hardware MIDI as seq clients, and PipeWire/JACK
    // bridge their MIDI into the sequencer too — so this sees devices that rawmidi cannot.

    public const int SND_SEQ_OPEN_DUPLEX = 3;

    public const uint SND_SEQ_PORT_CAP_READ = 1 << 0;
    public const uint SND_SEQ_PORT_CAP_WRITE = 1 << 1;
    public const uint SND_SEQ_PORT_CAP_SUBS_READ = 1 << 5;
    public const uint SND_SEQ_PORT_CAP_SUBS_WRITE = 1 << 6;

    public const uint SND_SEQ_PORT_TYPE_MIDI_GENERIC = 1 << 1;
    public const uint SND_SEQ_PORT_TYPE_APPLICATION = 1 << 20;

    // snd_seq_event_type values we decode.
    public const byte SND_SEQ_EVENT_NOTEON = 6;
    public const byte SND_SEQ_EVENT_NOTEOFF = 7;
    public const byte SND_SEQ_EVENT_KEYPRESS = 8;
    public const byte SND_SEQ_EVENT_CONTROLLER = 10;
    public const byte SND_SEQ_EVENT_PGMCHANGE = 11;
    public const byte SND_SEQ_EVENT_CHANPRESS = 12;
    public const byte SND_SEQ_EVENT_PITCHBEND = 13;

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int snd_seq_open(out IntPtr handle, string name, int streams, int mode);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_close(IntPtr handle);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int snd_seq_set_client_name(IntPtr handle, string name);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_client_id(IntPtr handle);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_nonblock(IntPtr handle, int nonblock);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int snd_seq_create_simple_port(IntPtr handle, string name, uint caps, uint type);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_connect_from(IntPtr handle, int myPort, int srcClient, int srcPort);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_disconnect_from(IntPtr handle, int myPort, int srcClient, int srcPort);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_poll_descriptors_count(IntPtr handle, short events);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_poll_descriptors(IntPtr handle, IntPtr pfds, uint space, short events);

    // int snd_seq_event_input(snd_seq_t*, snd_seq_event_t** ev). *ev points to library-owned memory.
    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_event_input(IntPtr handle, out IntPtr ev);

    // Opaque client/port info — malloc + accessors (never mirror the struct).
    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_client_info_malloc(out IntPtr info);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_seq_client_info_free(IntPtr info);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_seq_client_info_set_client(IntPtr info, int client);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_client_info_get_client(IntPtr info);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr snd_seq_client_info_get_name(IntPtr info);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_query_next_client(IntPtr handle, IntPtr info);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_port_info_malloc(out IntPtr info);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_seq_port_info_free(IntPtr info);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_seq_port_info_set_client(IntPtr info, int client);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_seq_port_info_set_port(IntPtr info, int port);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_port_info_get_port(IntPtr info);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint snd_seq_port_info_get_capability(IntPtr info);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr snd_seq_port_info_get_name(IntPtr info);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_query_next_port(IntPtr handle, IntPtr info);
}
