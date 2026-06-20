using System;
using System.Runtime.InteropServices;

namespace Ongenet.Audio.Interop;

/// <summary>
/// P/Invoke surface over the Windows Multimedia MIDI input API (winmm.dll). Only touched on Windows
/// (the factory guards by OS), so the imports are never resolved elsewhere. The PortAudio resolver
/// already registered on this assembly returns IntPtr.Zero for "winmm.dll", so default resolution
/// loads the system library.
/// </summary>
internal static class WinMmMidiNative
{
    private const string Lib = "winmm.dll";

    public const int MAXPNAMELEN = 32;
    public const uint CALLBACK_FUNCTION = 0x00030000;

    // Messages delivered to the MidiInProc callback.
    public const uint MIM_DATA = 0x3C3;
    public const uint MIM_LONGDATA = 0x3C4;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void MidiInProc(IntPtr hMidiIn, uint wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MIDIINCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAXPNAMELEN)]
        public string szPname;

        public uint dwSupport;
    }

    [DllImport(Lib)]
    public static extern uint midiInGetNumDevs();

    // Use the wide entry point explicitly; an A/W charset mismatch garbles device names.
    [DllImport(Lib, EntryPoint = "midiInGetDevCapsW", CharSet = CharSet.Unicode)]
    public static extern int midiInGetDevCaps(UIntPtr uDeviceID, ref MIDIINCAPS caps, uint cbMidiInCaps);

    [DllImport(Lib)]
    public static extern int midiInOpen(out IntPtr lphMidiIn, uint uDeviceID, MidiInProc dwCallback,
        IntPtr dwInstance, uint dwFlags);

    [DllImport(Lib)]
    public static extern int midiInStart(IntPtr hMidiIn);

    [DllImport(Lib)]
    public static extern int midiInStop(IntPtr hMidiIn);

    [DllImport(Lib)]
    public static extern int midiInReset(IntPtr hMidiIn);

    [DllImport(Lib)]
    public static extern int midiInClose(IntPtr hMidiIn);
}
