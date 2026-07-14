namespace Ongenet.Core.Audio.Files;

/// <summary>One beat-slice region stored on an <see cref="AudioSampleBuffer"/>.</summary>
public sealed class AudioSliceRegion
{
    public long StartFrame { get; set; }
    public long EndFrame { get; set; }
    public int Order { get; set; }
    public bool Selected { get; set; } = true;
}
