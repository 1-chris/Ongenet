using System;

namespace Ongenet.App.Services;

/// <summary>
/// UI refresh intervals that differ on the browser demo, where Avalonia and Web Audio
/// (<c>ScriptProcessorNode</c>) contend for the same main thread. Desktop keeps the snappy
/// defaults; browser slows (and while playing, freezes) live UI so DSP callbacks are less
/// likely to miss deadlines.
/// </summary>
public static class UiPerfProfile
{
    public static bool IsConstrained => OperatingSystem.IsBrowser();

    /// <summary>FrameTicker idle poll (transport / meters when not playing).</summary>
    public static int FrameIdleIntervalMs => IsConstrained ? 250 : 100;

    /// <summary>FrameTicker while playing (playhead overlays only on web).</summary>
    public static int FrameFastIntervalMs => IsConstrained ? 200 : 33;

    /// <summary>Minimum spacing between <see cref="PlaybackClock"/> fan-out ticks when active.</summary>
    public static int PlaybackClockMinIntervalMs => IsConstrained ? 200 : 30;

    /// <summary>Standalone analyser / grain-monitor DispatcherTimer interval.</summary>
    public static int AnalyserIntervalMs => IsConstrained ? 200 : 33;

    /// <summary>Grain cloud monitor prefers 60 fps on desktop; slow on browser.</summary>
    public static int GrainMonitorIntervalMs => IsConstrained ? 200 : 16;

    /// <summary>
    /// When true, skip mixer/lane meters, parameter auto-refresh, and <see cref="PlaybackClock"/>
    /// fan-out while the transport is playing — the largest Avalonia cost competing with audio.
    /// </summary>
    public static bool SuppressLiveUiWhilePlaying => IsConstrained;
}
