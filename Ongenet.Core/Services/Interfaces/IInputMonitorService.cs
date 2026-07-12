using System;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>
/// Software input monitoring: captures the live input device and mixes it into the master bus
/// for tracks with <see cref="Models.Audio.InputMonitoringMode"/> enabled.
/// </summary>
public interface IInputMonitorService
{
    /// <summary>Whether any track currently requests monitoring.</summary>
    bool IsActive { get; }

    /// <summary>Called from the audio thread to mix monitored input into <paramref name="buffer"/>.</summary>
    void Mix(Span<float> buffer, int channels, int frames);

    /// <summary>Feeds a captured input block (e.g. from recording) into the monitor ring buffer.</summary>
    void PushCapture(ReadOnlySpan<float> input, int channels);

    /// <summary>Re-evaluates which tracks need monitoring and starts/stops capture accordingly.</summary>
    void Refresh();

    /// <summary>Recording has exclusive input — pause standalone capture.</summary>
    void SetRecordingExclusive(bool exclusive);
}

/// <summary>No-op when input monitoring is unavailable.</summary>
public sealed class NullInputMonitorService : IInputMonitorService
{
    public bool IsActive => false;
    public void Mix(Span<float> buffer, int channels, int frames) { }
    public void PushCapture(ReadOnlySpan<float> input, int channels) { }
    public void Refresh() { }
    public void SetRecordingExclusive(bool exclusive) { }
}
