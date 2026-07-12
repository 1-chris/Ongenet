using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// An <see cref="IInstrument"/> that can render additional output buses beyond the main mix.
/// </summary>
public interface IMultiOutputInstrument : IInstrument
{
    int OutputBusCount { get; }
    IReadOnlyList<PluginOutputBusDescriptor> OutputBuses { get; }

    /// <summary>
    /// Renders the main bus into <paramref name="buffer"/> (additive) and optionally delivers
    /// auxiliary buses through <paramref name="extraBusCallback"/> (bus index, interleaved audio).
    /// </summary>
    void RenderMulti(Span<float> buffer, Action<int, ReadOnlySpan<float>>? extraBusCallback);
}
