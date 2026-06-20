using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Core.Models.Audio;

/// <summary>
/// One instrument in an <see cref="TrackKind.Instrument"/> track's instrument rack: the instrument
/// itself, a bypass flag, and its own (pre) insert-effect chain that processes only this instrument's
/// output before the track-level effects. A track can hold several slots; the track's MIDI drives them
/// all simultaneously. POCO by design — the Desktop layer wraps it in a view model.
/// </summary>
public sealed class InstrumentSlot
{
    public InstrumentSlot(IInstrument instrument) => Instrument = instrument;

    /// <summary>The instrument this slot renders.</summary>
    public IInstrument Instrument { get; }

    /// <summary>Whether this instrument sounds. When false the engine skips it (rendering and notes).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>This instrument's own insert effect chain (UI-facing). Edit, then call <see cref="CommitEffects"/>.</summary>
    public List<IAudioEffect> Effects { get; } = new();

    private volatile IAudioEffect[] _activeEffects = Array.Empty<IAudioEffect>();

    /// <summary>Lock-free snapshot of the per-instrument effect chain read by the audio engine.</summary>
    public IAudioEffect[] ActiveEffects => _activeEffects;

    /// <summary>Publishes the current <see cref="Effects"/> list to the audio thread.</summary>
    public void CommitEffects() => _activeEffects = Effects.ToArray();
}
