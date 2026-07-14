using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Containers;

/// <summary>
/// Shared recursive render/prepare helpers for container instruments and nested effect chains.
/// </summary>
public static class ContainerRenderer
{
    public static void PrepareInstrument(IInstrument instrument, AudioFormat format)
    {
        instrument.Prepare(format);
        if (instrument is not IContainerInstrument container) return;
        foreach (var slot in container.Children)
        {
            slot.Instrument.Prepare(format);
            foreach (var fx in slot.ActiveEffects) fx.Prepare(format);
        }
    }

    public static void PrepareEffect(IAudioEffect effect, AudioFormat format)
    {
        effect.Prepare(format);
        if (effect is not IContainerEffect container) return;
        foreach (var child in container.Children) PrepareEffect(child, format);
    }

    public static void AllNotesOffInstrument(IInstrument instrument)
    {
        instrument.AllNotesOff();
        if (instrument is not IContainerInstrument container) return;
        foreach (var slot in container.Children) AllNotesOffInstrument(slot.Instrument);
    }

    public static bool HasActiveVoices(IInstrument instrument)
    {
        if (instrument is IContainerInstrument container)
        {
            foreach (var slot in container.Children)
            {
                if (HasActiveVoices(slot.Instrument)) return true;
            }

            return false;
        }

        if (instrument is IInstrumentVoiceState vs) return vs.HasActiveVoices;
        return false;
    }

    /// <summary>
    /// Renders all enabled child slots into <paramref name="output"/> (summed). Optionally routes
    /// notes via <paramref name="router"/> when handling note events externally.
    /// </summary>
    public static void RenderChildren(IContainerInstrument container, Span<float> output,
        Span<float> scratch, EffectContext? effectCtx = null)
    {
        output.Clear();
        foreach (var slot in container.Children)
        {
            if (!slot.Enabled) continue;
            scratch.Clear();
            slot.Instrument.Render(scratch);
            if (!HasSignal(scratch)) continue;
            ProcessSlotEffects(slot, scratch, effectCtx);
            for (var i = 0; i < output.Length; i++) output[i] += scratch[i];
        }
    }

    /// <summary>Renders a single child slot by index into <paramref name="output"/>.</summary>
    public static void RenderChild(IContainerInstrument container, int slotIndex, Span<float> output,
        EffectContext? effectCtx = null)
    {
        output.Clear();
        if (slotIndex < 0 || slotIndex >= container.Children.Count) return;
        var slot = container.Children[slotIndex];
        if (!slot.Enabled) return;
        slot.Instrument.Render(output);
        if (!HasSignal(output)) return;
        ProcessSlotEffects(slot, output, effectCtx);
    }

    public static void ProcessEffectChain(IReadOnlyList<IAudioEffect> chain, Span<float> buffer,
        EffectContext? effectCtx)
    {
        foreach (var fx in chain)
        {
            if (!fx.Enabled) continue;
            if (effectCtx is not null && fx is IContextualEffect ctx) ctx.SetContext(effectCtx);
            fx.Process(buffer);
        }
    }

    public static void ProcessBranch(ContainerEffectBranch branch, Span<float> buffer,
        EffectContext? effectCtx)
        => ProcessEffectChain(branch.Effects, buffer, effectCtx);

    private static void ProcessSlotEffects(InstrumentSlot slot, Span<float> buffer, EffectContext? effectCtx)
        => ProcessEffectChain(slot.ActiveEffects, buffer, effectCtx);

    private static bool HasSignal(ReadOnlySpan<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var a = buffer[i];
            if (a < 0) a = -a;
            if (a > 1e-6f) return true;
        }

        return false;
    }
}
