using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A track insert effect: it processes a block of interleaved audio in place. Stateful (e.g.
/// reverb tails), so a single instance is used per track for the engine's lifetime. The reusable
/// counterpart to <see cref="Instruments.IInstrument"/>.
/// </summary>
public interface IAudioEffect
{
    /// <summary>Display name.</summary>
    string Name { get; }

    /// <summary>Stable registry type id, used to recreate this effect when loading a project.</summary>
    string TypeId { get; }

    /// <summary>
    /// Whether the effect processes audio. When false the engine bypasses it (the signal passes
    /// through untouched) without removing it from the chain.
    /// </summary>
    bool Enabled { get; set; }

    /// <summary>Editable parameters, rendered generically by the effects panel.</summary>
    IReadOnlyList<Parameter> Parameters { get; }

    /// <summary>Called with the engine format before processing (and on format change).</summary>
    void Prepare(AudioFormat format);

    /// <summary>Processes <paramref name="buffer"/> (interleaved) in place.</summary>
    void Process(Span<float> buffer);

    /// <summary>Creates a fresh copy with the same parameters (for track duplication).</summary>
    IAudioEffect Clone();
}
