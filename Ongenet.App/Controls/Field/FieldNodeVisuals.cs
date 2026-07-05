using System;
using Ongenet.App.Controls.Engine3D;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field;
using Ongenet.Engine3D.Abstractions;

namespace Ongenet.App.Controls.Field;

/// <summary>
/// Maps a Field node to the GPU 3D visualization shown on its body (when it has one). Scope nodes get the
/// waveform-trail oscilloscope; wavetable nodes get the morphing-table view. Returns null for nodes without
/// a visual. Each call returns a fresh factory so every hosted view gets its own visualization instance.
/// </summary>
public static class FieldNodeVisuals
{
    public static Func<IEngine3DVisualization>? CreateFactory(FieldNode node)
    {
        switch (node)
        {
            case IWaveformSource waveform:
                return () => new WaveformTrailVisualization(waveform);
            case IWavetableView table:
                return () => new Wavetable3DVisualization(table);
            default:
                return null;
        }
    }

    public static bool HasVisual(FieldNode node) => node.HasVisual && CreateFactory(node) is not null;
}
