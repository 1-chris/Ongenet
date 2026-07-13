namespace Ongenet.Core.Services.Interfaces;

/// <summary>Muxes a rendered WAV master with an existing video file via ffmpeg.</summary>
public interface IVideoMuxer
{
    bool IsAvailable { get; }

    void Mux(string wavPath, string videoPath, double videoOffsetSeconds, string outputPath,
        double inPointSeconds = 0, double outPointSeconds = 0);
}
