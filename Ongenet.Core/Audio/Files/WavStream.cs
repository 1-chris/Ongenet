using System;
using System.IO;
using System.Text;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Writes an <see cref="AudioSampleBuffer"/> to a stream as a 32-bit IEEE-float WAV (lossless — it mirrors the
/// in-memory float PCM exactly). Reading back is done with <see cref="WavParser.Parse"/>, which already supports
/// float32. Used to embed project samples in the .ongen archive without quality loss.
/// </summary>
public static class WavStream
{
    public static void WriteFloat32(Stream stream, AudioSampleBuffer buffer)
    {
        var channels = buffer.Channels;
        var sampleRate = buffer.SampleRate;
        var samples = buffer.Samples;
        var dataBytes = samples.Length * sizeof(float);
        var blockAlign = channels * sizeof(float);

        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataBytes);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));

        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);              // fmt chunk size
        w.Write((short)3);        // WAVE_FORMAT_IEEE_FLOAT
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(sampleRate * blockAlign); // byte rate
        w.Write((short)blockAlign);
        w.Write((short)32);       // bits per sample

        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataBytes);

        var bytes = new byte[dataBytes];
        Buffer.BlockCopy(samples, 0, bytes, 0, dataBytes);
        w.Write(bytes);
    }
}
