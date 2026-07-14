using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Containers;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects.Spectral;

/// <summary>
/// Two-branch spectral splitter: decomposes the input into complementary paths, processes each branch,
/// then sums back to the main output (same routing pattern as mid/side and multiband containers).
/// </summary>
public abstract class SpectralSplitEffectBase : ContainerEffectBase, IAudioRouter, IContextualEffect
{
    protected EffectContext? Ctx;
    protected AudioFormat Format = AudioFormat.Default;
    protected float[] BandBuffer = Array.Empty<float>();

    protected SpectralSplitEffectBase()
    {
        EnsureBranchCount(2);
        Branches[0].Effects.Add(new UtilityEffect());
        Branches[1].Effects.Add(new UtilityEffect());
    }

    public int BranchCount => 2;

    public void SetContext(EffectContext context) => Ctx = context;

    public override void Prepare(AudioFormat format)
    {
        Format = format;
        base.Prepare(format);
        var channels = format.Channels < 1 ? 1 : format.Channels;
        var len = Math.Max(8192, channels * 4096);
        if (BandBuffer.Length < len) BandBuffer = new float[len];
        OnPrepare(format);
    }

    protected virtual void OnPrepare(AudioFormat format) { }

    /// <summary>
    /// Splits <paramref name="buffer"/> into two band buffers, processes branches, and sums into
    /// <paramref name="buffer"/>.
    /// </summary>
    protected void ProcessDualBand(Span<float> buffer, Action<int, int, float, Span<float>, Span<float>> decompose)
    {
        if (buffer.Length == 0) return;
        var channels = Format.Channels < 1 ? 1 : Format.Channels;
        var frames = buffer.Length / channels;
        EnsureScratch(Format);
        var low = Scratch.AsSpan(0, buffer.Length);
        var high = BandBuffer.AsSpan(0, buffer.Length);
        low.Clear();
        high.Clear();

        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
                decompose(c, i + c, buffer[i + c], low, high);
        }

        ContainerRenderer.ProcessBranch(Branches[0], low, Ctx);
        ContainerRenderer.ProcessBranch(Branches[1], high, Ctx);

        buffer.Clear();
        for (var j = 0; j < buffer.Length; j++)
            buffer[j] = low[j] + high[j];
    }
}
