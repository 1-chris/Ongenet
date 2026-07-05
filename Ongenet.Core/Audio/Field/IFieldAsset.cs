namespace Ongenet.Core.Audio.Field;

/// <summary>
/// Implemented by a node that produces a resource on an <see cref="FieldSignalKind.Asset"/> output port
/// (e.g. a loaded soundfont or wavetable). The compiler reads the asset when it resolves asset connections.
/// </summary>
public interface IFieldAssetProvider
{
    /// <summary>Returns the current asset for the given output port id, or null if none is loaded.</summary>
    object? GetAsset(string portId);
}

/// <summary>
/// Implemented by a node that consumes a resource on an <see cref="FieldSignalKind.Asset"/> input port. The
/// compiler pushes the connected provider's asset (or null when disconnected) into it at compile time — never
/// on the audio thread — so the node can, e.g., apply a loaded SFZ patch or swap its wavetable.
/// </summary>
public interface IFieldAssetConsumer
{
    /// <summary>Receives the asset connected to the given input port id (null when nothing is connected).</summary>
    void SetAsset(string portId, object? asset);
}
