namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Version stamp for code-defined factory presets. When this rises, the desktop preset library
/// rewrites <c>Presets/Factory</c> so existing installs pick up new built-in patches.
/// User-saved presets under <c>Presets/Instruments|Effects|Chains</c> are never touched.
/// </summary>
public static class FactoryContentVersion
{
    /// <summary>Bump whenever factory instrument/effect/chain definitions change.</summary>
    public const int Current = 3;
}
