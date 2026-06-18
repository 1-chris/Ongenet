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
