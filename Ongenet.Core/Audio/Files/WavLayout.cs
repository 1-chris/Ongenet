using System;
using System.IO;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// The on-disk layout of a WAV file (data offset + sample format) read from the header without decoding
/// the audio. Lets a streaming reader pull and convert PCM frames directly from the original file, and a
/// loader decide a sample's tier (resident vs streamed) and grab just its attack — avoiding a full decode
/// and an intermediate float32 copy for large samples.
/// </summary>
public sealed class WavLayout
{
    private const ushort FormatPcm = 1;
    private const ushort FormatFloat = 3;
    private const ushort FormatExtensible = 0xFFFE;

    public long DataOffset { get; init; }   // byte offset of the first sample
    public long DataSize { get; init; }      // bytes of sample data
    public int Channels { get; init; }
    public int SampleRate { get; init; }
    public int BitsPerSample { get; init; }
    public bool IsFloat { get; init; }

    public int BytesPerSample => BitsPerSample / 8;
    public int FrameSize => BytesPerSample * Channels;
    public long FrameCount => FrameSize > 0 ? DataSize / FrameSize : 0;

    /// <summary>Reads a WAV header, or returns null if the file isn't a WAV we can stream.</summary>
    public static WavLayout? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            if (new string(reader.ReadChars(4)) != "RIFF") return null;
            reader.ReadUInt32();
            if (new string(reader.ReadChars(4)) != "WAVE") return null;

            ushort format = FormatPcm, channels = 2, bits = 16;
            uint sampleRate = 44100;
            var haveFormat = false;

            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = new string(reader.ReadChars(4));
                var chunkSize = reader.ReadUInt32();
                var chunkStart = stream.Position;

                if (chunkId == "fmt ")
                {
                    format = reader.ReadUInt16();
                    channels = reader.ReadUInt16();
                    sampleRate = reader.ReadUInt32();
                    reader.ReadUInt32();
                    reader.ReadUInt16();
                    bits = reader.ReadUInt16();
                    if (format == FormatExtensible && chunkSize >= 26)
                    {
                        reader.ReadUInt16();
                        reader.ReadUInt16();
                        reader.ReadUInt32();
                        format = reader.ReadUInt16();
                    }

                    haveFormat = true;
                    stream.Position = chunkStart + chunkSize;
                }
                else if (chunkId == "data")
                {
                    if (!haveFormat) return null;
                    var dataSize = (long)chunkSize;
                    if (dataSize <= 0 || chunkStart + dataSize > stream.Length) dataSize = stream.Length - chunkStart;
                    if (format != FormatPcm && format != FormatFloat) return null; // unsupported encoding
                    return new WavLayout
                    {
                        DataOffset = chunkStart,
                        DataSize = dataSize,
                        Channels = channels,
                        SampleRate = (int)sampleRate,
                        BitsPerSample = bits,
                        IsFloat = format == FormatFloat
                    };
                }
                else
                {
                    stream.Position = chunkStart + chunkSize + (chunkSize & 1);
                }
            }

            return null;
        }
        catch { return null; }
    }

    /// <summary>Reads and converts <paramref name="frameCount"/> frames from <paramref name="startFrame"/> to interleaved float.</summary>
    public float[] ReadFrames(string path, long startFrame, long frameCount)
    {
        if (frameCount <= 0) return Array.Empty<float>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Seek(DataOffset + startFrame * FrameSize, SeekOrigin.Begin);

        var bytes = new byte[frameCount * FrameSize];
        var got = 0;
        while (got < bytes.Length)
        {
            var n = stream.Read(bytes, got, bytes.Length - got);
            if (n == 0) break;
            got += n;
        }

        var framesGot = got / FrameSize;
        var dst = new float[framesGot * Channels];
        Convert(bytes, 0, framesGot, dst, 0);
        return dst;
    }

    /// <summary>Converts <paramref name="frames"/> interleaved frames from a byte buffer into floats.</summary>
    public void Convert(byte[] src, int srcByteOffset, int frames, float[] dst, int dstIndex)
    {
        var bps = BytesPerSample;
        for (var f = 0; f < frames; f++)
        {
            var frameOffset = srcByteOffset + f * FrameSize;
            for (var c = 0; c < Channels; c++)
                dst[dstIndex++] = ToFloat(src, frameOffset + c * bps, BitsPerSample, IsFloat);
        }
    }

    /// <summary>Converts one little-endian sample to float [-1, 1].</summary>
    public static float ToFloat(byte[] buffer, int offset, int bits, bool isFloat)
    {
        if (isFloat)
            return bits == 64 ? (float)BitConverter.ToDouble(buffer, offset) : BitConverter.ToSingle(buffer, offset);

        switch (bits)
        {
            case 8: return (buffer[offset] - 128) / 128f;
            case 16: return BitConverter.ToInt16(buffer, offset) / 32768f;
            case 24:
                var v = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
                if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
                return v / 8388608f;
            case 32: return BitConverter.ToInt32(buffer, offset) / 2147483648f;
            default: return 0f;
        }
    }
}
