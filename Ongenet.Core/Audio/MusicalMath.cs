using System;

namespace Ongenet.Core.Audio;

/// <summary>
/// Small helpers for music/DSP maths.
/// </summary>
public static class MusicalMath
{
    private const double A4Frequency = 440.0;
    private const int A4MidiNote = 69;

    /// <summary>Converts a MIDI note number (A4 = 69) to its frequency in Hz, equal temperament.</summary>
    public static double NoteToFrequency(int midiNote)
        => A4Frequency * Math.Pow(2.0, (midiNote - A4MidiNote) / 12.0);

    /// <summary>Converts a (possibly fractional) MIDI note to its frequency in Hz, equal temperament.</summary>
    public static double NoteToFrequency(double midiNote)
        => A4Frequency * Math.Pow(2.0, (midiNote - A4MidiNote) / 12.0);

    /// <summary>Playback-rate ratio for a pitch shift of <paramref name="semitones"/> (12 = one octave up).</summary>
    public static double SemitonesToRatio(double semitones) => Math.Pow(2.0, semitones / 12.0);

    /// <summary>Playback-rate ratio for a pitch shift of <paramref name="cents"/> (100 cents = 1 semitone).</summary>
    public static double CentsToRatio(double cents) => Math.Pow(2.0, cents / 1200.0);
}
