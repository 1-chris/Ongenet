using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Hardware;

namespace Ongenet.Core.Audio.MidiFx;

/// <summary>Default registry of built-in MIDI note effects.</summary>
public sealed class MidiEffectRegistry : IMidiEffectRegistry
{
    private readonly object _lock = new();
    private const string CatPitch = "Pitch";
    private const string CatTiming = "Timing";
    private const string CatPerformance = "Performance";
    private const string CatSelection = "Selection";
    private const string CatAdvanced = "Advanced";
    private const string CatHardware = "Hardware";

    private readonly List<MidiEffectInfo> _builtIn = new()
    {
        new(ScaleMidiEffect.TypeId, "Scale", () => new ScaleMidiEffect(), CatPitch),
        new(QuantizeMidiEffect.TypeId, "Quantize", () => new QuantizeMidiEffect(), CatTiming),
        new(ChordMidiEffect.TypeId, "Chord", () => new ChordMidiEffect(), CatPitch),
        new(HarmonizeMidiEffect.TypeId, "Harmonize", () => new HarmonizeMidiEffect(), CatPitch),
        new(ArpMidiEffect.TypeId, "Arpeggiator", () => new ArpMidiEffect(), CatTiming),
        new(NoteEchoMidiEffect.TypeId, "Echo", () => new NoteEchoMidiEffect(), CatTiming),
        new(RandomMidiEffect.TypeId, "Randomize", () => new RandomMidiEffect(), CatPerformance),
        new(HumanizeMidiEffect.TypeId, "Humanize", () => new HumanizeMidiEffect(), CatPerformance),
        new(NoteTransposeMidiEffect.TypeId, "Note Transpose", () => new NoteTransposeMidiEffect(), CatPitch),
        new(NoteDelayMidiEffect.TypeId, "Note Delay", () => new NoteDelayMidiEffect(), CatTiming),
        new(NoteLengthMidiEffect.TypeId, "Note Length", () => new NoteLengthMidiEffect(), CatTiming),
        new(NoteRepeatsMidiEffect.TypeId, "Note Repeats", () => new NoteRepeatsMidiEffect(), CatTiming),
        new(VelocityCurveMidiEffect.TypeId, "Velocity Curve", () => new VelocityCurveMidiEffect(), CatPerformance),
        new(KeyFilterMidiEffect.TypeId, "Key Filter+", () => new KeyFilterMidiEffect(), CatSelection),
        new(NoteFilterMidiEffect.TypeId, "Note Filter", () => new NoteFilterMidiEffect(), CatSelection),
        new(ChannelFilterMidiEffect.TypeId, "Channel Filter", () => new ChannelFilterMidiEffect(), CatSelection),
        new(ChannelMapMidiEffect.TypeId, "Channel Map", () => new ChannelMapMidiEffect(), CatSelection),
        new(BendMidiEffect.TypeId, "Bend", () => new BendMidiEffect(), CatPitch),
        new(MicroPitchMidiEffect.TypeId, "Micro-pitch", () => new MicroPitchMidiEffect(), CatPitch),
        new(StrumMidiEffect.TypeId, "Strum", () => new StrumMidiEffect(), CatPerformance),
        new(LatchMidiEffect.TypeId, "Latch", () => new LatchMidiEffect(), CatPerformance),
        new(MultiNoteMidiEffect.TypeId, "Multi-note", () => new MultiNoteMidiEffect(), CatAdvanced),
        new(NoteGridMidiEffect.TypeId, "Note Grid", () => new NoteGridMidiEffect(), CatAdvanced),
        new(StepwiseMidiEffect.TypeId, "Stepwise", () => new StepwiseMidiEffect(), CatTiming),
        new(DribbleMidiEffect.TypeId, "Dribble", () => new DribbleMidiEffect(), CatTiming),
        new(RicochetMidiEffect.TypeId, "Ricochet", () => new RicochetMidiEffect(), CatTiming),
        new(TransposeMapMidiEffect.TypeId, "Transpose Map", () => new TransposeMapMidiEffect(), CatPitch),

        // Hardware
        new(MidiCcMidiEffect.TypeId, "MIDI CC", () => new MidiCcMidiEffect(), CatHardware),
        new(MidiProgramChangeMidiEffect.TypeId, "MIDI Program Change", () => new MidiProgramChangeMidiEffect(), CatHardware),
        new(MidiSongSelectMidiEffect.TypeId, "MIDI Song Select", () => new MidiSongSelectMidiEffect(), CatHardware),
    };

    private readonly List<MidiEffectInfo> _dynamic = new();
    private Func<string, IMidiEffect?>? _fallbackCreate;

    public event Action? Changed;

    public IReadOnlyList<MidiEffectInfo> Available
    {
        get { lock (_lock) return _builtIn.Concat(_dynamic).ToList(); }
    }

    public IMidiEffect Create(string id)
    {
        MidiEffectInfo? info;
        lock (_lock) info = _builtIn.Concat(_dynamic).FirstOrDefault(e => e.Id == id);
        if (info is not null) return info.Create();

        var fallback = _fallbackCreate?.Invoke(id);
        if (fallback is not null) return fallback;

        throw new ArgumentException($"Unknown MIDI effect type '{id}'.", nameof(id));
    }

    public void Register(MidiEffectInfo info)
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

    public void SetFallbackCreate(Func<string, IMidiEffect?> fallback) => _fallbackCreate = fallback;
}
