namespace Ongenet.Core.Models.Audio;

/// <summary>Per-channel surround pan gains for immersive mixing (5.1 / 7.1).</summary>
public sealed class SurroundChannelPan
{
    public double FrontLeft { get; set; } = 1;
    public double FrontRight { get; set; } = 1;
    public double Center { get; set; }
    public double Lfe { get; set; }
    public double SurroundLeft { get; set; }
    public double SurroundRight { get; set; }
    public double RearLeft { get; set; }
    public double RearRight { get; set; }
}
