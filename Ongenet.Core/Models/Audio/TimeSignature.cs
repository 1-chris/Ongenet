namespace Ongenet.Core.Models.Audio;

/// <summary>
/// A musical time signature (e.g. 4/4), where <see cref="Numerator"/> beats of
/// note value <see cref="Denominator"/> make up a bar.
/// </summary>
public readonly record struct TimeSignature(int Numerator, int Denominator)
{
    /// <summary>Common time, 4/4.</summary>
    public static TimeSignature FourFour => new(4, 4);
}
