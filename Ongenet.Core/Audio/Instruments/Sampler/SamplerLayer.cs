using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ongenet.Core.Audio.Instruments.Sampler;

/// <summary>
/// One stacked instrument file inside a multi-layer <see cref="SamplerInstrument"/>
/// (SFZ or SF2 program). Enabled layers are flattened into the playable region list.
/// </summary>
public sealed class SamplerLayer
{
    /// <summary>Suggested swatches for the layer colour picker (opaque ARGB).</summary>
    public static readonly uint[] Palette =
    [
        0xFFCBA6F7, 0xFF89B4FA, 0xFFA6E3A1, 0xFFFAB387,
        0xFFF5C2E7, 0xFF94E2D5, 0xFFF9E2AF, 0xFF89DCEB,
        0xFFB4BEFE, 0xFFEBA0AC, 0xFFF38BA8, 0xFF74C7EC,
        0xFFA6ADC8, 0xFFCDD6F4, 0xFFFEF9C3, 0xFF86EFAC
    ];

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;
    public SamplerFormat Format { get; set; } = SamplerFormat.Sfz;
    public int PresetIndex { get; set; } = -1;
    public IReadOnlyList<SamplerPresetInfo> Presets { get; set; } = Array.Empty<SamplerPresetInfo>();
    public bool Enabled { get; set; } = true;

    /// <summary>UI / zone-map colour (opaque ARGB).</summary>
    public uint ColorArgb { get; set; } = CreateRandomColor();

    /// <summary>Optional key-range clip (-1 = unused). Applied when flattening.</summary>
    public int KeyMaskLo { get; set; } = -1;
    public int KeyMaskHi { get; set; } = -1;

    public IReadOnlyList<SamplerRegion> Regions { get; set; } = Array.Empty<SamplerRegion>();
    public SamplerSampleLibrary? Library { get; set; }

    public static SamplerLayer FromLoad(SamplerLoadResult result, Guid? reuseId = null, uint? reuseColor = null)
    {
        var id = reuseId ?? Guid.NewGuid();
        var color = reuseColor is > 0 ? reuseColor.Value : CreateRandomColor();
        var name = result.DisplayName;
        if (string.IsNullOrWhiteSpace(name))
            name = Path.GetFileNameWithoutExtension(result.Path);
        return new SamplerLayer
        {
            Id = id,
            Name = name,
            SourcePath = result.Path,
            SourceText = result.SourceText,
            Format = result.Format,
            PresetIndex = result.PresetIndex,
            Presets = result.Presets,
            ColorArgb = color,
            Regions = result.Regions.Select(r => r.WithLayer(id, color)).ToArray(),
            Library = result.Library,
        };
    }

    public IEnumerable<SamplerRegion> FlattenedRegions()
    {
        if (!Enabled || Regions.Count == 0) yield break;
        int? lo = KeyMaskLo >= 0 ? KeyMaskLo : null;
        int? hi = KeyMaskHi >= 0 ? KeyMaskHi : null;
        foreach (var r in Regions)
        {
            var tagged = r.WithLayer(Id, ColorArgb, lo, hi);
            if (tagged.LoKey <= tagged.HiKey)
                yield return tagged;
        }
    }

    /// <summary>Random saturated pastel via golden-angle hues.</summary>
    public static uint CreateRandomColor()
    {
        var hue = Random.Shared.NextDouble() * 360.0;
        return HsvToArgb(hue, 0.55 + Random.Shared.NextDouble() * 0.2, 0.82 + Random.Shared.NextDouble() * 0.12);
    }

    public static uint HsvToArgb(double hueDegrees, double saturation, double value)
    {
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);
        var h = (hueDegrees % 360 + 360) % 360 / 60.0;
        var c = value * saturation;
        var x = c * (1 - Math.Abs(h % 2 - 1));
        var m = value - c;
        double r, g, b;
        if (h < 1) { r = c; g = x; b = 0; }
        else if (h < 2) { r = x; g = c; b = 0; }
        else if (h < 3) { r = 0; g = c; b = x; }
        else if (h < 4) { r = 0; g = x; b = c; }
        else if (h < 5) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        var R = (byte)Math.Clamp((int)((r + m) * 255), 0, 255);
        var G = (byte)Math.Clamp((int)((g + m) * 255), 0, 255);
        var B = (byte)Math.Clamp((int)((b + m) * 255), 0, 255);
        return 0xFF000000u | ((uint)R << 16) | ((uint)G << 8) | B;
    }
}
