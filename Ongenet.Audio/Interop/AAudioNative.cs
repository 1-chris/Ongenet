using System;
using System.Runtime.InteropServices;

namespace Ongenet.Audio.Interop;

/// <summary>
/// Raw P/Invoke surface for Android's native <c>libaaudio.so</c> (AAudio), the low-latency audio API
/// available since Android 8.0 / API 26. Only the output path the Android backend needs is bound here.
/// The data callback is taken as a <c>delegate* unmanaged</c> so AAudio's real-time audio thread calls
/// straight into managed code with no marshalling thunk (the same approach the CoreAudio backend uses).
///
/// <para>Mirrors the other Interop bindings (CoreAudioNative / WasapiNative / …): the symbols bind lazily
/// and are only ever resolved when the Android backend actually runs, so this compiles on every OS.</para>
/// </summary>
internal static unsafe class AAudioNative
{
    private const string Lib = "aaudio";

    // aaudio_direction_t
    public const int DIRECTION_OUTPUT = 0;

    // aaudio_format_t — 32-bit float, the engine's native sample format.
    public const int FORMAT_PCM_FLOAT = 2;

    // aaudio_sharing_mode_t
    public const int SHARING_MODE_SHARED = 1;

    // aaudio_performance_mode_t
    public const int PERFORMANCE_MODE_LOW_LATENCY = 12;

    // aaudio_data_callback_result_t
    public const int CALLBACK_RESULT_CONTINUE = 0;

    // aaudio_result_t
    public const int OK = 0;

    [DllImport(Lib)] public static extern int AAudio_createStreamBuilder(out IntPtr builder);
    [DllImport(Lib)] public static extern void AAudioStreamBuilder_setDirection(IntPtr builder, int direction);
    [DllImport(Lib)] public static extern void AAudioStreamBuilder_setFormat(IntPtr builder, int format);
    [DllImport(Lib)] public static extern void AAudioStreamBuilder_setChannelCount(IntPtr builder, int channelCount);
    [DllImport(Lib)] public static extern void AAudioStreamBuilder_setSampleRate(IntPtr builder, int sampleRate);
    [DllImport(Lib)] public static extern void AAudioStreamBuilder_setSharingMode(IntPtr builder, int sharingMode);
    [DllImport(Lib)] public static extern void AAudioStreamBuilder_setPerformanceMode(IntPtr builder, int mode);

    [DllImport(Lib)]
    public static extern void AAudioStreamBuilder_setDataCallback(
        IntPtr builder,
        delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, int, int> callback,
        IntPtr userData);

    [DllImport(Lib)] public static extern int AAudioStreamBuilder_openStream(IntPtr builder, out IntPtr stream);
    [DllImport(Lib)] public static extern int AAudioStreamBuilder_delete(IntPtr builder);

    [DllImport(Lib)] public static extern int AAudioStream_requestStart(IntPtr stream);
    [DllImport(Lib)] public static extern int AAudioStream_requestStop(IntPtr stream);
    [DllImport(Lib)] public static extern int AAudioStream_close(IntPtr stream);
    [DllImport(Lib)] public static extern int AAudioStream_getSampleRate(IntPtr stream);
    [DllImport(Lib)] public static extern int AAudioStream_getChannelCount(IntPtr stream);
}
