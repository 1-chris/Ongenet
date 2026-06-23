# Creating new effects

This guide walks you through building a new audio **effect** for Ongenet — something that takes incoming
audio and changes it, like a delay, filter, distortion or compressor. It is written for people
comfortable with C# but **new to audio programming (DSP)**, and it explains the audio ideas as it goes.

If you haven't yet, skim [creating-instruments.md](creating-instruments.md) first — it introduces the
core concepts (samples, frames, blocks, the real-time audio thread) that this guide builds on. The big
difference: an **instrument produces** sound from notes, while an **effect transforms** sound that is
already there.

Everything here lives in [`Ongenet.Core`](../Ongenet.Core) (`Audio/Effects`, `Audio/Dsp`,
`Audio/Parameters`) — plain `net10.0`, no UI, no native code.

> **The shortest path:** copy a small existing effect
> ([`DelayEffect.cs`](../Ongenet.Core/Audio/Effects/DelayEffect.cs) or
> [`UtilityEffect.cs`](../Ongenet.Core/Audio/Effects/UtilityEffect.cs)), rename it, change the DSP in
> `Process`, and add one line to the registry.

---

## 1. The mental model for effects

**An effect edits the buffer in place.** Where an instrument *adds* its sound into the buffer, an effect
is handed a buffer that already contains audio and **rewrites it**. Read each sample, compute a new
value, write it back to the same slot. There is no separate input and output buffer.

**The format is the same as everywhere else.** 32-bit float samples, interleaved by channel
(`[L, R, L, R, …]`), at the engine's sample rate. For a buffer of length `N` and `channels` channels,
`frames = N / channels`, and channel `c` of frame `f` is at `buffer[f * channels + c]`.

**Effects are stateful and long-lived.** One instance lives on a track for the whole session. That
matters because many effects *remember* the past — a reverb tail keeps ringing after the input stops, a
delay echoes what came before. You keep that memory (delay lines, filter state) in fields.

**The audio thread is sacred — same rules as instruments.** Don't allocate, lock, or do slow work in
`Process`. Allocate buffers up front (in `Prepare`).

**Where effects sit in the signal chain.** Effects run as *inserts*: the audio flows through them in
order. There are actually two insert points — a **pre** chain on each instrument slot, and a **post**
chain on the track (and on group/master buses). The signal path is covered in
[audio-engine.md](audio-engine.md); for writing an effect you just need to know you get a buffer and
must transform it.

---

## 2. The contract: `IAudioEffect`

Every effect implements this one interface:

```12:37:Ongenet.Core/Audio/Effects/IAudioEffect.cs
public interface IAudioEffect
{
    /// <summary>Display name.</summary>
    string Name { get; }

    /// <summary>Stable registry type id, used to recreate this effect when loading a project.</summary>
    string TypeId { get; }

    /// <summary>
    /// Whether the effect processes audio. When false the engine bypasses it (the signal passes
    /// through untouched) without removing it from the chain.
    /// </summary>
    bool Enabled { get; set; }

    /// <summary>Editable parameters, rendered generically by the effects panel.</summary>
    IReadOnlyList<Parameter> Parameters { get; }

    /// <summary>Called with the engine format before processing (and on format change).</summary>
    void Prepare(AudioFormat format);

    /// <summary>Processes <paramref name="buffer"/> (interleaved) in place.</summary>
    void Process(Span<float> buffer);

    /// <summary>Creates a fresh copy with the same parameters (for track duplication).</summary>
    IAudioEffect Clone();
}
```

What each member is for:

- **`Name`** — what shows in the UI (e.g. `"Delay"`).
- **`TypeId`** — a short, *stable* string id (e.g. `"delay"`). Saved into project files; never change
  it once shipped. (Tip: declare `public const string TypeId = "delay";` and then implement the
  interface member explicitly: `string IAudioEffect.TypeId => TypeId;`.)
- **`Enabled`** — when `false`, the engine skips `Process` entirely (true bypass). You don't have to
  check it yourself.
- **`Parameters`** — your knobs (see §5).
- **`Prepare(format)`** — called before processing and whenever the device format changes. This is
  where you learn the sample rate/channel count and allocate any state buffers. There is **no separate
  `Reset()`** — re-initialise your DSP state here.
- **`Process(buffer)`** — the heart of the effect: transform the interleaved buffer in place.
- **`Clone()`** — return a new instance carrying the same parameter values (used when a track is
  duplicated).

---

## 3. Worked example: a gain/utility effect (the simplest case)

[`UtilityEffect`](../Ongenet.Core/Audio/Effects/UtilityEffect.cs) just applies gain, pan, mono
fold-down and phase invert. It is *stateless* — each output sample depends only on the matching input
sample — so it's the cleanest place to see the shape of an effect:

```csharp
public sealed class UtilityEffect : IAudioEffect
{
    public const string TypeId = "utility";
    string IAudioEffect.TypeId => TypeId;
    public string Name => "Utility";
    public bool Enabled { get; set; } = true;

    public double GainDb { get; set; }       // backing fields = the single source of truth
    public double Pan { get; set; }
    public bool Mono { get; set; }
    public bool InvertPhase { get; set; }

    private int _channels = 2;
    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Gain", -24.0, 24.0, () => GainDb, v => GainDb = v, "0.#", "dB"),
        new FloatParameter("Pan", -1.0, 1.0, () => Pan, v => Pan = v, "0.##"),
        new BoolParameter("Mono", () => Mono, v => Mono = v),
        new BoolParameter("Invert Phase", () => InvertPhase, v => InvertPhase = v)
    };

    public void Prepare(AudioFormat format) =>
        _channels = format.Channels < 1 ? 1 : format.Channels;

    public void Process(Span<float> buffer)
    {
        var gain = (float)AudioMath.Db2Lin(GainDb);   // dB → linear multiplier
        var frames = buffer.Length / _channels;
        for (var f = 0; f < frames; f++)
            for (var c = 0; c < _channels; c++)
                buffer[f * _channels + c] *= gain;     // edit in place
    }

    public IAudioEffect Clone() => new UtilityEffect
    {
        Enabled = Enabled, GainDb = GainDb, Pan = Pan, Mono = Mono, InvertPhase = InvertPhase
    };
}
```

Notice the pattern that repeats in every effect:

1. **Backing properties** hold the state (`GainDb`, `Pan`, …). They are the single source of truth.
2. **`Parameters`** wraps those properties with get/set delegates and is built lazily (`??=`).
3. **`Prepare`** only caches what it needs (here, the channel count).
4. **`Process`** loops frames × channels and rewrites the buffer; it reads parameters straight from the
   properties — no allocation, no locks.

---

## 4. Worked example: a delay (an effect with memory)

A **delay** (echo) needs to remember past audio so it can play it back later. The tool for that is a
`DelayLine` — a ring buffer. Here is the complete
[`DelayEffect`](../Ongenet.Core/Audio/Effects/DelayEffect.cs):

```42:76:Ongenet.Core/Audio/Effects/DelayEffect.cs
    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _size = (int)(MaxDelaySeconds * _sampleRate) + 4;
        _lines = new DelayLine[_channels];
        for (var c = 0; c < _channels; c++) { _lines[c] = new DelayLine(); _lines[c].Resize(_size); }
    }

    public IAudioEffect Clone() => new DelayEffect
    {
        Enabled = Enabled, TimeMs = TimeMs, Feedback = Feedback, Mix = Mix
    };

    public void Process(Span<float> buffer)
    {
        if (_lines.Length == 0) return;
        var channels = _channels;
        var delay = Math.Clamp((int)(TimeMs / 1000.0 * _sampleRate), 1, _size - 1);
        var fb = (float)Math.Clamp(Feedback, 0, 0.95);
        var mix = (float)Math.Clamp(Mix, 0, 1);

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var dry = buffer[i + c];
                var delayed = _lines[c].ReadInt(delay);
                buffer[i + c] = dry * (1 - mix) + delayed * mix;
                _lines[c].Write(dry + delayed * fb);
            }
        }
    }
```

The important lessons:

- **Allocate state in `Prepare`, not `Process`.** The delay lines are sized once for the maximum delay
  time. Because `Prepare` is also called on format change, the buffers are always correct for the
  current sample rate. (This doubles as "reset" — there is no separate reset method.)
- **Read parameters once per block** into locals (`delay`, `fb`, `mix`) and clamp them defensively.
- **The classic delay recipe**, per sample: read the delayed sample, mix dry + wet to the output, then
  write `dry + delayed * feedback` back into the line so echoes repeat and decay.
- **"dry" vs "wet"** is universal effect vocabulary: dry = the untouched input, wet = the processed
  signal. A `Mix` knob blends between them.

---

## 5. Parameters

Parameters are identical to the instrument system — see
[creating-instruments.md §5](creating-instruments.md). In short, they are thin wrappers around your own
fields, so the field stays the single source of truth and the audio thread reads it directly:

| Type | Use for | Constructor sketch |
| --- | --- | --- |
| `FloatParameter` | a continuous knob/slider | `new FloatParameter("Time", 1, 2000, () => TimeMs, v => TimeMs = v, "0", "ms", skew: 2.0)` |
| `BoolParameter` | an on/off switch | `new BoolParameter("Mono", () => Mono, v => Mono = v)` |
| `ChoiceParameter` | a dropdown | `new ChoiceParameter("Mode", names, () => idx, i => idx = i)` |

`skew` bends a knob's response (use `> 1` for time/frequency so small values are easier to dial);
`unit` is a display suffix (`"dB"`, `"ms"`, `"Hz"`, `"%"`); `format` is a .NET number format.
**Automation and MIDI-learn come for free** the moment you expose a parameter — the engine writes the
backing field before each block; you write no extra code.

Build the list lazily and read the backing property directly in `Process`:

```csharp
private IReadOnlyList<Parameter>? _parameters;
public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
{
    new FloatParameter("Amount", 0.0, 1.0, () => Amount, v => Amount = v)
};
```

---

## 6. The DSP building blocks

Before writing your own filter or saturator, reach for [`Audio/Dsp`](../Ongenet.Core/Audio/Dsp). These
are the shared, audio-thread-safe primitives effects are built from:

| Building block | What it does | Key methods |
| --- | --- | --- |
| `DelayLine` | Ring buffer — remembers the last N samples (echo, chorus, flanger, comb). | `Resize`, `Clear`, `ReadInt`, `ReadFrac`, `Write` |
| `Biquad` + `BiquadCoefficients` | The workhorse filter (low/high/band-pass, peaking EQ, shelves). | `BiquadCoefficients.Compute(mode, freq, q, sr)`; `Biquad.Process(in coeffs, x)` |
| `OnePole` | Cheap one-pole filter; also ideal for **smoothing** a parameter to avoid zipper noise. | `SetLowpass`, `SetSmoothTime`, `ProcessLP/HP`, `Reset` |
| `EnvelopeFollower` | Tracks the loudness of a signal over time (the brain of compressors/gates). | `SetTimes(attackMs, releaseMs, sr)`, `Process(rectified)`, `Value` |
| `Lfo` | A slow oscillator to modulate things (chorus/tremolo/phaser sweeps). | `SetRate`, `Reset`, `Next`, `Value(phaseOffset)` |
| `WaveShaper` / `DistortionStack` | Non-linear shaping — drive, saturation, distortion. | `WaveShaper.Shape(x, type, drive, bias)` |
| `FilterBank` | Splits a signal into frequency bands (vocoder, multiband). | `Configure(bands, minHz, maxHz, sr)`, `Process(input, bandsOut)` |
| `CaptureBuffer` | A larger ring buffer for capturing/replaying audio (stutter, granular). | `Write`, `ReadAbs`, `Snapshot` |
| `PitchShifter` / `PitchDetector` | Shift or detect pitch (auto-tune, harmonisers). | `Configure`, `SetRatio`, `Process` / `Push`, `Detect` |
| `CombFilter`, `TapeStopProcessor`, `SpectrumScope` | Comb resonator, tape-stop effect, analyser tap. | see each file |
| `AudioMath` | dB↔linear, clamp, soft-clip, lerp, equal-power pan. | `Db2Lin`, `Lin2Db`, `Clamp`, `SoftClip`, `Lerp`, `PanGains` |

A typical filtered-effect skeleton wires these together: in `Prepare`, compute coefficients and reset
the filter; in `Process`, run each sample through `Biquad.Process`.

---

## 7. All the built-in effects (study targets)

Every one of these is a real, readable implementation in
[`Audio/Effects`](../Ongenet.Core/Audio/Effects):

| Category | Effects | Good example of… |
| --- | --- | --- |
| EQ & Filter | `EqEffect`, `FilterEffect` | biquad filtering, a custom inspector UI |
| Dynamics | `CompressorEffect`, `LimiterEffect`, `GateEffect`, `SidechainEffect` | envelope followers, parameter smoothing, sidechain |
| Modulation | `ChorusEffect`, `PhaserEffect`, `FlangerEffect`, `TremoloEffect`, `StutteroEffect` | LFOs + delay lines; tempo-sync + MIDI (Stuttero) |
| Delay & Reverb | `DelayEffect`, `ReverbEffect` | ring buffers, multi-line reverb |
| Distortion | `DistortionEffect`, `BitcrusherEffect` | waveshaping, sample-rate/bit reduction |
| Pitch | `VocoderEffect`, `AutoTuneEffect` | filter banks, pitch detection, sidechain carrier |
| Utility | `StereoWidthEffect`, `UtilityEffect` | mid/side, simple gain/pan |

For a simple effect, read `UtilityEffect` and `DelayEffect`. For the full toolbox in action, read
`StutteroEffect` (tempo + MIDI + custom state) and `VocoderEffect` (sidechain + filter bank).

---

## 8. Advanced seams: tempo, sidechain, MIDI, custom state

Effects opt into extra capabilities by implementing small marker interfaces *in addition to*
`IAudioEffect`. The engine detects them and feeds them what they need.

### Tempo & transport — `IContextualEffect`

To sync to the project tempo or know where the playhead is, implement `IContextualEffect`. The engine
calls `SetContext` right before every `Process`, handing you an `EffectContext`:

```csharp
public interface IContextualEffect
{
    void SetContext(EffectContext context);
}

public sealed class EffectContext
{
    public AudioFormat Format { get; set; }
    public double Bpm { get; set; } = 120.0;     // project tempo
    public double PlayheadBeats { get; set; }    // position at the START of this block, in quarter-note beats
    public bool Playing { get; set; }
    public ISidechainBus Sidechain { get; set; } // see below
}
```

Typical use — convert beats to samples for a tempo-synced rate:

```csharp
private EffectContext? _ctx;
public void SetContext(EffectContext context) => _ctx = context;

private double SamplesPerBeat()
{
    var bpm = _ctx?.Bpm ?? 120.0;
    if (bpm <= 0) bpm = 120.0;
    return _sampleRate * 60.0 / bpm;     // e.g. a 1/4-note delay = this many samples
}
```

### Sidechain (reading another track) — `ISourceTrackEffect` + `IContextualEffect`

A **sidechain** lets your effect read *another track's* audio — the classic "duck the bass when the
kick hits", or a vocoder using a synth as its carrier. Implement `ISourceTrackEffect` to expose which
track to read:

```csharp
public interface ISourceTrackEffect
{
    Guid? SourceTrackId { get; set; }   // the UI lets the user pick this
}
```

Then, in `Process`, request and read that track from the sidechain bus (available via
`EffectContext.Sidechain`):

```csharp
if (_ctx is not null && SourceTrackId is { } id)
{
    _ctx.Sidechain.Request(id);                          // ask for it (once per block)
    var src = _ctx.Sidechain.Read(id, out var srcChannels);  // read its post-FX audio
    // ... use src as the sidechain/carrier signal ...
}
```

The bus is audio-thread-only and lock-free. If the source track happens to render *after* you, you get
its previous block (one block of latency) — fine for control signals. See
[`SidechainBus.cs`](../Ongenet.Core/Audio/Effects/SidechainBus.cs),
[`SidechainEffect`](../Ongenet.Core/Audio/Effects/SidechainEffect.cs) and
[`VocoderEffect`](../Ongenet.Core/Audio/Effects/VocoderEffect.cs).

### MIDI into an effect — `IMidiAwareEffect`

Some effects respond to MIDI notes (e.g. Stuttero triggers stutters from keys). Implement
`IMidiAwareEffect`:

```csharp
public interface IMidiAwareEffect
{
    void HandleMidi(in MidiMessage message);
    void AllNotesOff();
}
```

**Thread safety matters here:** MIDI can arrive from a different thread than `Process`. The standard,
safe pattern is to push messages into a `MidiEventFifo` and drain them at the start of `Process`:

```csharp
private readonly MidiEventFifo _fifo = new();
private readonly List<MidiMessage> _drain = new();

public void HandleMidi(in MidiMessage message) => _fifo.Push(message);   // any thread

public void Process(Span<float> buffer)
{
    _fifo.Drain(_drain);                  // audio thread: pull queued messages
    foreach (var m in _drain) { /* react to notes/CC */ }
    // ... audio processing ...
}
```

`MidiMessage` decodes the raw bytes for you: `message.Note`, `message.Velocity` (0..1),
`message.Controller`, `message.Value`, `message.PitchBend14`, etc.

### Extra saved state — `IProjectStatefulComponent`

If your effect has state that isn't a simple `Parameter` (an EQ's variable list of bands, a chosen
source-track GUID, Stuttero's drawn gestures), implement `IProjectStatefulComponent` to read/write it
to the project file:

```csharp
public interface IProjectStatefulComponent
{
    void WriteProjectState(OngenWriter writer);
    void ReadProjectState(OngenReader reader);
}
```

(Plain parameter values are saved automatically; you only need this for the extras.)

### Spectrum analyser display — `ISpectrumSource`

Implement `ISpectrumSource` (and feed a `SpectrumScope` from `Process`) if you want the inspector to
draw a live spectrum, as `EqEffect` and `FilterEffect` do.

---

## 9. Registering your effect

The app finds effects through the
[`EffectRegistry`](../Ongenet.Core/Audio/Effects/EffectRegistry.cs). Add one line to the `_builtIn`
list and you're done — the library, the "add effect" menu, and project loading all read from it:

```19:40:Ongenet.Core/Audio/Effects/EffectRegistry.cs
    private readonly List<EffectInfo> _builtIn = new()
    {
        new EffectInfo(EqEffect.TypeId, "EQ", () => new EqEffect(), CatEqFilter),
        new EffectInfo(FilterEffect.TypeId, "Filter", () => new FilterEffect(), CatEqFilter),
        new EffectInfo(CompressorEffect.TypeId, "Compressor", () => new CompressorEffect(), CatDynamics),
        new EffectInfo(LimiterEffect.TypeId, "Limiter", () => new LimiterEffect(), CatDynamics),
        new EffectInfo(GateEffect.TypeId, "Gate", () => new GateEffect(), CatDynamics),
        new EffectInfo(SidechainEffect.TypeId, "Sidechain", () => new SidechainEffect(), CatDynamics),
        new EffectInfo(ChorusEffect.TypeId, "Chorus", () => new ChorusEffect(), CatModulation),
        // ...
        new EffectInfo(UtilityEffect.TypeId, "Utility", () => new UtilityEffect(), CatUtility)
    };
```

Your line, e.g.:

```csharp
new EffectInfo(MyEffect.TypeId, "My Effect", () => new MyEffect(), CatModulation),
```

The category constant (`CatDynamics`, `CatModulation`, …) chooses the group it appears under. The
registry is a DI singleton (registered in
[`ServiceCollectionExtensions`](../Ongenet.Core/DependencyInjection/ServiceCollectionExtensions.cs)), so
nothing else needs wiring. Most effects use the generic parameter inspector automatically; if you want
a bespoke UI (like the EQ's graph), you'd add a dedicated view model in the app's effect-chain view —
not required for a normal effect.

---

## 10. A copy-paste starting template

```csharp
using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

public sealed class MyEffect : IAudioEffect
{
    public const string TypeId = "myeffect";
    string IAudioEffect.TypeId => TypeId;
    public string Name => "My Effect";
    public bool Enabled { get; set; } = true;

    public double Amount { get; set; } = 0.5;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Amount", 0.0, 1.0, () => Amount, v => Amount = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        // allocate / reset any DSP state here (delay lines, filters, followers)
    }

    public void Process(Span<float> buffer)
    {
        var channels = _channels;
        var frames = buffer.Length / channels;
        var amount = (float)Amount;            // read parameters once per block
        for (var f = 0; f < frames; f++)
            for (var c = 0; c < channels; c++)
                buffer[f * channels + c] *= amount;   // your DSP here, in place
    }

    public IAudioEffect Clone() => new MyEffect { Enabled = Enabled, Amount = Amount };
}
```

Then register it in `EffectRegistry._builtIn`:

```csharp
new EffectInfo(MyEffect.TypeId, "My Effect", () => new MyEffect(), CatUtility),
```

---

## 11. Build your own — step by step

1. **Create the class** in `Ongenet.Core/Audio/Effects/MyEffect.cs` implementing `IAudioEffect`.
2. **Add a stable id** (`public const string TypeId = "myeffect";` + `string IAudioEffect.TypeId => TypeId;`).
3. **Add backing properties** for your knobs and a lazy `Parameters` list wrapping them.
4. **In `Prepare`:** cache `format.SampleRate`/`format.Channels`; allocate & reset DSP state.
5. **In `Process`:** loop frames × channels, read parameters from the properties, transform the buffer
   **in place**, no allocations.
6. **Implement `Clone()`** copying every parameter field.
7. **Register** with one line in `EffectRegistry._builtIn`.
8. **Add seams as needed:** `IContextualEffect` (tempo), `ISourceTrackEffect` + `IContextualEffect`
   (sidechain), `IMidiAwareEffect` (MIDI), `IProjectStatefulComponent` (extra saved state),
   `ISpectrumSource` (analyser).
9. **Run** `dotnet run --project Ongenet.Desktop`, add a track, drop your effect on it, and listen.

### The cardinal rules (clean-audio checklist)

- **Edit in place** in `Process` (`buffer[i] = …`).
- **Never allocate** in `Process` — pre-allocate everything in `Prepare`.
- **Re-init state in `Prepare`** (there is no separate `Reset`); it's called on format change.
- **Use the real sample rate** from `format`, not a hard-coded 44,100.
- **Clamp parameters** defensively before using them.
- **Smooth fast-changing parameters** (e.g. with `OnePole`) to avoid clicks/zipper noise.
- **Keep `TypeId` stable forever** — it lives in saved projects.
- **Make MIDI handling thread-safe** with a `MidiEventFifo` drained inside `Process`.

---

## Where to go next

- [creating-instruments.md](creating-instruments.md) — the sibling guide for instruments.
- [audio-engine.md](audio-engine.md) — how the engine calls `Process`, the full signal flow (instrument
  pre-FX → track FX → buses → master), and real-time safety in depth.
- [main-window-layout.md](main-window-layout.md) — where effects and their inspectors live in the UI.
