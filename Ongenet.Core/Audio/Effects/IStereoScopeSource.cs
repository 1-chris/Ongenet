namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Exposes recent left/right sample pairs for vectorscope / goniometer displays.
/// </summary>
public interface IStereoScopeSource
{
    /// <summary>
    /// Copies the most recent stereo pairs into <paramref name="left"/> and <paramref name="right"/>.
    /// Returns the number of frames written (min of buffer lengths).
    /// </summary>
    int CaptureLatestStereo(float[] left, float[] right);
}
