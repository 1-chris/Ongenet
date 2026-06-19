using System;
using System.IO;
using System.Text;

namespace Ongenet.Core.Persistence;

/// <summary>
/// Little-endian binary writer for the .ongen document format. Supports length-prefixed "chunks" so
/// that readers can append new fields in future format versions and older readers skip past them.
/// </summary>
public sealed class OngenWriter : IDisposable
{
    private readonly BinaryWriter _writer;

    public OngenWriter(Stream stream) => _writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

    public void WriteInt(int v) => _writer.Write(v);
    public void WriteLong(long v) => _writer.Write(v);
    public void WriteBool(bool v) => _writer.Write(v);
    public void WriteDouble(double v) => _writer.Write(v);
    public void WriteFloat(float v) => _writer.Write(v);
    public void WriteString(string? v) => _writer.Write(v ?? string.Empty);
    public void WriteGuid(Guid g) => _writer.Write(g.ToByteArray());

    public void WriteNullableDouble(double? v)
    {
        _writer.Write(v.HasValue);
        if (v.HasValue) _writer.Write(v.Value);
    }

    public void WriteNullableGuid(Guid? v)
    {
        _writer.Write(v.HasValue);
        if (v.HasValue) _writer.Write(v.Value.ToByteArray());
    }

    /// <summary>
    /// Writes a self-describing chunk: a 4-byte byte-length prefix followed by whatever <paramref name="body"/>
    /// emits. Readers that don't understand the chunk (or a newer version's extra fields) skip it by its length.
    /// </summary>
    public void WriteChunk(Action<OngenWriter> body)
    {
        using var ms = new MemoryStream();
        using (var inner = new OngenWriter(ms)) body(inner);
        _writer.Write((int)ms.Length);
        ms.Position = 0;
        ms.CopyTo(_writer.BaseStream);
    }

    public void Dispose() => _writer.Dispose();
}
