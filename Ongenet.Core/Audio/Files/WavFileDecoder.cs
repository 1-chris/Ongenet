using System;
using System.Collections.Generic;
using System.IO;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Dependency-free RIFF/WAVE decoder. Handles PCM 8/16/24/32-bit and IEEE float 32/64-bit
/// (including WAVE_FORMAT_EXTENSIBLE), decoding to an interleaved float <see cref="AudioSampleBuffer"/>.
/// </summary>
public sealed class WavFileDecoder : IAudioFileDecoder
{
    private const ushort FormatPcm = 1;
    private const ushort FormatFloat = 3;
    private const ushort FormatExtensible = 0xFFFE;

    public bool CanDecode(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".wave", StringComparison.OrdinalIgnoreCase);
    }

    public AudioSampleBuffer Decode(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);

        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Not a RIFF file.");
        reader.ReadUInt32(); // overall size
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Not a WAVE file.");

        ushort format = FormatPcm;
        ushort channels = 2;
        uint sampleRate = 44100;
        ushort bitsPerSample = 16;
        var haveFormat = false;

        // Walk chunks until we hit "data".
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
                reader.ReadUInt32(); // byte rate
                reader.ReadUInt16(); // block align
                bitsPerSample = reader.ReadUInt16();

                if (format == FormatExtensible && chunkSize >= 26)
                {
                    reader.ReadUInt16();             // cbSize
                    reader.ReadUInt16();             // valid bits per sample
                    reader.ReadUInt32();             // channel mask
                    format = reader.ReadUInt16();    // sub-format code (PCM or float)
                }

                haveFormat = true;
                stream.Position = chunkStart + chunkSize; // skip any extension bytes
            }
            else if (chunkId == "data")
            {
                if (!haveFormat) throw new InvalidDataException("WAV 'data' chunk before 'fmt '.");
                var dataSize = (long)chunkSize;
                if (dataSize <= 0 || chunkStart + dataSize > stream.Length)
                {
                    dataSize = stream.Length - chunkStart; // tolerate bad/streamed sizes
                }

                return ReadSamples(stream, format, channels, sampleRate, bitsPerSample, dataSize);
            }
            else
            {
                stream.Position = chunkStart + chunkSize + (chunkSize & 1); // chunks are word-aligned
            }
        }

        throw new InvalidDataException("WAV 'data' chunk not found.");
    }

    private static AudioSampleBuffer ReadSamples(
        Stream stream, ushort format, ushort channels, uint sampleRate, ushort bitsPerSample, long dataSize)
    {
        var bytesPerSample = bitsPerSample / 8;
        var frameSize = bytesPerSample * channels;
        if (frameSize <= 0) throw new InvalidDataException("Invalid WAV frame size.");

        var totalFrames = dataSize / frameSize;
        var samples = new float[totalFrames * channels];

        // Read in block-aligned chunks for speed.
        var bufferFrames = Math.Max(1, 65536 / frameSize);
        var buffer = new byte[bufferFrames * frameSize];
        long framesRead = 0;
        var writeIndex = 0;

        while (framesRead < totalFrames)
        {
            var want = (int)Math.Min(bufferFrames, totalFrames - framesRead) * frameSize;
            var got = ReadFull(stream, buffer, want);
            if (got < frameSize) break;

            var framesInBuffer = got / frameSize;
            for (var f = 0; f < framesInBuffer; f++)
            {
                var offset = f * frameSize;
                for (var c = 0; c < channels; c++)
                {
                    samples[writeIndex++] = ReadSample(buffer, offset + c * bytesPerSample, format, bitsPerSample);
                }
            }

            framesRead += framesInBuffer;
        }

        return new AudioSampleBuffer(samples, channels, (int)sampleRate);
    }

    private static float ReadSample(byte[] buffer, int offset, ushort format, ushort bitsPerSample)
    {
        if (format == FormatFloat)
        {
            return bitsPerSample == 64
                ? (float)BitConverter.ToDouble(buffer, offset)
                : BitConverter.ToSingle(buffer, offset);
        }

        // PCM integer (little-endian, signed except 8-bit which is unsigned).
        switch (bitsPerSample)
        {
            case 8:
                return (buffer[offset] - 128) / 128f;
            case 16:
                return BitConverter.ToInt16(buffer, offset) / 32768f;
            case 24:
                var v24 = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
                if ((v24 & 0x800000) != 0) v24 |= unchecked((int)0xFF000000); // sign-extend
                return v24 / 8388608f;
            case 32:
                return BitConverter.ToInt32(buffer, offset) / 2147483648f;
            default:
                return 0f;
        }
    }

    private static int ReadFull(Stream stream, byte[] buffer, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = stream.Read(buffer, total, count - total);
            if (read == 0) break;
            total += read;
        }

        return total;
    }
}
