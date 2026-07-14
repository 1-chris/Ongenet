namespace Ongenet.Core.Audio.Containers;

/// <summary>
/// Describes how a container splits or merges audio across parallel processing branches.
/// </summary>
public interface IAudioRouter
{
    /// <summary>Number of parallel audio branches (layers, bands, L/R, mid/side, etc.).</summary>
    int BranchCount { get; }
}
