using System.Collections.Generic;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Containers;

/// <summary>One parallel branch in a container effect (FX Layer branch, multiband slot, etc.).</summary>
public sealed class ContainerEffectBranch
{
    public List<IAudioEffect> Effects { get; } = new();
}
