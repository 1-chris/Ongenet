using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>Flanger+ with four character architectures.</summary>
public sealed class FlangerPlusEffect : IAudioEffect
{
    public const string TypeId = "flanger_plus";

    string IAudioEffect.TypeId => TypeId;
    public string Name => "Flanger+";
    public bool Enabled { get; set; } = true;

    public double RateHz { get; set; } = 0.3;
    public double Depth { get; set; } = 0.6;
    public double Feedback { get; set; } = 0.3;
    public double Mix { get; set; } = 0.5;
    public int Character { get; set; }

    private readonly FlangerEffect _core = new();

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

    public IAudioEffect Clone() => new FlangerPlusEffect
    {
        Enabled = Enabled, RateHz = RateHz, Depth = Depth, Feedback = Feedback, Mix = Mix, Character = Character
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
        _core.Process(buffer);
    }
}
