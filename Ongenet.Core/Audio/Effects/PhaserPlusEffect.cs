using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>Phaser+ with character modes and enhanced sweep range.</summary>
public sealed class PhaserPlusEffect : IAudioEffect
{
    public const string TypeId = "phaser_plus";

    string IAudioEffect.TypeId => TypeId;
    public string Name => "Phaser+";
    public bool Enabled { get; set; } = true;

    public double RateHz { get; set; } = 0.5;
    public double Depth { get; set; } = 0.8;
    public double Feedback { get; set; } = 0.4;
    public double Mix { get; set; } = 0.5;
    public int StagesIndex { get; set; } = 1;
    public int Character { get; set; }

    private readonly PhaserEffect _core = new() { Enhanced = true };

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Rate", 0.05, 5, () => RateHz, v => RateHz = v, "0.##", "Hz", 2.0),
        new FloatParameter("Depth", 0, 1, () => Depth, v => Depth = v),
        new FloatParameter("Feedback", -0.95, 0.95, () => Feedback, v => Feedback = v),
        new FloatParameter("Mix", 0, 1, () => Mix, v => Mix = v),
        new ChoiceParameter("Character", ModulationCharacterEngine.CharacterNames, () => Character, i => Character = i)
    };

    public void Prepare(AudioFormat format) => _core.Prepare(format);

    public IAudioEffect Clone() => new PhaserPlusEffect
    {
        Enabled = Enabled, RateHz = RateHz, Depth = Depth, Feedback = Feedback,
        Mix = Mix, StagesIndex = StagesIndex, Character = Character
    };

    public void Process(Span<float> buffer)
    {
        if (!Enabled) return;
        var depth = Depth;
        var fb = Feedback;
        var spread = 0.5;
        var voices = 1;
        ModulationCharacterEngine.ApplyCharacter((ModulationCharacter)Character, ref depth, ref fb, ref spread, ref voices);
        _core.Enabled = true;
        _core.RateHz = RateHz;
        _core.Depth = depth;
        _core.Feedback = fb;
        _core.Mix = Mix;
        _core.StagesIndex = StagesIndex;
        _core.Enhanced = true;
        _core.Process(buffer);
    }
}
