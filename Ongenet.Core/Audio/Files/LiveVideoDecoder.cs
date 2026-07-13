using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Ongenet.Core.Audio.Files;

/// <summary>Persistent ffmpeg decoder streaming raw RGB frames for live preview.</summary>
public sealed class LiveVideoDecoder : IDisposable
{
    private Process? _process;
    private Stream? _stdout;
    private int _width;
    private int _height;
    private readonly object _gate = new();
    private double _lastSeek = double.NaN;

    public bool IsRunning => _process is { HasExited: false };
    public int Width => _width;
    public int Height => _height;

    public static bool IsAvailable => FfmpegVideoFrameExtractor.IsAvailable;

    public bool Open(string videoPath, double startSeconds, int width = 1280, int height = 720)
    {
        Close();
        if (!File.Exists(videoPath)) return false;
        var ffmpeg = FfmpegEncoder.Locate();
        if (ffmpeg is null) return false;

        _width = width;
        _height = height;
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("error");
        if (startSeconds > 1e-6)
        {
            psi.ArgumentList.Add("-ss");
            psi.ArgumentList.Add(startSeconds.ToString("0.###"));
        }
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(videoPath);
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("rawvideo");
        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add("rgb24");
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add($"{width}x{height}");
        psi.ArgumentList.Add("-");

        try
        {
            _process = Process.Start(psi);
            _stdout = _process?.StandardOutput.BaseStream;
            _lastSeek = startSeconds;
            return _process is not null && _stdout is not null;
        }
        catch
        {
            Close();
            return false;
        }
    }

    public byte[]? ReadFrame()
    {
        lock (_gate)
        {
            if (_stdout is null || _width <= 0 || _height <= 0) return null;
            var size = _width * _height * 3;
            var buf = new byte[size];
            var read = 0;
            while (read < size)
            {
                var n = _stdout.Read(buf, read, size - read);
                if (n <= 0) return null;
                read += n;
            }

            return buf;
        }
    }

    public void Seek(string videoPath, double seconds)
    {
        if (Math.Abs(seconds - _lastSeek) < 0.05 && IsRunning) return;
        Open(videoPath, seconds, _width, _height);
    }

    public void Close()
    {
        lock (_gate)
        {
            try { _stdout?.Dispose(); } catch { /* ignore */ }
            _stdout = null;
            if (_process is { HasExited: false })
            {
                try { _process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            }
            _process?.Dispose();
            _process = null;
        }
    }

    public void Dispose() => Close();
}
