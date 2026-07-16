using System;
using System.IO;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Lightweight RIFF/WAVE <c>fmt </c> peek — used to decide native PCM decode vs ffmpeg.
/// FL Studio Edison often writes Ogg Vorbis inside a <c>.wav</c> container (format <c>0x674F</c>);
/// treating those bytes as PCM produces loud static.
/// </summary>
public static class WavFormatProbe
{
    public const ushort FormatPcm = 1;
    public const ushort FormatFloat = 3;
    public const ushort FormatExtensible = 0xFFFE;
    /// <summary>FL Studio / Edison Ogg Vorbis-in-WAV (<c>'Og'</c>).</summary>
    public const ushort FormatOggVorbis = 0x674F;

    /// <summary>True when <see cref="WavParser"/> can decode the format without ffmpeg.</summary>
    public static bool IsNativePcmOrFloat(ushort formatTag) =>
        formatTag is FormatPcm or FormatFloat;

    /// <summary>Reads the WAVE format tag (or extensible sub-format) from a file.</summary>
    public static bool TryGetFormatTag(string path, out ushort formatTag)
    {
        formatTag = 0;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return TryGetFormatTag(stream, out formatTag);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Reads the WAVE format tag from an open stream (restores position).</summary>
    public static bool TryGetFormatTag(Stream stream, out ushort formatTag)
    {
        formatTag = 0;
        var restore = stream.CanSeek ? stream.Position : -1L;
        try
        {
            if (!stream.CanSeek) return false;
            stream.Position = 0;
            using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);

            if (new string(reader.ReadChars(4)) != "RIFF") return false;
            reader.ReadUInt32();
            if (new string(reader.ReadChars(4)) != "WAVE") return false;

            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = new string(reader.ReadChars(4));
                var chunkSize = reader.ReadUInt32();
                var chunkStart = stream.Position;

                if (chunkId == "fmt " && chunkSize >= 16)
                {
                    formatTag = reader.ReadUInt16();
                    if (formatTag == FormatExtensible && chunkSize >= 40)
                    {
                        reader.ReadUInt16(); // channels
                        reader.ReadUInt32(); // sample rate
                        reader.ReadUInt32(); // byte rate
                        reader.ReadUInt16(); // block align
                        reader.ReadUInt16(); // bits
                        reader.ReadUInt16(); // cbSize
                        reader.ReadUInt16(); // valid bits
                        reader.ReadUInt32(); // channel mask
                        formatTag = reader.ReadUInt16(); // sub-format
                    }
                    return true;
                }

                stream.Position = chunkStart + chunkSize + (chunkSize & 1);
            }

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (restore >= 0 && stream.CanSeek)
                stream.Position = restore;
        }
    }
}
