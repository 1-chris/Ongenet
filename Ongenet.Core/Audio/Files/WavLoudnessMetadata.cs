using System;
using System.IO;
using System.Text;

namespace Ongenet.Core.Audio.Files;

/// <summary>Appends a standard RIFF INFO/ICMT loudness comment to a completed WAV file.</summary>
public static class WavLoudnessMetadata
{
    public static void Append(string path, float integratedLufs, float truePeakDbTp)
    {
        try
        {
            var comment = $"Integrated loudness: {integratedLufs:0.0} LUFS; True peak: {truePeakDbTp:0.0} dBTP";
            var text = Encoding.UTF8.GetBytes(comment + "\0");
            var paddedLength = text.Length + (text.Length & 1);
            var listSize = 4 + 8 + paddedLength;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            if (stream.Length < 12 || stream.Length + 8L + listSize > uint.MaxValue + 8L)
                return;

            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            stream.Seek(0, SeekOrigin.End);
            writer.Write(Encoding.ASCII.GetBytes("LIST"));
            writer.Write(listSize);
            writer.Write(Encoding.ASCII.GetBytes("INFO"));
            writer.Write(Encoding.ASCII.GetBytes("ICMT"));
            writer.Write(text.Length);
            writer.Write(text);
            if ((text.Length & 1) != 0) writer.Write((byte)0);
            writer.Flush();

            stream.Seek(4, SeekOrigin.Begin);
            writer.Write((uint)(stream.Length - 8));
        }
        catch (IOException)
        {
            // Metadata is best-effort; never invalidate an otherwise completed deliverable.
        }
        catch (UnauthorizedAccessException)
        {
            // The WAV remains usable when its destination cannot be reopened for patching.
        }
    }
}
