using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ongenet.Vst.Vst2.Interop;

/// <summary>
/// Hand-transcribed VST 2.4 C ABI (the flat <c>AEffect</c> struct + opcode dispatch), plus the
/// <c>audioMaster</c> host callback Ongenet provides. The ABI is a well-documented public interface; no
/// Steinberg SDK headers are used. 64-bit only — the struct layout below assumes 8-byte pointers, which
/// is all .NET 10 targets. Function-pointer fields we call are typed <c>delegate* unmanaged[Cdecl]</c>;
/// the managed instance behind an <c>AEffect</c> is recovered from its host-owned <c>user</c> pointer.
/// </summary>
public static unsafe class Vst2Api
{
    public const int Magic = 0x56737450; // 'VstP'

    // Entry-point export names, tried in order. Modern plugins export VSTPluginMain; very old ones "main".
    public static readonly string[] EntrySymbols = { "VSTPluginMain", "main", "main_macho" };

    // --- effXxx dispatcher opcodes (host -> plugin) ---
    public const int EffOpen = 0;
    public const int EffClose = 1;
    public const int EffSetProgram = 2;
    public const int EffGetProgram = 3;
    public const int EffGetProgramName = 5;
    public const int EffGetParamLabel = 6;
    public const int EffGetParamDisplay = 7;
    public const int EffGetParamName = 8;
    public const int EffSetSampleRate = 10;
    public const int EffSetBlockSize = 11;
    public const int EffMainsChanged = 12;
    public const int EffEditGetRect = 13;
    public const int EffEditOpen = 14;
    public const int EffEditClose = 15;
    public const int EffEditIdle = 19;
    public const int EffGetChunk = 23;
    public const int EffSetChunk = 24;
    public const int EffProcessEvents = 25;
    public const int EffGetPlugCategory = 35;
    public const int EffGetEffectName = 45;
    public const int EffGetVendorString = 47;
    public const int EffGetProductString = 48;
    public const int EffGetVendorVersion = 49;
    public const int EffCanDo = 51;
    public const int EffGetVstVersion = 58;

    // --- AEffect.flags ---
    public const int FlagsHasEditor = 1 << 0;
    public const int FlagsCanReplacing = 1 << 4;
    public const int FlagsProgramChunks = 1 << 5;
    public const int FlagsIsSynth = 1 << 8;
    public const int FlagsCanDoubleReplacing = 1 << 12;

    public const int PlugCategSynth = 2;

    // --- audioMasterXxx opcodes (plugin -> host) ---
    public const int AmAutomate = 0;
    public const int AmVersion = 1;
    public const int AmCurrentId = 2;
    public const int AmIdle = 3;
    public const int AmGetTime = 7;
    public const int AmProcessEvents = 8;
    public const int AmIOChanged = 13;
    public const int AmSizeWindow = 15;
    public const int AmGetSampleRate = 16;
    public const int AmGetBlockSize = 17;
    public const int AmGetCurrentProcessLevel = 23;
    public const int AmGetVendorString = 32;
    public const int AmGetProductString = 33;
    public const int AmGetVendorVersion = 34;
    public const int AmCanDo = 37;
    public const int AmGetLanguage = 38;
    public const int AmUpdateDisplay = 42;
    public const int AmBeginEdit = 43;
    public const int AmEndEdit = 44;

    // kVstMidiType for VstEvent.type.
    public const int MidiEventType = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct AEffect
    {
        public int Magic;
        public delegate* unmanaged[Cdecl]<AEffect*, int, int, nint, void*, float, nint> Dispatcher;
        public delegate* unmanaged[Cdecl]<AEffect*, float**, float**, int, void> Process; // deprecated
        public delegate* unmanaged[Cdecl]<AEffect*, int, float, void> SetParameter;
        public delegate* unmanaged[Cdecl]<AEffect*, int, float> GetParameter;
        public int NumPrograms;
        public int NumParams;
        public int NumInputs;
        public int NumOutputs;
        public int Flags;
        public nint Resvd1;
        public nint Resvd2;
        public int InitialDelay;
        public int RealQualities;   // deprecated
        public int OffQualities;    // deprecated
        public float IoRatio;       // deprecated
        public void* Object;
        public void* User;          // reserved for host — we stash a GCHandle here
        public int UniqueId;
        public int Version;
        public delegate* unmanaged[Cdecl]<AEffect*, float**, float**, int, void> ProcessReplacing;
        public delegate* unmanaged[Cdecl]<AEffect*, double**, double**, int, void> ProcessDoubleReplacing;
        public fixed byte Future[56];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ERect { public short Top, Left, Bottom, Right; }

    [StructLayout(LayoutKind.Sequential)]
    public struct VstMidiEvent
    {
        public int Type;        // = MidiEventType
        public int ByteSize;    // = sizeof(VstMidiEvent)
        public int DeltaFrames;
        public int Flags;
        public int NoteLength;
        public int NoteOffset;
        public byte Data0;
        public byte Data1;
        public byte Data2;
        public byte Data3;
        public sbyte Detune;
        public byte NoteOffVelocity;
        public byte Reserved1;
        public byte Reserved2;
    }

    // Variable-length: { int numEvents; nint reserved; VstEvent* events[]; }. We build it by hand.
    [StructLayout(LayoutKind.Sequential)]
    public struct VstEventsHeader
    {
        public int NumEvents;
        public nint Reserved;
        // followed by NumEvents pointers
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VstTimeInfo
    {
        public double SamplePos;
        public double SampleRate;
        public double NanoSeconds;
        public double PpqPos;
        public double Tempo;
        public double BarStartPos;
        public double CycleStartPos;
        public double CycleEndPos;
        public int TimeSigNumerator;
        public int TimeSigDenominator;
        public int SmpteOffset;
        public int SmpteFrameRate;
        public int SamplesToNextClock;
        public int Flags;
    }

    // --- Host callback ---

    private static Vst2PluginBase? HostInstance(AEffect* effect)
    {
        if (effect == null || effect->User == null) return null;
        try { return GCHandle.FromIntPtr((nint)effect->User).Target as Vst2PluginBase; }
        catch { return null; }
    }

    /// <summary>The audioMaster callback handed to every plugin's entry point.</summary>
    public static readonly delegate* unmanaged[Cdecl]<AEffect*, int, int, nint, void*, float, nint> Callback = &HostCallback;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static nint HostCallback(AEffect* effect, int opcode, int index, nint value, void* ptr, float opt)
    {
        switch (opcode)
        {
            case AmVersion: return 2400;
            case AmCurrentId: return 0;
            case AmGetCurrentProcessLevel: return 2; // realtime
            case AmGetLanguage: return 1;            // English
            case AmGetVendorVersion: return 1000;
            case AmGetSampleRate:
            {
                var inst = HostInstance(effect);
                return (nint)(inst?.SampleRate > 0 ? (int)inst.SampleRate : 44100);
            }
            case AmGetBlockSize: return Vst2PluginBase.MaxBlock;
            case AmGetVendorString:
            case AmGetProductString:
                WriteHostString(ptr, "Ongenet");
                return 1;
            case AmGetTime:
            {
                var inst = HostInstance(effect);
                return inst != null ? inst.TimeInfo : 0;
            }
            case AmCanDo:
                return HostCanDo(ReadUtf8((byte*)ptr));
            case AmSizeWindow:
                // audioMasterSizeWindow: index = width, value = height.
                HostInstance(effect)?.OnPluginResize(index, (int)value);
                return 1;
            case AmUpdateDisplay:
            case AmIOChanged:
            case AmBeginEdit:
            case AmEndEdit:
            case AmIdle:
            case AmAutomate:
            case AmProcessEvents:
                return 1;
            default:
                return 0;
        }
    }

    private static nint HostCanDo(string? feature) => feature switch
    {
        "sendVstEvents" => 1,
        "sendVstMidiEvent" => 1,
        "receiveVstEvents" => 1,
        "receiveVstMidiEvent" => 1,
        "sizeWindow" => 1,
        "supplyIdle" => 1,
        _ => 0,
    };

    private static void WriteHostString(void* ptr, string s)
    {
        if (ptr == null) return;
        var dst = (byte*)ptr;
        var bytes = System.Text.Encoding.ASCII.GetBytes(s);
        var n = Math.Min(bytes.Length, 63);
        for (var i = 0; i < n; i++) dst[i] = bytes[i];
        dst[n] = 0;
    }

    // --- String helpers ---

    public static string ReadUtf8(byte* p) => p == null ? string.Empty : (Marshal.PtrToStringUTF8((nint)p) ?? string.Empty);

    /// <summary>Reads a NUL-terminated string from a fixed dispatcher buffer (max <paramref name="max"/> bytes).</summary>
    public static string ReadFixed(byte* p, int max)
    {
        if (p == null) return string.Empty;
        var len = 0;
        while (len < max && p[len] != 0) len++;
        return System.Text.Encoding.ASCII.GetString(p, len);
    }
}
