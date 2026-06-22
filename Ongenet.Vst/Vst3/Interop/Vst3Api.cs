using System;
using System.Runtime.InteropServices;

namespace Ongenet.Vst.Vst3.Interop;

/// <summary>
/// Hand-transcribed VST 3 C ABI (the COM-style <c>FUnknown</c> vtable model), plus the host-side COM
/// objects Ongenet provides (host application / component handler / plug frame / event list / parameter
/// changes / memory stream). The ABI is the public VST3 interface; no Steinberg SDK headers are used.
///
/// VST3 objects are pointers to a pointer to a vtable (an array of C function pointers). We model each
/// plugin interface as a struct of <c>delegate* unmanaged[Cdecl]</c> in slot order — on x64 the cdecl
/// and stdcall conventions coincide, so this is correct on Windows, Linux and macOS. Methods we never
/// call are kept as <c>nint</c> placeholders so the slots after them stay at the right offset. Host
/// objects are built the mirror way: a native block whose first field points at a vtable of
/// <c>UnmanagedCallersOnly</c> thunks, with a managed <see cref="System.Runtime.InteropServices.GCHandle"/>
/// stashed alongside so the thunks can recover their managed owner.
/// </summary>
public static unsafe class Vst3Api
{
    // --- Bundle entry points (per OS) ---
    public const string FactorySymbol = "GetPluginFactory";
    public static readonly string[] InitSymbols = { "InitDll", "ModuleEntry", "bundleEntry" };
    public static readonly string[] ExitSymbols = { "ExitDll", "ModuleExit", "bundleExit" };

    public const string AudioModuleClass = "Audio Module Class";

    // --- result codes (platform-dependent on Windows for the error values) ---
    public const int ResultOk = 0;     // == kResultTrue
    public const int ResultFalse = 1;
    public static int NoInterface => OperatingSystem.IsWindows() ? unchecked((int)0x80004002) : -1;
    public static int NotImplemented => OperatingSystem.IsWindows() ? unchecked((int)0x80004001) : 3;
    public static bool Ok(int r) => r == ResultOk;

    // --- media types / bus directions / bus types ---
    public const int MediaAudio = 0;
    public const int MediaEvent = 1;
    public const int DirInput = 0;
    public const int DirOutput = 1;

    // --- symbolic sample size / process mode ---
    public const int Sample32 = 0;
    public const int ProcessModeRealtime = 0;

    // --- speaker arrangements ---
    public const ulong SpeakerMono = 0x1;
    public const ulong SpeakerStereo = 0x3;

    // --- event types ---
    public const ushort NoteOnEvent = 0;
    public const ushort NoteOffEvent = 1;

    // --- IPlugView platform types ---
    public const string PlatformHwnd = "HWND";
    public const string PlatformNsView = "NSView";
    public const string PlatformX11 = "X11EmbedWindowID";

    // ============================ Interface IIDs ============================

    public static readonly byte[] IidFUnknown = Tuid(0x00000000, 0x00000000, 0xC0000000, 0x00000046);
    public static readonly byte[] IidPluginFactory = Tuid(0x7A4D811C, 0x52114A1F, 0xAED9D2EE, 0x0B43BF9F);
    public static readonly byte[] IidPluginFactory2 = Tuid(0x0007B650, 0xF24B4C0B, 0xA464EDB9, 0xF00B2ABB);
    public static readonly byte[] IidComponent = Tuid(0xE831FF31, 0xF2D54301, 0x928EBBEE, 0x25697802);
    public static readonly byte[] IidAudioProcessor = Tuid(0x42043F99, 0xB7DA453C, 0xA569E79D, 0x9AAEC33D);
    public static readonly byte[] IidEditController = Tuid(0xDCD7BBE3, 0x7742448D, 0xA874AACC, 0x979C759E);
    public static readonly byte[] IidConnectionPoint = Tuid(0x70A4156F, 0x6E6E4026, 0x989148BF, 0xAA60D8D1);
    public static readonly byte[] IidPlugView = Tuid(0x5BC32507, 0xD06049EA, 0xA6151B52, 0x2B755B29);
    public static readonly byte[] IidPlugFrame = Tuid(0x367FAF01, 0xAFA94693, 0x8D4DA2A0, 0xED0882A3);
    public static readonly byte[] IidComponentHandler = Tuid(0x93A0BEA3, 0x0BD045DB, 0x8E890B0C, 0xC1E46AC6);
    public static readonly byte[] IidHostApplication = Tuid(0x58E595CC, 0xDB2D0969, 0x8B6AAF8C, 0x36A664E5);
    public static readonly byte[] IidBStream = Tuid(0xC3BF6EA2, 0x30994752, 0x9B6BF990, 0x1EE33E9B);
    public static readonly byte[] IidEventList = Tuid(0x3A2C4214, 0x346349FE, 0xB2C4F397, 0xB9695A44);
    public static readonly byte[] IidParameterChanges = Tuid(0xA4779663, 0x0BB64A56, 0xB44384A8, 0x466FEB9D);
    public static readonly byte[] IidParamValueQueue = Tuid(0x01263A18, 0xED074F6F, 0x98C9D356, 0x4686F9BA);
    // Steinberg::Linux::IRunLoop — required to host plugin GUIs on X11 (FD + timer pumping).
    public static readonly byte[] IidRunLoop = Tuid(0x18C35366, 0x97764F1A, 0x9C5B8385, 0x7A871389);

    /// <summary>Builds a 16-byte TUID from the four 32-bit groups, in the platform's native byte layout.</summary>
    public static byte[] Tuid(uint l1, uint l2, uint l3, uint l4)
    {
        var b = new byte[16];
        if (OperatingSystem.IsWindows())
        {
            // COM_COMPATIBLE: l1 big-endian, l2 with swapped 16-bit halves, l3/l4 big-endian.
            b[0] = (byte)(l1 >> 24); b[1] = (byte)(l1 >> 16); b[2] = (byte)(l1 >> 8); b[3] = (byte)l1;
            b[4] = (byte)(l2 >> 16); b[5] = (byte)(l2 >> 24); b[6] = (byte)l2; b[7] = (byte)(l2 >> 8);
            b[8] = (byte)(l3 >> 24); b[9] = (byte)(l3 >> 16); b[10] = (byte)(l3 >> 8); b[11] = (byte)l3;
            b[12] = (byte)(l4 >> 24); b[13] = (byte)(l4 >> 16); b[14] = (byte)(l4 >> 8); b[15] = (byte)l4;
        }
        else
        {
            b[0] = (byte)(l1 >> 24); b[1] = (byte)(l1 >> 16); b[2] = (byte)(l1 >> 8); b[3] = (byte)l1;
            b[4] = (byte)(l2 >> 24); b[5] = (byte)(l2 >> 16); b[6] = (byte)(l2 >> 8); b[7] = (byte)l2;
            b[8] = (byte)(l3 >> 24); b[9] = (byte)(l3 >> 16); b[10] = (byte)(l3 >> 8); b[11] = (byte)l3;
            b[12] = (byte)(l4 >> 24); b[13] = (byte)(l4 >> 16); b[14] = (byte)(l4 >> 8); b[15] = (byte)l4;
        }

        return b;
    }

    // ============================ Plugin vtables ============================

    [StructLayout(LayoutKind.Sequential)]
    public struct FUnknownVtbl
    {
        public delegate* unmanaged[Cdecl]<void*, byte*, void**, int> QueryInterface;
        public delegate* unmanaged[Cdecl]<void*, uint> AddRef;
        public delegate* unmanaged[Cdecl]<void*, uint> Release;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PluginFactoryVtbl
    {
        public delegate* unmanaged[Cdecl]<void*, byte*, void**, int> QueryInterface;
        public delegate* unmanaged[Cdecl]<void*, uint> AddRef;
        public delegate* unmanaged[Cdecl]<void*, uint> Release;
        public nint GetFactoryInfo;
        public delegate* unmanaged[Cdecl]<void*, int> CountClasses;
        public delegate* unmanaged[Cdecl]<void*, int, PClassInfo*, int> GetClassInfo;
        public delegate* unmanaged[Cdecl]<void*, byte*, byte*, void**, int> CreateInstance;
        public delegate* unmanaged[Cdecl]<void*, int, PClassInfo2*, int> GetClassInfo2; // IPluginFactory2 slot
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ComponentVtbl
    {
        public delegate* unmanaged[Cdecl]<void*, byte*, void**, int> QueryInterface;
        public delegate* unmanaged[Cdecl]<void*, uint> AddRef;
        public delegate* unmanaged[Cdecl]<void*, uint> Release;
        public delegate* unmanaged[Cdecl]<void*, void*, int> Initialize;       // IPluginBase
        public delegate* unmanaged[Cdecl]<void*, int> Terminate;               // IPluginBase
        public delegate* unmanaged[Cdecl]<void*, byte*, int> GetControllerClassId;
        public nint SetIoMode;
        public delegate* unmanaged[Cdecl]<void*, int, int, int> GetBusCount;   // (mediaType, dir)
        public delegate* unmanaged[Cdecl]<void*, int, int, int, BusInfo*, int> GetBusInfo; // (mediaType, dir, index, info)
        public nint GetRoutingInfo;
        public delegate* unmanaged[Cdecl]<void*, int, int, int, byte, int> ActivateBus; // (mediaType, dir, index, state)
        public delegate* unmanaged[Cdecl]<void*, byte, int> SetActive;
        public nint SetState;
        public delegate* unmanaged[Cdecl]<void*, void*, int> GetState;         // (IBStream*)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioProcessorVtbl
    {
        public delegate* unmanaged[Cdecl]<void*, byte*, void**, int> QueryInterface;
        public delegate* unmanaged[Cdecl]<void*, uint> AddRef;
        public delegate* unmanaged[Cdecl]<void*, uint> Release;
        public delegate* unmanaged[Cdecl]<void*, ulong*, int, ulong*, int, int> SetBusArrangements;
        public nint GetBusArrangement;
        public delegate* unmanaged[Cdecl]<void*, int, int> CanProcessSampleSize;
        public nint GetLatencySamples;
        public delegate* unmanaged[Cdecl]<void*, ProcessSetup*, int> SetupProcessing;
        public delegate* unmanaged[Cdecl]<void*, byte, int> SetProcessing;
        public delegate* unmanaged[Cdecl]<void*, ProcessData*, int> Process;
        public nint GetTailSamples;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EditControllerVtbl
    {
        public delegate* unmanaged[Cdecl]<void*, byte*, void**, int> QueryInterface;
        public delegate* unmanaged[Cdecl]<void*, uint> AddRef;
        public delegate* unmanaged[Cdecl]<void*, uint> Release;
        public delegate* unmanaged[Cdecl]<void*, void*, int> Initialize;
        public delegate* unmanaged[Cdecl]<void*, int> Terminate;
        public delegate* unmanaged[Cdecl]<void*, void*, int> SetComponentState; // (IBStream*)
        public nint SetState;
        public nint GetState;
        public delegate* unmanaged[Cdecl]<void*, int> GetParameterCount;
        public delegate* unmanaged[Cdecl]<void*, int, ParameterInfo*, int> GetParameterInfo;
        public nint GetParamStringByValue;
        public nint GetParamValueByString;
        public nint NormalizedParamToPlain;
        public nint PlainParamToNormalized;
        public delegate* unmanaged[Cdecl]<void*, uint, double> GetParamNormalized;
        public delegate* unmanaged[Cdecl]<void*, uint, double, int> SetParamNormalized;
        public delegate* unmanaged[Cdecl]<void*, void*, int> SetComponentHandler;
        public delegate* unmanaged[Cdecl]<void*, byte*, void*> CreateView;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ConnectionPointVtbl
    {
        public delegate* unmanaged[Cdecl]<void*, byte*, void**, int> QueryInterface;
        public delegate* unmanaged[Cdecl]<void*, uint> AddRef;
        public delegate* unmanaged[Cdecl]<void*, uint> Release;
        public delegate* unmanaged[Cdecl]<void*, void*, int> Connect;
        public delegate* unmanaged[Cdecl]<void*, void*, int> Disconnect;
        public delegate* unmanaged[Cdecl]<void*, void*, int> Notify; // (this, IMessage*)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PlugViewVtbl
    {
        public delegate* unmanaged[Cdecl]<void*, byte*, void**, int> QueryInterface;
        public delegate* unmanaged[Cdecl]<void*, uint> AddRef;
        public delegate* unmanaged[Cdecl]<void*, uint> Release;
        public delegate* unmanaged[Cdecl]<void*, byte*, int> IsPlatformTypeSupported;
        public delegate* unmanaged[Cdecl]<void*, void*, byte*, int> Attached;
        public delegate* unmanaged[Cdecl]<void*, int> Removed;
        public nint OnWheel;
        public nint OnKeyDown;
        public nint OnKeyUp;
        public delegate* unmanaged[Cdecl]<void*, ViewRect*, int> GetSize;
        public delegate* unmanaged[Cdecl]<void*, ViewRect*, int> OnSize;
        public nint OnFocus;
        public delegate* unmanaged[Cdecl]<void*, void*, int> SetFrame;
        public delegate* unmanaged[Cdecl]<void*, int> CanResize;
        public nint CheckSizeConstraint;
    }

    // ============================ Data structs ============================

    public const int Str128Bytes = 256; // String128 = char16[128]

    [StructLayout(LayoutKind.Sequential)]
    public struct PClassInfo
    {
        public fixed byte Cid[16];
        public int Cardinality;
        public fixed byte Category[32];
        public fixed byte Name[64];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PClassInfo2
    {
        public fixed byte Cid[16];
        public int Cardinality;
        public fixed byte Category[32];
        public fixed byte Name[64];
        public uint ClassFlags;
        public fixed byte SubCategories[128];
        public fixed byte Vendor[64];
        public fixed byte Version[64];
        public fixed byte SdkVersion[64];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BusInfo
    {
        public int MediaType;
        public int Direction;
        public int ChannelCount;
        public fixed byte Name[Str128Bytes];
        public int BusType;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessSetup
    {
        public int ProcessMode;
        public int SymbolicSampleSize;
        public int MaxSamplesPerBlock;
        public double SampleRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioBusBuffers
    {
        public int NumChannels;
        public ulong SilenceFlags;
        public void* ChannelBuffers; // float** (Sample32)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessData
    {
        public int ProcessMode;
        public int SymbolicSampleSize;
        public int NumSamples;
        public int NumInputs;
        public int NumOutputs;
        public AudioBusBuffers* Inputs;
        public AudioBusBuffers* Outputs;
        public void* InputParameterChanges;
        public void* OutputParameterChanges;
        public void* InputEvents;
        public void* OutputEvents;
        public void* ProcessContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ParameterInfo
    {
        public uint Id;
        public fixed byte Title[Str128Bytes];
        public fixed byte ShortTitle[Str128Bytes];
        public fixed byte Units[Str128Bytes];
        public int StepCount;
        public double DefaultNormalizedValue;
        public int UnitId;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ViewRect { public int Left, Top, Right, Bottom; }

    // VST3 Event (48 bytes); union members overlap from offset 24.
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct Event
    {
        [FieldOffset(0)] public int BusIndex;
        [FieldOffset(4)] public int SampleOffset;
        [FieldOffset(8)] public double PpqPosition;
        [FieldOffset(16)] public ushort Flags;
        [FieldOffset(18)] public ushort Type;
        // NoteOnEvent
        [FieldOffset(24)] public short NoteOnChannel;
        [FieldOffset(26)] public short NoteOnPitch;
        [FieldOffset(28)] public float NoteOnTuning;
        [FieldOffset(32)] public float NoteOnVelocity;
        [FieldOffset(36)] public int NoteOnLength;
        [FieldOffset(40)] public int NoteOnNoteId;
        // NoteOffEvent
        [FieldOffset(24)] public short NoteOffChannel;
        [FieldOffset(26)] public short NoteOffPitch;
        [FieldOffset(28)] public float NoteOffVelocity;
        [FieldOffset(32)] public int NoteOffNoteId;
        [FieldOffset(36)] public float NoteOffTuning;
    }

    // ============================ Call helpers ============================

    public static void* QueryInterface(void* obj, byte[] iid)
    {
        if (obj == null) return null;
        var v = *(FUnknownVtbl**)obj;
        void* result = null;
        fixed (byte* p = iid)
            if (v->QueryInterface(obj, p, &result) != ResultOk) return null;
        return result;
    }

    public static void Release(void* obj)
    {
        if (obj == null) return;
        var v = *(FUnknownVtbl**)obj;
        v->Release(obj);
    }

    public static bool IidEquals(byte* a, byte[] b)
    {
        fixed (byte* bp = b)
            for (var i = 0; i < 16; i++) if (a[i] != bp[i]) return false;
        return true;
    }

    // ============================ String helpers ============================

    public static string ReadAscii(byte* p, int max)
    {
        if (p == null) return string.Empty;
        var len = 0;
        while (len < max && p[len] != 0) len++;
        return System.Text.Encoding.ASCII.GetString(p, len);
    }

    /// <summary>Reads a UTF-16 String128-style buffer (char16[max/2]) up to its NUL.</summary>
    public static string ReadUtf16(byte* p, int maxBytes)
    {
        if (p == null) return string.Empty;
        var chars = (char*)p;
        var len = 0;
        var maxChars = maxBytes / 2;
        while (len < maxChars && chars[len] != '\0') len++;
        return new string(chars, 0, len);
    }

    /// <summary>Writes a managed string as a NUL-terminated UTF-16 buffer into <paramref name="dst"/>.</summary>
    public static void WriteUtf16(byte* dst, string s, int maxBytes)
    {
        if (dst == null) return;
        var chars = (char*)dst;
        var maxChars = maxBytes / 2 - 1;
        var n = Math.Min(s.Length, maxChars);
        for (var i = 0; i < n; i++) chars[i] = s[i];
        chars[n] = '\0';
    }

    public static byte[] HexToTuid(string hex)
    {
        var b = new byte[16];
        for (var i = 0; i < 16 && i * 2 + 1 < hex.Length; i++)
            b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return b;
    }

    public static string TuidToHex(byte* cid)
    {
        var sb = new System.Text.StringBuilder(32);
        for (var i = 0; i < 16; i++) sb.Append(cid[i].ToString("x2"));
        return sb.ToString();
    }
}
