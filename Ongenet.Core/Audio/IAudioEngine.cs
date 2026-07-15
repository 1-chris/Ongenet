using System;

namespace Ongenet.Core.Audio;

public enum MasterMeterTap
{
    PostFader,
    PreLimiter,
    PostChain
}

/// <summary>
/// The real-time audio engine: owns the device, mixes the project's sources, and renders to
/// the output. A live runtime object distinct from the project data model.
/// </summary>
public interface IAudioEngine : IDisposable
{
    /// <summary>Whether the engine is started and streaming.</summary>
    bool IsRunning { get; }

    /// <summary>The format the engine/device runs at.</summary>
    AudioFormat Format { get; }

    /// <summary>Master output peak level (0..1, with release) for the left channel.</summary>
    float MasterLevelLeft { get; }

    /// <summary>Master output peak level (0..1, with release) for the right channel.</summary>
    float MasterLevelRight { get; }

    /// <summary>True-peak (dBTP) of the latest master bus block, left channel.</summary>
    float MasterTruePeakLeftDbTp { get; }

    /// <summary>True-peak (dBTP) of the latest master bus block, right channel.</summary>
    float MasterTruePeakRightDbTp { get; }

    /// <summary>Session-max true peak across both channels (dBTP).</summary>
    float MasterTruePeakMaxDbTp { get; }

    /// <summary>K-weighted momentary loudness (LUFS).</summary>
    float MasterMomentaryLufs { get; }

    /// <summary>K-weighted short-term loudness (LUFS).</summary>
    float MasterShortTermLufs { get; }

    /// <summary>K-weighted gated integrated loudness (LUFS).</summary>
    float MasterIntegratedLufs { get; }

    /// <summary>EBU R128 Loudness Range (LU). NaN until enough history.</summary>
    float MasterLoudnessRangeLu { get; }

    /// <summary>Stereo correlation at the selected master meter tap (−1..+1).</summary>
    float MasterCorrelation { get; }

    /// <summary>Stage feeding the title-bar master meters.</summary>
    MasterMeterTap MasterMeterTap { get; set; }

    /// <summary>Resets integrated/session loudness and held true-peak maxima.</summary>
    void ResetMasterLoudness();

    /// <summary>Opens the device and starts rendering the current project.</summary>
    void Start();

    /// <summary>Stops rendering and closes the device.</summary>
    void Stop();
}
