using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>Character modes for enhanced Chorus+/Flanger+/Phaser+ devices.</summary>
public enum ModulationCharacter
{
    Classic = 0,
    Character1 = 1,
    Character2 = 2,
    Character3 = 3
}

/// <summary>
/// Applies character-specific modulation scaling to delay/LFO parameters.
/// Reusable by +modulation audio effects.
/// </summary>
public static class ModulationCharacterEngine
{
    public static void ApplyCharacter(
        ModulationCharacter character,
        ref double depth,
        ref double feedback,
        ref double spread,
        ref int voices)
    {
        switch (character)
        {
            case ModulationCharacter.Character1:
                depth *= 1.15;
                spread *= 1.2;
                voices = Math.Max(voices, 3);
                break;
            case ModulationCharacter.Character2:
                depth *= 0.85;
                feedback *= 1.25;
                spread *= 0.7;
                break;
            case ModulationCharacter.Character3:
                depth *= 1.35;
                feedback *= 0.6;
                voices = Math.Max(voices, 5);
                spread *= 1.4;
                break;
        }
    }

    public static readonly string[] CharacterNames = { "Classic", "CE / DD", "8v / FB", "x2 / Wide" };
}
