using System.Collections.Generic;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Containers;

/// <summary>
/// An audio effect that hosts nested effect chains (parallel branches or band slots), like rack
/// FX Layer, Multiband FX, or Mid-Side Split devices.
/// </summary>
public interface IContainerEffect : IAudioEffect
{
    /// <summary>All nested effects (flattened across branches/bands).</summary>
    IReadOnlyList<IAudioEffect> Children { get; }
}
