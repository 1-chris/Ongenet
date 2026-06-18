using System;

namespace Ongenet.Core.Audio;

/// <summary>
/// A node in the audio graph that produces samples. The render model is <b>additive</b>:
/// <see cref="Render"/> mixes (adds) its output into the supplied buffer rather than
/// overwriting it, which lets the mixer sum many sources with no temporary buffers and no
/// allocation on the audio thread. The buffer is interleaved at the engine's
/// <see cref="AudioFormat"/> and is pre-cleared by the engine each callback.
/// </summary>
public interface ISampleSource
{
    /// <summary>
    /// Called before rendering (and whenever the format changes) to hand the source the
    /// engine's sample rate and channel count.
    /// </summary>
    void Prepare(AudioFormat format);

    /// <summary>
    /// Adds this source's output into <paramref name="buffer"/> (interleaved samples,
    /// length = frames × channels). Must not allocate.
    /// </summary>
    void Render(Span<float> buffer);
}
