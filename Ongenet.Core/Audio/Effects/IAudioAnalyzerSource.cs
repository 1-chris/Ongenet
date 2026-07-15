namespace Ongenet.Core.Audio.Effects;

/// <summary>Effects that expose level/phase/loudness analysis for Tool/Wave Candy meter UI.</summary>
public interface IAudioAnalyzerSource
{
    float PeakLeft { get; }
    float PeakRight { get; }
    float Rms { get; }
    float Correlation { get; }
    float PhaseDegrees { get; }

    /// <summary>Short-term LUFS (3 s), or −∞ if not measured.</summary>
    float ShortTermLufs { get; }

    /// <summary>Integrated LUFS, or −∞ if not measured.</summary>
    float IntegratedLufs { get; }

    /// <summary>True-peak max (dBTP) held since prepare/reset.</summary>
    float MaxTruePeakDbTp { get; }
}
