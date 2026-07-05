using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// Allocates and frees the fixed pool of <see cref="FieldVoiceState"/> slots for a Field instrument graph,
/// with oldest-voice stealing (mirroring <see cref="PolyphonicInstrument"/>). Voices are freed a short while
/// after release once their audible output falls silent, detected from the per-voice peak measured at the
/// voice-collecting boundary. Effect graphs use a single always-on voice and never touch this.
/// </summary>
public sealed class FieldVoiceManager
{
    // Consecutive near-silent blocks after release before a voice is reclaimed.
    private const int SilenceBlocksToFree = 4;
    private const float SilenceThreshold = 1e-4f;

    private readonly FieldVoiceState[] _voices;
    private uint _counter;

    public FieldVoiceManager(int maxVoices)
    {
        var n = maxVoices < 1 ? 1 : maxVoices;
        _voices = new FieldVoiceState[n];
        for (var i = 0; i < n; i++) _voices[i] = new FieldVoiceState();
    }

    public FieldVoiceState[] Voices => _voices;
    public int MaxVoices => _voices.Length;

    /// <summary>Allocates a voice for a new note (stealing the oldest if the pool is full). Returns its index.</summary>
    public int NoteOn(int note, float velocity, double frequency)
    {
        var index = FindFree();
        if (index < 0) index = FindOldest();

        var v = _voices[index];
        v.Active = true;
        v.Note = note;
        v.Velocity = velocity;
        v.Gate = true;
        v.Frequency = frequency;
        v.StartOrder = _counter++;
        v.SilentBlocks = 0;
        return index;
    }

    /// <summary>Releases every gated voice playing <paramref name="note"/>.</summary>
    public void NoteOff(int note)
    {
        foreach (var v in _voices)
            if (v.Active && v.Gate && v.Note == note) v.Gate = false;
    }

    public void AllNotesOff()
    {
        foreach (var v in _voices)
            if (v.Active) v.Gate = false;
    }

    /// <summary>Immediately silences and frees all voices (e.g. on recompile).</summary>
    public void Reset()
    {
        foreach (var v in _voices) v.Reset();
    }

    /// <summary>Sets the sounding frequency of every active voice from its note plus a pitch-bend offset.</summary>
    public void ApplyPitchBend(double semitones)
    {
        foreach (var v in _voices)
            if (v.Active) v.Frequency = MusicalMath.NoteToFrequency(v.Note) * System.Math.Pow(2.0, semitones / 12.0);
    }

    /// <summary>
    /// Called once per block with the peak audible level observed for each voice. Frees released voices that
    /// have been silent long enough. Gated voices are kept alive.
    /// </summary>
    public void EndBlock(float[] voicePeak)
    {
        for (var i = 0; i < _voices.Length; i++)
        {
            var v = _voices[i];
            if (!v.Active) continue;
            if (v.Gate) { v.SilentBlocks = 0; continue; }
            if (voicePeak[i] < SilenceThreshold)
            {
                if (++v.SilentBlocks >= SilenceBlocksToFree) v.Reset();
            }
            else
            {
                v.SilentBlocks = 0;
            }
        }
    }

    private int FindFree()
    {
        for (var i = 0; i < _voices.Length; i++)
            if (!_voices[i].Active) return i;
        return -1;
    }

    private int FindOldest()
    {
        var oldest = 0;
        for (var i = 1; i < _voices.Length; i++)
            if (_voices[i].StartOrder < _voices[oldest].StartOrder) oldest = i;
        return oldest;
    }
}
