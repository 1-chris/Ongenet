using System;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Offline-renders a short audition clip for an instrument preset (IPreviewRenderer fast path, otherwise
/// a generic note-on/render loop). The host always renders a detached clone — never a live sounding voice.
/// </summary>
public static class InstrumentPresetPreviewRenderer
{
    public const int SampleRate = 44100;
    public const int Channels = 2;

    /// <summary>Renders a mono preview and wraps it as a stereo buffer suitable for <see cref="IAuditionPlayer"/>.</summary>
    public static AudioSampleBuffer? Render(IInstrument instrument, int midiNote = 60)
    {
        var clone = instrument.Clone();
        if (clone is IPreviewRenderer preview)
            return RenderPreviewRenderer(preview);

        return RenderGeneric(clone, midiNote);
    }

    private static AudioSampleBuffer RenderPreviewRenderer(IPreviewRenderer renderer)
    {
        var seconds = renderer.PreviewSeconds <= 0 ? 1.0 : renderer.PreviewSeconds;
        var length = Math.Max(1, (int)(seconds * SampleRate));
        var mono = new float[length];
        renderer.RenderPreview(mono, SampleRate);
        var used = TrimTail(mono);
        return ToStereoBuffer(mono.AsSpan(0, used));
    }

    private static AudioSampleBuffer RenderGeneric(IInstrument instrument, int midiNote)
    {
        var format = new AudioFormat(SampleRate, Channels);
        instrument.Prepare(format);
        instrument.NoteOn(midiNote, 0.9f);

        var maxFrames = (int)(2.5 * SampleRate);
        var interleaved = new float[maxFrames * Channels];
        var block = new float[512];
        var silentBlocks = 0;
        var writtenFrames = 0;

        for (var pass = 0; pass < maxFrames / 256 + 8; pass++)
        {
            Array.Clear(block);
            var span = block.AsSpan(0, Math.Min(256 * Channels, block.Length));
            instrument.Render(span);
            var frames = span.Length / Channels;
            if (writtenFrames + frames > maxFrames) frames = maxFrames - writtenFrames;
            span.Slice(0, frames * Channels).CopyTo(interleaved.AsSpan(writtenFrames * Channels));
            writtenFrames += frames;

            var peak = 0f;
            foreach (var s in span) peak = Math.Max(peak, Math.Abs(s));
            silentBlocks = peak > 1e-5f ? 0 : silentBlocks + 1;
            if (writtenFrames >= SampleRate / 5 && silentBlocks >= 6 &&
                instrument is not IInstrumentVoiceState { HasActiveVoices: true })
                break;
            if (writtenFrames >= maxFrames) break;
        }

        instrument.NoteOff(midiNote);
        for (var tail = 0; tail < 8 && writtenFrames < maxFrames; tail++)
        {
            Array.Clear(block);
            var span = block.AsSpan(0, Math.Min(256 * Channels, block.Length));
            instrument.Render(span);
            var frames = span.Length / Channels;
            if (writtenFrames + frames > maxFrames) frames = maxFrames - writtenFrames;
            span.Slice(0, frames * Channels).CopyTo(interleaved.AsSpan(writtenFrames * Channels));
            writtenFrames += frames;
        }

        var mono = Downmix(interleaved, writtenFrames);
        var used = TrimTail(mono);
        return ToStereoBuffer(mono.AsSpan(0, used));
    }

    private static float[] Downmix(float[] interleaved, int frames)
    {
        var mono = new float[Math.Max(1, frames)];
        for (var i = 0; i < frames; i++)
        {
            var l = interleaved[i * Channels];
            var r = Channels > 1 ? interleaved[i * Channels + 1] : l;
            mono[i] = (l + r) * 0.5f;
        }

        return mono;
    }

    private static int TrimTail(float[] mono)
    {
        var used = mono.Length;
        while (used > 1 && Math.Abs(mono[used - 1]) < 1e-4f) used--;
        return Math.Min(mono.Length, Math.Max(used + SampleRate / 100, 1));
    }

    private static AudioSampleBuffer ToStereoBuffer(ReadOnlySpan<float> mono)
    {
        var stereo = new float[mono.Length * Channels];
        for (var i = 0; i < mono.Length; i++)
        {
            stereo[i * Channels] = mono[i];
            if (Channels > 1) stereo[i * Channels + 1] = mono[i];
        }

        return new AudioSampleBuffer(stereo, Channels, SampleRate);
    }
}
