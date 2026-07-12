using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Ongenet.Au.Interop;

/// <summary>
/// P/Invoke surface over Apple's Audio Unit / Audio Component stack for <b>hosting</b> third-party
/// Audio Units: enumeration + instantiation (<c>AudioComponent*</c>), property/parameter access,
/// pull-model rendering (<c>AudioUnitRender</c>), MIDI dispatch (<c>MusicDeviceMIDIEvent</c>), plus
/// the sliver of CoreFoundation + the Objective-C runtime needed to read <c>CFString</c>s and embed a
/// plugin's Cocoa view. Only touched on macOS (the provider guards by OS); the framework paths simply
/// never resolve elsewhere, so the type compiles and ships everywhere but binds lazily on a Mac.
///
/// The layouts/selectors are transcribed from Apple's public headers; exact byte offsets need
/// on-device shakeout, like the sibling CoreAudio / CoreMIDI interop in Ongenet.Audio.
/// </summary>
internal static unsafe class AudioUnitApi
{
    public const string AudioUnit = "/System/Library/Frameworks/AudioUnit.framework/AudioUnit";
    public const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    public const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    public const string ObjC = "/usr/lib/libobjc.A.dylib";

    public const uint kCFStringEncodingUTF8 = 0x08000100;

    // --- four-char-code helpers ------------------------------------------------------------------
    public static uint FourCC(string s) => (uint)(((byte)s[0] << 24) | ((byte)s[1] << 16) | ((byte)s[2] << 8) | (byte)s[3]);

    // AudioComponent types we care about (componentType codes).
    public static readonly uint kAudioUnitType_Output = FourCC("auou");
    public static readonly uint kAudioUnitType_MusicDevice = FourCC("aumu"); // instruments (MIDI in, audio out)
    public static readonly uint kAudioUnitType_MusicEffect = FourCC("aumf"); // MIDI + audio in, audio out
    public static readonly uint kAudioUnitType_Effect = FourCC("aufx");      // audio in, audio out
    public static readonly uint kAudioUnitType_Generator = FourCC("augn");
    public static readonly uint kAudioUnitType_FormatConverter = FourCC("aufc");

    // ASBD (canonical float stream format).
    public static readonly uint kAudioFormatLinearPCM = FourCC("lpcm");
    public const uint kAudioFormatFlagIsFloat = 0x1;
    public const uint kAudioFormatFlagIsPacked = 0x8;
    public const uint kAudioFormatFlagIsNonInterleaved = 0x20;

    // AudioUnit property ids (plain integers) + scopes.
    public const uint kAudioUnitProperty_ParameterList = 3;
    public const uint kAudioUnitProperty_ParameterInfo = 4;
    public const uint kAudioUnitProperty_StreamFormat = 8;
    public const uint kAudioUnitProperty_Latency = 12;
    public const uint kAudioUnitProperty_MaximumFramesPerSlice = 14;
    public const uint kAudioUnitProperty_SetRenderCallback = 23;
    public const uint kAudioUnitProperty_CocoaUI = 31;

    public const uint kAudioUnitScope_Global = 0;
    public const uint kAudioUnitScope_Input = 1;
    public const uint kAudioUnitScope_Output = 2;

    // AudioTimeStamp flags.
    public const uint kAudioTimeStampSampleTimeValid = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioComponentDescription
    {
        public uint componentType;
        public uint componentSubType;
        public uint componentManufacturer;
        public uint componentFlags;
        public uint componentFlagsMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioStreamBasicDescription
    {
        public double mSampleRate;
        public uint mFormatID;
        public uint mFormatFlags;
        public uint mBytesPerPacket;
        public uint mFramesPerPacket;
        public uint mBytesPerFrame;
        public uint mChannelsPerFrame;
        public uint mBitsPerChannel;
        public uint mReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SMPTETime
    {
        public short mSubframes;
        public short mSubframeDivisor;
        public uint mCounter;
        public uint mType;
        public uint mFlags;
        public short mHours;
        public short mMinutes;
        public short mSeconds;
        public short mFrames;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioTimeStamp
    {
        public double mSampleTime;
        public ulong mHostTime;
        public double mRateScalar;
        public ulong mWordClockTime;
        public SMPTETime mSMPTETime;
        public uint mFlags;
        public uint mReserved;
    }

    // One buffer of an AudioBufferList. The list itself is variable-length (a UInt32 count followed by
    // an array of these, with 4 bytes of padding after the count on a 64-bit ABI) so we build it by hand.
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioBuffer
    {
        public uint mNumberChannels;
        public uint mDataByteSize;
        public IntPtr mData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AURenderCallbackStruct
    {
        public IntPtr inputProc;
        public IntPtr inputProcRefCon;
    }

    // char name[52]; CFStringRef unitName; UInt32 clumpID; CFStringRef cfNameString; UInt32 unit;
    // Float32 min/max/default; UInt32 flags. Default field alignment matches the C layout on 64-bit.
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioUnitParameterInfo
    {
        public fixed byte name[52];
        public IntPtr unitName;
        public uint clumpID;
        public IntPtr cfNameString;
        public uint unit;
        public float minValue;
        public float maxValue;
        public float defaultValue;
        public uint flags;
    }

    // CFURLRef bundle location + a (variable-length) array of CFStringRef class names; we read the first.
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioUnitCocoaViewInfo
    {
        public IntPtr mCocoaAUViewBundleLocation;
        public IntPtr mCocoaAUViewClass0;
    }

    // --- AudioComponent / AudioUnit lifecycle (AudioToolbox + AudioUnit) --------------------------

    [DllImport(AudioToolbox)]
    public static extern IntPtr AudioComponentFindNext(IntPtr inComponent, ref AudioComponentDescription desc);

    [DllImport(AudioToolbox)]
    public static extern int AudioComponentGetDescription(IntPtr comp, out AudioComponentDescription desc);

    [DllImport(AudioToolbox)]
    public static extern int AudioComponentCopyName(IntPtr comp, out IntPtr outName);

    [DllImport(AudioToolbox)]
    public static extern int AudioComponentInstanceNew(IntPtr comp, out IntPtr unit);

    [DllImport(AudioToolbox)]
    public static extern int AudioComponentInstanceDispose(IntPtr unit);

    [DllImport(AudioUnit)]
    public static extern int AudioUnitInitialize(IntPtr unit);

    [DllImport(AudioUnit)]
    public static extern int AudioUnitUninitialize(IntPtr unit);

    [DllImport(AudioUnit)]
    public static extern int AudioUnitSetProperty(IntPtr unit, uint propID, uint scope, uint element, void* data, uint dataSize);

    [DllImport(AudioUnit)]
    public static extern int AudioUnitGetProperty(IntPtr unit, uint propID, uint scope, uint element, void* data, ref uint ioDataSize);

    [DllImport(AudioUnit)]
    public static extern int AudioUnitGetPropertyInfo(IntPtr unit, uint propID, uint scope, uint element, out uint outDataSize, out byte outWritable);

    [DllImport(AudioUnit)]
    public static extern int AudioUnitGetParameter(IntPtr unit, uint paramID, uint scope, uint element, out float value);

    [DllImport(AudioUnit)]
    public static extern int AudioUnitSetParameter(IntPtr unit, uint paramID, uint scope, uint element, float value, uint bufferOffsetInFrames);

    [DllImport(AudioUnit)]
    public static extern int AudioUnitRender(IntPtr unit, uint* ioActionFlags, AudioTimeStamp* inTimeStamp,
        uint inBusNumber, uint inNumberFrames, void* ioData);

    // MusicDeviceComponent is an AudioUnit; MusicDeviceMIDIEvent lives in AudioToolbox.
    [DllImport(AudioToolbox)]
    public static extern int MusicDeviceMIDIEvent(IntPtr unit, uint inStatus, uint inData1, uint inData2, uint inOffsetSampleFrame);

    // --- CoreFoundation (CFString / CFURL) -------------------------------------------------------

    [DllImport(CoreFoundation)]
    public static extern void CFRelease(IntPtr cf);

    [DllImport(CoreFoundation)]
    public static extern nint CFStringGetLength(IntPtr theString);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CFStringGetCString(IntPtr theString, byte[] buffer, nint bufferSize, uint encoding);

    /// <summary>Copies a CFString (does not release it) into a managed string, or null on failure.</summary>
    public static string? CFStringToManaged(IntPtr cfString)
    {
        if (cfString == IntPtr.Zero) return null;
        var len = CFStringGetLength(cfString);
        var bufSize = (int)((len + 1) * 4 + 1);
        var buf = new byte[bufSize];
        if (!CFStringGetCString(cfString, buf, buf.Length, kCFStringEncodingUTF8)) return null;
        var end = Array.IndexOf(buf, (byte)0);
        if (end < 0) end = buf.Length;
        return Encoding.UTF8.GetString(buf, 0, end);
    }

    // --- Objective-C runtime (editor embedding only) ---------------------------------------------

    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    public static extern IntPtr Sel(string name);

    [DllImport(ObjC, EntryPoint = "objc_getClass")]
    public static extern IntPtr GetClass(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr MsgSend_Ptr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr MsgSend_PtrSize(IntPtr receiver, IntPtr selector, IntPtr arg1, CGSize arg2);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern void MsgSend_Size(IntPtr receiver, IntPtr selector, CGSize arg1);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern void MsgSend_ULong(IntPtr receiver, IntPtr selector, ulong arg1);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool MsgSend_Bool_Sel(IntPtr receiver, IntPtr selector, IntPtr arg1);

    // NSRect (frame) return: on arm64 a >16-byte struct return uses the normal objc_msgSend (indirect
    // result register); on x86_64 it uses objc_msgSend_stret. We dispatch on the process architecture.
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern CGRect MsgSend_CGRect(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend_stret")]
    public static extern void MsgSend_Stret_CGRect(out CGRect ret, IntPtr receiver, IntPtr selector);

    [StructLayout(LayoutKind.Sequential)]
    public struct CGSize { public double Width; public double Height; }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGRect { public double X, Y, W, H; }

    public static CGRect ViewFrame(IntPtr view, IntPtr frameSel)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            MsgSend_Stret_CGRect(out var r, view, frameSel);
            return r;
        }

        return MsgSend_CGRect(view, frameSel);
    }

    public static bool TryProbe()
    {
        try { return NativeLibrary.TryLoad(AudioToolbox, out _); }
        catch { return false; }
    }
}
