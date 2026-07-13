using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>Persistent ffmpeg decoder streaming raw RGB frames for live video preview.</summary>
public interface ILiveVideoDecoder : IDisposable
{
    bool IsRunning { get; }
    int Width { get; }
    int Height { get; }
    static bool IsAvailable => false;

    bool Open(string videoPath, double startSeconds, int width = 1280, int height = 720);
    byte[]? ReadFrame();
    void Seek(string videoPath, double seconds);
    void Close();
}
