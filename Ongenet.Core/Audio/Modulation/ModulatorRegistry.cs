using System;
using System.Collections.Generic;
using System.Linq;

namespace Ongenet.Core.Audio.Modulation;

/// <summary>Default registry of built-in modulators (43 types).</summary>
public sealed class ModulatorRegistry : IModulatorRegistry
{
    private readonly object _lock = new();
    private const string CatEnvelopes = "Envelopes";
    private const string CatLfos = "LFOs";
    private const string CatPerformance = "Performance";
    private const string CatAnalysis = "Analysis";
    private const string CatPitchKey = "Pitch/Key";
    private const string CatLogicMath = "Logic/Math";

    private readonly List<ModulatorInfo> _builtIn = new()
    {
        // Envelopes
        new(FourStageModulator.ModId, "4-Stage", () => new FourStageModulator(), CatEnvelopes),
        new(AdsrModulator.ModId, "ADSR", () => new AdsrModulator(), CatEnvelopes),
        new(AhdOnReleaseModulator.ModId, "AHD on Release", () => new AhdOnReleaseModulator(), CatEnvelopes),
        new(AhdsrModulator.ModId, "AHDSR", () => new AhdsrModulator(), CatEnvelopes),
        new(CurvesModulator.ModId, "Curves", () => new CurvesModulator(), CatEnvelopes),
        new(EnvelopeFollowerModulator.ModId, "Envelope Follower", () => new EnvelopeFollowerModulator(), CatEnvelopes),
        new(RampModulator.ModId, "Ramp", () => new RampModulator(), CatEnvelopes),
        new(SegmentsModulator.ModId, "Segments", () => new SegmentsModulator(), CatEnvelopes),

        // LFOs
        new(LfoModulator.ModId, "LFO", () => new LfoModulator(), CatLfos),
        new(ClassicLfoModulator.ModId, "Classic LFO", () => new ClassicLfoModulator(), CatLfos),
        new(BeatLfoModulator.ModId, "Beat LFO", () => new BeatLfoModulator(), CatLfos),
        new(WavetableLfoModulator.ModId, "Wavetable LFO", () => new WavetableLfoModulator(), CatLfos),
        new(RandomModulator.ModId, "Random", () => new RandomModulator(), CatLfos),
        new(SampleHoldModulator.ModId, "Sample and Hold", () => new SampleHoldModulator(), CatLfos),
        new(StepsModulator.ModId, "Steps", () => new StepsModulator(), CatLfos),

        // Performance
        new(ButtonModulator.ModId, "Button", () => new ButtonModulator(), CatPerformance),
        new(ButtonsModulator.ModId, "Buttons", () => new ButtonsModulator(), CatPerformance),
        new(MacroModulator.ModId, "Macro", () => new MacroModulator(), CatPerformance),
        new(Macro4Modulator.ModId, "Macro-4", () => new Macro4Modulator(), CatPerformance),
        new(ExpressionsModulator.ModId, "Expressions", () => new ExpressionsModulator(), CatPerformance),
        new(VoiceControlModulator.ModId, "Voice Control", () => new VoiceControlModulator(), CatPerformance),
        new(VibratoModulator.ModId, "Vibrato", () => new VibratoModulator(), CatPerformance),
        new(StackSpreadModulator.ModId, "Stack Spread", () => new StackSpreadModulator(), CatPerformance),
        new(XyModulator.ModId, "XY", () => new XyModulator(), CatPerformance),
        new(GlobalsModulator.ModId, "Globals", () => new GlobalsModulator(), CatPerformance),

        // Analysis
        new(AudioRateModulator.ModId, "Audio Rate", () => new AudioRateModulator(), CatAnalysis),
        new(AudioSidechainModulator.ModId, "Audio Sidechain", () => new AudioSidechainModulator(), CatAnalysis),
        new(NoteSidechainModulator.ModId, "Note Sidechain", () => new NoteSidechainModulator(), CatAnalysis),
        new(NoteCounterModulator.ModId, "Note Counter", () => new NoteCounterModulator(), CatAnalysis),

        // Pitch / key
        new(KeytrackPlusModulator.ModId, "Keytrack+", () => new KeytrackPlusModulator(), CatPitchKey),
        new(Pitch12Modulator.ModId, "Pitch-12", () => new Pitch12Modulator(), CatPitchKey),
        new(RelativeKeytrackModulator.ModId, "Relative Keytrack", () => new RelativeKeytrackModulator(), CatPitchKey),
        new(MidiModulator.ModId, "MIDI", () => new MidiModulator(), CatPitchKey),
        new(HwCvInModulator.ModId, "HW CV In", () => new HwCvInModulator(), CatPitchKey),
        new(Channel16Modulator.ModId, "Channel-16", () => new Channel16Modulator(), CatPitchKey),

        // Logic / math
        new(MathModulator.ModId, "Math", () => new MathModulator(), CatLogicMath),
        new(MixModulator.ModId, "Mix", () => new MixModulator(), CatLogicMath),
        new(PolynomModulator.ModId, "Polynom", () => new PolynomModulator(), CatLogicMath),
        new(QuantizeModulator.ModId, "Quantize", () => new QuantizeModulator(), CatLogicMath),
        new(Select4Modulator.ModId, "Select-4", () => new Select4Modulator(), CatLogicMath),
        new(Vector4Modulator.ModId, "Vector-4", () => new Vector4Modulator(), CatLogicMath),
        new(Vector8Modulator.ModId, "Vector-8", () => new Vector8Modulator(), CatLogicMath),
        new(ParSeq8Modulator.ModId, "ParSeq-8", () => new ParSeq8Modulator(), CatLogicMath),
    };

    private readonly List<ModulatorInfo> _dynamic = new();
    private Func<string, IModulator?>? _fallbackCreate;

    public event Action? Changed;

    public IReadOnlyList<ModulatorInfo> Available
    {
        get { lock (_lock) return _builtIn.Concat(_dynamic).ToList(); }
    }

    public IModulator Create(string id)
    {
        ModulatorInfo? info;
        lock (_lock) info = _builtIn.Concat(_dynamic).FirstOrDefault(e => e.Id == id);
        if (info is not null) return info.Create();

        var fallback = _fallbackCreate?.Invoke(id);
        if (fallback is not null) return fallback;

        throw new ArgumentException($"Unknown modulator type '{id}'.", nameof(id));
    }

    public void Register(ModulatorInfo info)
    {
        lock (_lock)
        {
            if (_builtIn.Any(e => e.Id == info.Id)) return;
            var existing = _dynamic.FindIndex(e => e.Id == info.Id);
            if (existing >= 0) _dynamic[existing] = info;
            else _dynamic.Add(info);
        }
        Changed?.Invoke();
    }

    public bool Unregister(string id)
    {
        lock (_lock)
        {
            if (_dynamic.RemoveAll(e => e.Id == id) <= 0) return false;
        }
        Changed?.Invoke();
        return true;
    }

    public void SetFallbackCreate(Func<string, IModulator?> fallback) => _fallbackCreate = fallback;
}
