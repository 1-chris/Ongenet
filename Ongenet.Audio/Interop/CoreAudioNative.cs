using System;
using System.Runtime.InteropServices;

namespace Ongenet.Audio.Interop;

/// <summary>
/// P/Invoke surface over Apple's CoreAudio stack for raw audio: the HAL output AudioUnit (AUHAL) for
/// low-latency playback/capture, plus the HAL property API for device enumeration. Only touched on
/// macOS (the backend guards by OS); on Linux these framework paths simply never resolve, so the
/// type compiles and ships everywhere but binds lazily on a Mac. Transcribed without headers — exact
/// byte layouts/selectors need on-device shakeout, like the CoreMIDI interop.
/// </summary>
internal static class CoreAudioNative
{
    public const string AudioUnit = "/System/Library/Frameworks/AudioUnit.framework/AudioUnit";
    public const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    public const string CoreAudio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";

    // --- four-char-code helpers ------------------------------------------------------------------
    public static uint FourCC(string s) => (uint)(((byte)s[0] << 24) | ((byte)s[1] << 16) | ((byte)s[2] << 8) | (byte)s[3]);

    // AudioComponentDescription codes.
    public static readonly uint kAudioUnitType_Output = FourCC("auou");
    public static readonly uint kAudioUnitSubType_HALOutput = FourCC("ahal");
    public static readonly uint kAudioUnitManufacturer_Apple = FourCC("appl");

    // ASBD.
    public static readonly uint kAudioFormatLinearPCM = FourCC("lpcm");
    public const uint kAudioFormatFlagIsFloat = 0x1;
    public const uint kAudioFormatFlagIsPacked = 0x8;

    // AudioUnit property ids (plain integers) + scopes.
    public const uint kAudioOutputUnitProperty_CurrentDevice = 2000;
    public const uint kAudioOutputUnitProperty_EnableIO = 2001;
    public const uint kAudioOutputUnitProperty_SetInputCallback = 2005;
    public const uint kAudioUnitProperty_StreamFormat = 8;
    public const uint kAudioUnitProperty_SetRenderCallback = 23;
    public const uint kAudioUnitScope_Global = 0;
    public const uint kAudioUnitScope_Input = 1;
    public const uint kAudioUnitScope_Output = 2;

    // HAL property selectors/scopes (four-char codes).
    public static readonly uint kAudioHardwarePropertyDevices = FourCC("dev#");
    public static readonly uint kAudioHardwarePropertyDefaultOutputDevice = FourCC("dOut");
    public static readonly uint kAudioHardwarePropertyDefaultInputDevice = FourCC("dIn ");
    public static readonly uint kAudioDevicePropertyDeviceNameCFString = FourCC("lnam");
    public static readonly uint kAudioDevicePropertyStreamConfiguration = FourCC("slay");
    public static readonly uint kAudioDevicePropertyNominalSampleRate = FourCC("nsrt");
    public static readonly uint kAudioObjectPropertyScopeGlobal = FourCC("glob");
    public static readonly uint kAudioObjectPropertyScopeInput = FourCC("inpt");
    public static readonly uint kAudioObjectPropertyScopeOutput = FourCC("outp");
    public const uint kAudioObjectPropertyElementMain = 0; // formerly kAudioObjectPropertyElementMaster
    public const uint kAudioObjectSystemObject = 1;

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
    public struct AudioObjectPropertyAddress
    {
        public uint mSelector;
        public uint mScope;
        public uint mElement;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AURenderCallbackStruct
    {
        public IntPtr inputProc;
        public IntPtr inputProcRefCon;
    }

    // --- AudioComponent / AudioUnit lifecycle (AudioToolbox + AudioUnit) --------------------------

    [DllImport(AudioToolbox)]
    public static extern IntPtr AudioComponentFindNext(IntPtr inComponent, ref AudioComponentDescription desc);

    [DllImport(AudioToolbox)]
    public static extern int AudioComponentInstanceNew(IntPtr comp, out IntPtr unit);

    [DllImport(AudioToolbox)]
    public static extern int AudioComponentInstanceDispose(IntPtr unit);

    [DllImport(AudioUnit)]
    public static extern int AudioUnitSetProperty(IntPtr unit, uint propID, uint scope, uint element, IntPtr data, uint dataSize);

    [DllImport(AudioUnit)]
    public static extern int AudioUnitGetProperty(IntPtr unit, uint propID, uint scope, uint element, IntPtr data, ref uint ioDataSize);

    [DllImport(AudioUnit)]
    public static extern int AudioUnitInitialize(IntPtr unit);

    [DllImport(AudioUnit)]
    public static extern int AudioUnitUninitialize(IntPtr unit);

    [DllImport(AudioUnit)]
    public static extern int AudioOutputUnitStart(IntPtr unit);

    [DllImport(AudioUnit)]
    public static extern int AudioOutputUnitStop(IntPtr unit);

    // Called from the input callback to pull captured frames into our AudioBufferList.
    [DllImport(AudioUnit)]
    public static extern int AudioUnitRender(IntPtr unit, IntPtr ioActionFlags, IntPtr inTimeStamp,
        uint inBusNumber, uint inNumberFrames, IntPtr ioData);

    // --- HAL property API (CoreAudio) ------------------------------------------------------------

    [DllImport(CoreAudio)]
    public static extern int AudioObjectGetPropertyDataSize(uint objectId, ref AudioObjectPropertyAddress addr,
        uint qualifierDataSize, IntPtr qualifierData, out uint outDataSize);

    [DllImport(CoreAudio)]
    public static extern int AudioObjectGetPropertyData(uint objectId, ref AudioObjectPropertyAddress addr,
        uint qualifierDataSize, IntPtr qualifierData, ref uint ioDataSize, IntPtr outData);

    public static bool TryProbe()
    {
        try { return NativeLibrary.TryLoad(AudioUnit, out _); }
        catch { return false; }
    }
}
