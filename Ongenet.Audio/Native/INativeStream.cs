using System;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native;

/// <summary>
/// A running native audio stream (playback or capture). Disposing it stops and closes the device. The
/// audio thread lives inside the stream; <see cref="Format"/> is the rate/channels actually negotiated.
/// </summary>
internal interface INativeStream : IDisposable
{
    /// <summary>The format the stream actually opened at (rate may differ from what was requested).</summary>
    AudioFormat Format { get; }
}
