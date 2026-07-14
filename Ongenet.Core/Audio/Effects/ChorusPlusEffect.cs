using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>Chorus+ with four character architectures.</summary>
public sealed class ChorusPlusEffect : IAudioEffect
{
    public const string TypeId = "chorus_plus";

    string IAudioEffect.TypeId => TypeId;
    public string Name => "Chorus+";
    public bool Enabled { get; set; } = true;

    public double RateHz { get; set; } = 0.5;
    public double Depth { get; set; } = 0.6;
    public double Mix { get; set; } = 0.5;
    public double Spread { get; set; } = 0.7;
    public int Character { get; set; }

    private readonly ChorusEffect _core = new() { Enhanced = true };

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Rate", 0.05, 5, () => RateHz, v => RateHz = v, "0.##", "Hz", 2.0),
        new FloatParameter("Depth", 0, 1, () => Depth, v => Depth = v),
        new FloatParameter("Spread", 0, 1, () => Spread, v => Spread = v),
        new FloatParameter("Mix", 0, 1, () => Mix, v => Mix = v),
        new ChoiceParameter("Character", ModulationCharacterEngine.CharacterNames, () => Character, i => Character = i)
    };

    public void Prepare(AudioFormat format) => _core.Prepare(format);

    public IAudioEffect Clone() => new ChorusPlusEffect
    {
        Enabled = Enabled, RateHz = RateHz, Depth = Depth, Mix = Mix, Spread = Spread, Character = Character
    };

    public void Process(Span<float> buffer)
    {
        if (!Enabled) return;
        SyncCore();
        _core.Process(buffer);
    }

    private void SyncCore()
    {
        _core.Enabled = true;
        var depth = Depth;
        var spread = Spread;
        var fb = 0.0;
        var voices = 3;
        ModulationCharacterEngine.ApplyCharacter((ModulationCharacter)Character, ref depth, ref fb, ref spread, ref voices);
        _core.RateHz = RateHz;
        _core.Depth = depth;
        _core.Spread = spread;
        _core.Mix = Mix;
        _core.Enhanced = true;
    }
}
