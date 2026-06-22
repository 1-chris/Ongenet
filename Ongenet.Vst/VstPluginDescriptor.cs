namespace Ongenet.Vst;

/// <summary>The VST ABI a descriptor belongs to. The two are completely different hosting paths.</summary>
public enum VstFormat
{
    Vst2,
    Vst3,
}

/// <summary>
/// Format-neutral metadata for one VST plugin found on disk, used both for the library listing and to
/// re-create a live instance. <see cref="Uid"/> is the format-specific stable identifier (the VST2
/// 32-bit unique id as hex, or the VST3 class id as a 32-char hex TUID) that, with <see cref="Path"/>,
/// forms the registry/save id.
/// </summary>
public sealed record VstPluginDescriptor(
    VstFormat Format,
    string Path,
    string Uid,
    string Name,
    string Vendor,
    bool IsInstrument,
    bool IsEffect);
