namespace Ongenet.Core.Services.Interfaces;

/// <summary>Extracts single video frames via ffmpeg for scrub preview.</summary>
public interface IVideoFrameExtractor
{
    bool IsAvailable { get; }
    byte[]? ExtractFramePng(string videoPath, double timeSeconds);
}
