namespace Ongenet.Core.Audio;

/// <summary>
/// Cross-cutting realtime audio preferences applied before the device opens.
/// </summary>
public static class AudioRuntimeOptions
{
    /// <summary>
    /// macOS CoreAudio producer lead in frames (jitter slack / output latency).
    /// 2048 ≈ 43 ms @ 48 kHz; 4096 ≈ 85 ms. Environment variable
    /// <c>ONGENET_CA_LEAD_FRAMES</c> overrides this when set.
    /// </summary>
    public static int CoreAudioLeadFrames { get; set; } = 2048;
}
