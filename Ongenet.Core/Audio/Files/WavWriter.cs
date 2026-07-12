using System;
using System.IO;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Streams interleaved float samples to a PCM WAV file (16-, 24-, or 32-bit). The RIFF/data sizes are
/// written as placeholders up front and patched on <see cref="Dispose"/>, so arbitrarily long renders
/// need no in-memory buffer.
/// </summary>
public sealed class WavWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly int _bitsPerSample;
    private readonly int _bytesPerSample;
    private long _dataBytes;
    private bool _disposed;

    public WavWriter(string path, int channels, int sampleRate, int bitsPerSample = 16)
    {
        _channels = channels < 1 ? 1 : channels;
        _sampleRate = sampleRate <= 0 ? 44100 : sampleRate;
        _bitsPerSample = bitsPerSample switch
        {
            24 => 24,
            32 => 32,
            _ => 16
        };
        _bytesPerSample = _bitsPerSample / 8;
        _stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        _writer = new BinaryWriter(_stream);
        WriteHeader(0);
    }

    private void WriteHeader(int dataBytes)
    {
        var blockAlign = _channels * _bytesPerSample;
        var byteRate = _sampleRate * blockAlign;
        _writer.Write("RIFF".ToCharArray());
        _writer.Write(36 + dataBytes);
        _writer.Write("WAVE".ToCharArray());
        _writer.Write("fmt ".ToCharArray());
        _writer.Write(16);
        _writer.Write((ushort)1); // PCM
        _writer.Write((ushort)_channels);
        _writer.Write(_sampleRate);
        _writer.Write(byteRate);
        _writer.Write((ushort)blockAlign);
        _writer.Write((ushort)_bitsPerSample);
        _writer.Write("data".ToCharArray());
        _writer.Write(dataBytes);
    }

    /// <summary>Writes a block of interleaved float samples (clamped, converted to PCM).</summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            var s = sample > 1f ? 1f : sample < -1f ? -1f : sample;
            switch (_bitsPerSample)
            {
                case 24:
                    WritePcm24(s);
                    _dataBytes += 3;
                    break;
                case 32:
                    _writer.Write((int)(s * 2147483647f));
                    _dataBytes += 4;
                    break;
                default:
                    _writer.Write((short)(s * 32767f));
                    _dataBytes += 2;
                    break;
            }
        }
    }

    private void WritePcm24(float sample)
    {
        var v = (int)(sample * 8388607f);
        _writer.Write((byte)(v & 0xFF));
        _writer.Write((byte)((v >> 8) & 0xFF));
        _writer.Write((byte)((v >> 16) & 0xFF));
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
