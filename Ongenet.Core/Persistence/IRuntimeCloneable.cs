namespace Ongenet.Core.Persistence;

/// <summary>
/// Implemented by a component whose snapshot state includes heavy, already-in-memory data that must NOT
/// be rebuilt from scratch when cloning for the undo/redo history (e.g. the SFZ sampler's decoded sample
/// library, which its <see cref="IProjectStatefulComponent"/> path would otherwise re-read from disk).
/// When present, <see cref="ProjectCloner"/> calls <see cref="CopyRuntimeStateFrom"/> to share that data
/// by reference instead of doing a serialize/deserialize round-trip.
/// </summary>
public interface IRuntimeCloneable
{
    /// <summary>Copies in-memory runtime state from <paramref name="source"/> (a component of the same type).</summary>
    void CopyRuntimeStateFrom(object source);
}
