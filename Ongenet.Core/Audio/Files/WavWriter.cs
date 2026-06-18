using System;
using System.IO;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Streams interleaved float samples to a 16-bit PCM WAV file. The RIFF/data sizes are written as
/// placeholders up front and patched on <see cref="Dispose"/>, so arbitrarily long renders need no
/// in-memory buffer.
/// </summary>
public sealed class WavWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private readonly int _channels;
    private readonly int _sampleRate;
    private long _dataBytes;
    private bool _disposed;

    public WavWriter(string path, int channels, int sampleRate)
    {
        _channels = channels < 1 ? 1 : channels;
        _sampleRate = sampleRate <= 0 ? 44100 : sampleRate;
        _stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        _writer = new BinaryWriter(_stream);
        WriteHeader(0);
    }

    private void WriteHeader(int dataBytes)
    {
        var byteRate = _sampleRate * _channels * 2;
        _writer.Write("RIFF".ToCharArray());
        _writer.Write(36 + dataBytes);
        _writer.Write("WAVE".ToCharArray());
        _writer.Write("fmt ".ToCharArray());
        _writer.Write(16);
        _writer.Write((ushort)1);             // PCM
        _writer.Write((ushort)_channels);
        _writer.Write(_sampleRate);
        _writer.Write(byteRate);
        _writer.Write((ushort)(_channels * 2)); // block align
        _writer.Write((ushort)16);              // bits per sample
        _writer.Write("data".ToCharArray());
        _writer.Write(dataBytes);
    }

    /// <summary>Writes a block of interleaved float samples (clamped, converted to 16-bit).</summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            var s = sample > 1f ? 1f : sample < -1f ? -1f : sample;
            _writer.Write((short)(s * 32767f));
            _dataBytes += 2;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writer.Flush();
        _stream.Seek(0, SeekOrigin.Begin);
        WriteHeader((int)_dataBytes);
        _writer.Flush();
        _writer.Dispose();
        _stream.Dispose();
    }
}
