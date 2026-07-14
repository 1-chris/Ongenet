using System;

namespace Ongenet.Core.Audio.Instruments.Drums;

/// <summary>
/// Shared one-shot drum voice plumbing: timeline tracking, deterministic noise seeding and
/// interleaved buffer output helpers used by <see cref="KickaInstrument"/>,
/// <see cref="PercaInstrument"/> and <see cref="DrumModelInstrument"/>.
/// </summary>
public abstract class DrumVoice : Voice
{
    protected const float VoiceGain = 0.9f;
    protected const int MaxTaps = 4;

    // TR-808-style inharmonic ratios for metallic square-bank tone layers.
    protected static readonly double[] MetalRatios = { 1.0, 1.5, 2.08, 2.72, 3.4, 4.1 };

    private static uint _seedCounter = 1;

    protected long Elapsed;
    protected long TotalSamples;
    protected float Velocity;

    /// <summary>One-shot drums ignore NoteOff; the envelope timeline decides when the voice ends.</summary>
    public override void Release() { }

    protected static uint NextSeed(int midiNote) => _seedCounter++ * 2654435761u + (uint)midiNote;

    protected void BeginTimeline(double totalSeconds, int sampleRate)
    {
        Elapsed = 0;
        TotalSamples = (long)((totalSeconds + 0.02) * sampleRate) + 1;
    }

    protected bool AdvanceTimeline()
    {
        if (++Elapsed >= TotalSamples)
        {
            IsActive = false;
            return true;
        }

        return false;
    }

    protected static void WriteMono(Span<float> buffer, int frame, int channels, float sample)
    {
        var bi = frame * channels;
        for (var c = 0; c < channels; c++) buffer[bi + c] += sample;
    }

    protected static void WriteStereo(Span<float> buffer, int frame, int channels, float left, float right)
    {
        var bi = frame * channels;
        buffer[bi] += left;
        if (channels > 1) buffer[bi + 1] += right;
        for (var c = 2; c < channels; c++) buffer[bi + c] += 0.5f * (left + right);
    }
}
