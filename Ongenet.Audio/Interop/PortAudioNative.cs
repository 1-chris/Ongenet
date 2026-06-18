using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Ongenet.Audio.Interop;

/// <summary>
/// Thin P/Invoke surface over the PortAudio C library. We bind only the handful of functions
/// the engine needs and own this interop directly, rather than depending on a third-party
/// managed wrapper. A <see cref="System.Runtime.InteropServices.NativeLibrary"/> resolver maps
/// the logical name "portaudio" to the right binary per platform.
/// </summary>
internal static class PortAudioNative
{
    private const string Lib = "portaudio";

    // PaSampleFormat: 32-bit float, non-interleaved flag NOT set => interleaved.
    public const uint PaFloat32 = 0x00000001;

    // PaStreamCallbackResult.
    public const int PaContinue = 0;
    public const int PaComplete = 1;

    // Sentinel device index meaning "no device" (paNoDevice).
    public const int PaNoDevice = -1;

    // paNoFlag / paClipOff etc. We use no special stream flags.
    public const uint PaNoFlag = 0;

    /// <summary>
    /// Mirror of PortAudio's <c>PaDeviceInfo</c>. No <c>unsigned long</c> fields, so the default
    /// sequential layout is correct on every 64-bit platform (pointers are 8 bytes throughout).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PaDeviceInfo
    {
        public int structVersion;
        public IntPtr name; // const char*
        public int hostApi;
        public int maxInputChannels;
        public int maxOutputChannels;
        public double defaultLowInputLatency;
        public double defaultLowOutputLatency;
        public double defaultHighInputLatency;
        public double defaultHighOutputLatency;
        public double defaultSampleRate;
    }

    /// <summary>Mirror of PortAudio's <c>PaHostApiInfo</c> (used only for the host-API display name).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PaHostApiInfo
    {
        public int structVersion;
        public int type;
        public IntPtr name; // const char*
        public int deviceCount;
        public int defaultInputDevice;
        public int defaultOutputDevice;
    }

    /// <summary>
    /// Mirror of PortAudio's <c>PaStreamParameters</c>. <c>sampleFormat</c> is C's <c>unsigned long</c>
    /// (4 bytes on Windows LLP64, 8 on Unix LP64); we declare it as a 8-byte <see cref="ulong"/>, which
    /// gives byte-for-byte identical field offsets on both because the following <c>double</c> forces
    /// 8-byte alignment anyway, and on little-endian x64/arm64 the low 4 bytes carry the value Windows
    /// reads. Always zero-initialise before use so the high bytes are clean.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PaStreamParameters
    {
        public int device;
        public int channelCount;
        public ulong sampleFormat;
        public double suggestedLatency;
        public IntPtr hostApiSpecificStreamInfo;
    }

    /// <summary>
    /// PortAudio stream callback. Note: C's <c>unsigned long</c> is mapped to <see cref="uint"/>;
    /// frame counts and status flags are small, and on the x64 calling conventions each argument
    /// occupies its own register, so this is correct on both Windows (LLP64) and Linux (LP64).
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int PaStreamCallback(
        IntPtr input,
        IntPtr output,
        uint frameCount,
        IntPtr timeInfo,
        uint statusFlags,
        IntPtr userData);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int Pa_Initialize();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int Pa_Terminate();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int Pa_OpenDefaultStream(
        out IntPtr stream,
        int numInputChannels,
        int numOutputChannels,
        uint sampleFormat,
        double sampleRate,
        uint framesPerBuffer,
        PaStreamCallback streamCallback,
        IntPtr userData);

    // Opens a stream on explicit devices. inputParameters / outputParameters are pointers to a
    // PaStreamParameters (or IntPtr.Zero for "none"); this is what lets us pick a device, unlike
    // Pa_OpenDefaultStream. streamFlags is C's unsigned long but is passed by register, so uint is fine.
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int Pa_OpenStream(
        out IntPtr stream,
        IntPtr inputParameters,
        IntPtr outputParameters,
        double sampleRate,
        uint framesPerBuffer,
        uint streamFlags,
        PaStreamCallback streamCallback,
        IntPtr userData);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int Pa_GetDeviceCount();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int Pa_GetDefaultInputDevice();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int Pa_GetDefaultOutputDevice();

    // Returns a const PaDeviceInfo* owned by PortAudio (do not free); IntPtr.Zero if the index is bad.
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr Pa_GetDeviceInfo(int device);

    // Returns a const PaHostApiInfo* owned by PortAudio (do not free); IntPtr.Zero if the index is bad.
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr Pa_GetHostApiInfo(int hostApi);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int Pa_StartStream(IntPtr stream);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int Pa_StopStream(IntPtr stream);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int Pa_CloseStream(IntPtr stream);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr Pa_GetErrorText(int errorCode);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr Pa_GetVersionText();

    /// <summary>Decodes a PortAudio error code to its message.</summary>
    public static string ErrorText(int code)
    {
        var ptr = Pa_GetErrorText(code);
        return ptr == IntPtr.Zero ? $"PortAudio error {code}" : Marshal.PtrToStringAnsi(ptr) ?? $"PortAudio error {code}";
    }

    // --- Refcounted lifetime -------------------------------------------------------------
    // Several components (output stream, input stream, device enumeration) need PortAudio
    // initialised. Refcount Pa_Initialize/Pa_Terminate so they can come and go independently
    // and the library is only torn down once nothing is using it.

    private static int _initCount;
    private static readonly object InitLock = new();

    /// <summary>Ensures PortAudio is initialised, registering the library resolver first. Refcounted.</summary>
    public static void PaRef()
    {
        lock (InitLock)
        {
            if (_initCount == 0)
            {
                EnsureResolver();
                var code = Pa_Initialize();
                if (code < 0) throw new InvalidOperationException($"Pa_Initialize failed: {ErrorText(code)}");
            }

            _initCount++;
        }
    }

    /// <summary>Releases one PortAudio reference, terminating the library when the last one goes.</summary>
    public static void PaUnref()
    {
        lock (InitLock)
        {
            if (_initCount == 0) return;
            _initCount--;
            if (_initCount == 0) Pa_Terminate();
        }
    }

    // --- Native library resolution -------------------------------------------------------

    private static bool _resolverRegistered;
    private static readonly object ResolverLock = new();

    /// <summary>
    /// Registers the resolver that maps "portaudio" to the platform's binary. Safe to call
    /// repeatedly. Call before the first P/Invoke.
    /// </summary>
    public static void EnsureResolver()
    {
        if (_resolverRegistered) return;
        lock (ResolverLock)
        {
            if (_resolverRegistered) return;
            NativeLibrary.SetDllImportResolver(typeof(PortAudioNative).Assembly, Resolve);
            _resolverRegistered = true;
        }
    }

    private static IntPtr Resolve(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != Lib) return IntPtr.Zero;

        var appDir = AppContext.BaseDirectory;
        foreach (var candidate in CandidateNames())
        {
            // 1. A copy bundled next to the app. dlopen/LoadLibrary won't probe the app directory for a
            //    bare name on macOS/Linux, so try the absolute path explicitly — this is what lets a
            //    portaudio.dll / libportaudio.*.dylib / libportaudio.so.2 shipped with the app be found.
            if (!string.IsNullOrEmpty(appDir))
            {
                var local = Path.Combine(appDir, candidate);
                if (File.Exists(local) && NativeLibrary.TryLoad(local, out var bundled)) return bundled;
            }

            // 2. The system search path (preinstalled, e.g. a distro package or Homebrew).
            if (NativeLibrary.TryLoad(candidate, out var handle)) return handle;
        }

        return IntPtr.Zero;
    }

    private static string[] CandidateNames()
    {
        if (OperatingSystem.IsWindows())
        {
            // Shipped alongside the app (Windows never preinstalls PortAudio).
            return new[] { "portaudio.dll", "portaudio", "libportaudio" };
        }

        if (OperatingSystem.IsMacOS())
        {
            return new[] { "libportaudio.dylib", "libportaudio.2.dylib", "portaudio" };
        }

        // Linux / other: prefer the versioned soname that distros ship.
        return new[] { "libportaudio.so.2", "libportaudio.so", "portaudio" };
    }
}
