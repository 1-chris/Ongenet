using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Persistence;

/// <summary>
/// Collects unique audio samples for saving, keyed by a content hash (incl. channels + sample rate), so
/// identical buffers are stored once and clips/instruments reference them by hash. Shared by the project
/// (<c>.ongen</c>) and preset (<c>.ongenpreset</c>) writers, both of which emit one <c>samples/{hash}.wav</c>
/// entry per unique buffer.
/// </summary>
public sealed class SampleStore
{
    private readonly Dictionary<string, AudioSampleBuffer> _byHash = new();

    public IEnumerable<KeyValuePair<string, AudioSampleBuffer>> Entries => _byHash;

    public string Add(AudioSampleBuffer buffer)
    {
        var hash = Hash(buffer);
        _byHash.TryAdd(hash, buffer);
        return hash;
    }

    private static string Hash(AudioSampleBuffer b)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> header = stackalloc byte[8];
        BitConverter.TryWriteBytes(header, b.Channels);
        BitConverter.TryWriteBytes(header[4..], b.SampleRate);
        hasher.AppendData(header);
        hasher.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(b.Samples.AsSpan()));
        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }
}
