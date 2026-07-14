namespace Ongenet.Core.Audio.Effects;

/// <summary>Effects that expose level/phase analysis for Tool-style meter UI.</summary>
public interface IAudioAnalyzerSource
{
    float PeakLeft { get; }
    float PeakRight { get; }
    float Rms { get; }
    float Correlation { get; }
    float PhaseDegrees { get; }
}
