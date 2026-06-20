using System;
using System.IO;
using System.Text;

namespace Ongenet.Core.Persistence;

/// <summary>
/// Little-endian binary reader mirroring <see cref="OngenWriter"/>. <see cref="ReadChunk"/> always advances to
/// the chunk's end afterwards, so unknown trailing fields (written by a newer app) are skipped gracefully.
/// The underlying stream must be seekable (the document is read fully into memory first).
/// </summary>
public sealed class OngenReader : IDisposable
{
    private readonly BinaryReader _reader;
    private readonly Stream _stream;

    public OngenReader(Stream stream)
    {
        _stream = stream;
        _reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
    }

    /// <summary>True while there is more data to read (for optional trailing sections in older files).</summary>
    public bool HasMore => _stream.Position < _stream.Length;

    public int ReadInt() => _reader.ReadInt32();
    public long ReadLong() => _reader.ReadInt64();
    public bool ReadBool() => _reader.ReadBoolean();
    public double ReadDouble() => _reader.ReadDouble();
    public float ReadFloat() => _reader.ReadSingle();
    public string ReadString() => _reader.ReadString();
    public Guid ReadGuid() => new(_reader.ReadBytes(16));
    public double? ReadNullableDouble() => _reader.ReadBoolean() ? _reader.ReadDouble() : null;
    public Guid? ReadNullableGuid() => _reader.ReadBoolean() ? new Guid(_reader.ReadBytes(16)) : null;

    /// <summary>
    /// Reads a chunk written by <see cref="OngenWriter.WriteChunk"/>: invokes <paramref name="body"/> with the
    /// chunk's content, then seeks to the chunk's end regardless of how much (or little) the body consumed.
    /// </summary>
    public void ReadChunk(Action<OngenReader> body)
    {
        var len = _reader.ReadInt32();
        var end = _stream.Position + len;
        try { body(this); }
        finally { _stream.Position = end; }
    }

    public void Dispose() => _reader.Dispose();
}
