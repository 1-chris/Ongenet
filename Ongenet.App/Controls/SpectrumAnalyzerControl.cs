using Avalonia.Media;

namespace Ongenet.App.Controls;

/// <summary>
/// Live spectrum analyser display — reuses <see cref="SpectrumGraphControl"/> grid/axis/spectrum
/// rendering without additional overlays (used by the Spectrum effect card).
/// </summary>
public sealed class SpectrumAnalyzerControl : SpectrumGraphControl
{
    protected override void RenderOverlay(DrawingContext context, double width, double plotHeight) { }
}
