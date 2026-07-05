using System;
using System.Collections.Generic;
using Ongenet.Au.Interop;

namespace Ongenet.Au;

/// <summary>
/// Discovers installed Audio Units by walking the macOS Component Manager with
/// <see cref="AudioUnitApi.AudioComponentFindNext"/> — there is no filesystem/bundle scanning, the OS
/// enumerates every registered component (system + user <c>~/Library/Audio/Plug-Ins/Components</c>) for
/// us. Metadata only (name + type/subtype/manufacturer); nothing is instantiated. Safe off the UI thread.
/// </summary>
public sealed class AuPluginScanner
{
    private readonly Action<string>? _log;

    public AuPluginScanner(Action<string>? log = null) => _log = log;

    /// <summary>Enumerates every hostable Audio Unit (music devices + effects), skipping others.</summary>
    public IReadOnlyList<AuPluginDescriptor> Scan()
    {
        var results = new List<AuPluginDescriptor>();
        if (!OperatingSystem.IsMacOS()) return results;

        try
        {
            // A zeroed description is a full wildcard: iterate every registered component.
            var any = new AudioUnitApi.AudioComponentDescription();
            var comp = IntPtr.Zero;
            while ((comp = AudioUnitApi.AudioComponentFindNext(comp, ref any)) != IntPtr.Zero)
            {
                if (AudioUnitApi.AudioComponentGetDescription(comp, out var d) != 0) continue;

                var isInstrument = d.componentType == AudioUnitApi.kAudioUnitType_MusicDevice
                                   || d.componentType == AudioUnitApi.kAudioUnitType_MusicEffect;
                var isEffect = d.componentType == AudioUnitApi.kAudioUnitType_Effect
                               || d.componentType == AudioUnitApi.kAudioUnitType_MusicEffect;
                if (!isInstrument && !isEffect) continue;

                var name = ReadName(comp) ?? FourCcName(d);
                results.Add(new AuPluginDescriptor(d.componentType, d.componentSubType, d.componentManufacturer,
                    name, isInstrument, isEffect));
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke($"AU scan failed: {ex.Message}");
        }

        return results;
    }

    private static string? ReadName(IntPtr comp)
    {
        if (AudioUnitApi.AudioComponentCopyName(comp, out var cf) != 0 || cf == IntPtr.Zero) return null;
        try { return AudioUnitApi.CFStringToManaged(cf); }
        finally { AudioUnitApi.CFRelease(cf); }
    }

    private static string FourCcName(AudioUnitApi.AudioComponentDescription d) =>
        $"{FromFourCC(d.componentManufacturer)} {FromFourCC(d.componentSubType)}".Trim();

    private static string FromFourCC(uint code)
    {
        Span<char> chars = stackalloc char[4];
        chars[0] = (char)((code >> 24) & 0xFF);
        chars[1] = (char)((code >> 16) & 0xFF);
        chars[2] = (char)((code >> 8) & 0xFF);
        chars[3] = (char)(code & 0xFF);
        return new string(chars).Trim();
    }
}
