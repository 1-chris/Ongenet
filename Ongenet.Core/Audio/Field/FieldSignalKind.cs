namespace Ongenet.Core.Audio.Field;

/// <summary>
/// The semantic kind of a Field wire. All kinds are carried on the same underlying representation —
/// a mono block of <see cref="float"/> samples — so any output can be patched into any input
/// (exactly like Field's modular graph). The kind only drives wire colouring and default routing hints.
/// </summary>
public enum FieldSignalKind
{
    /// <summary>An audio-rate signal, conventionally bipolar in roughly [-1, 1].</summary>
    Audio,

    /// <summary>A control / modulation signal (still audio-rate here), uni- or bipolar.</summary>
    Cv,

    /// <summary>A note-derived control signal (pitch/gate/velocity). Processed identically to <see cref="Cv"/>.</summary>
    Note,

    /// <summary>
    /// A resource reference (a loaded soundfont, a wavetable, a sample) rather than a per-sample buffer.
    /// Asset wires carry an object, resolved once at compile time by pushing the producer's asset into the
    /// consumer (see <see cref="IFieldAssetProvider"/>/<see cref="IFieldAssetConsumer"/>); they allocate no
    /// audio buffers and never run on the audio thread.
    /// </summary>
    Asset
}
