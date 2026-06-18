using System;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Describes an available effect type: a stable id, display name, factory, and a category used to
/// group it in the "Add effect" menu (Dynamics / EQ &amp; Filter / Modulation / etc.).
/// </summary>
public sealed record EffectInfo(string Id, string DisplayName, Func<IAudioEffect> Create, string Category = "Other");
