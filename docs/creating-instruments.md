# Creating new instruments

This guide walks you through building a new instrument for Ongenet — a synth or a sampler — from
nothing to something you can play on the keyboard. It is written for people who are comfortable with
C# but **new to audio programming (DSP)**, so it explains the audio concepts as it goes.

Everything here lives in [`Ongenet.Core`](../Ongenet.Core) (`Audio/Instruments`, `Audio/Dsp`,
`Audio/Parameters`). That project has no UI and no native dependencies — it is plain `net10.0` — so you
can build and reason about instruments in isolation.

> **The shortest path:** copy an existing small instrument
> ([`OscillatorInstrument.cs`](../Ongenet.Core/Audio/Instruments/OscillatorInstrument.cs)), rename it,
> change the DSP inside the voice, and add one line to the registry. The rest of this guide explains
> *why* each piece is shaped the way it is so you can go further.

---

## 1. The mental model

A few ideas underpin everything below. If audio code is new to you, read this section slowly — the
rest of the guide assumes it.

**Audio is just a stream of numbers.** Sound is represented as a long sequence of `float` samples. At
44,100 Hz ("44.1 kHz") there are 44,100 numbers *per second per channel*. Each number is the speaker
position at that instant, in the range `[-1.0, 1.0]`. Your instrument's whole job is to produce those
numbers.

**The engine "pulls" audio in blocks.** The sound card repeatedly asks the engine for the next small
chunk of samples (a *block* — often a few hundred frames). For each block, the engine asks every
instrument to add its sound in. You never decide *when* you run; you just fill the buffer you are
handed. (The full story is in [audio-engine.md](audio-engine.md).)

**A "frame" vs a "sample".** One *frame* is one moment in time across all channels. In stereo, one
frame is two samples (left, right). Buffers are **interleaved**: `[L, R, L, R, L, R, …]`. So for a
buffer of length `N` with `channels` channels, `frames = N / channels`, and the sample for channel `c`
of frame `f` is at index `f * channels + c`.

**Rendering is additive.** You do **not** overwrite the buffer. You **add** (`+=`) your sound into it.
The engine pre-clears the buffer and then lets every source sum into the same buffer — that is how a
mixer combines many sounds without extra copies. This rule is the single most important thing to get
right.

**The audio thread is sacred.** Your rendering code runs on a high-priority real-time thread. If it is
slow, allocates memory (triggering the garbage collector), or blocks on a lock, you get audible clicks
and dropouts. The rule of thumb: **allocate everything up front, never `new` anything inside `Render`.**

---

## 2. The contracts you implement

### `ISampleSource` — "produce audio"

Every audio producer implements this two-method interface:

```12:25:Ongenet.Core/Audio/ISampleSource.cs
public interface ISampleSource
{
    /// <summary>
    /// Called before rendering (and whenever the format changes) to hand the source the
    /// engine's sample rate and channel count.
    /// </summary>
    void Prepare(AudioFormat format);

    /// <summary>
    /// Adds this source's output into <paramref name="buffer"/> (interleaved samples,
    /// length = frames × channels). Must not allocate.
    /// </summary>
    void Render(Span<float> buffer);
}
```

`AudioFormat` is just the sample rate and channel count. Samples are always 32-bit float, interleaved:

```7:11:Ongenet.Core/Audio/AudioFormat.cs
public readonly record struct AudioFormat(int SampleRate, int Channels)
{
    /// <summary>A common default: 44.1 kHz stereo.</summary>
    public static AudioFormat Default => new(44100, 2);
}
```

`Prepare` is your chance to learn the sample rate (you need it to tune oscillators and envelopes) and
to size any buffers you keep. It is called again if the audio device changes, so re-do that setup each
time rather than only in your constructor.

### `IInstrument` — "produce audio *and* respond to notes"

An instrument is an `ISampleSource` that also reacts to MIDI-style note events:

```10:45:Ongenet.Core/Audio/Instruments/IInstrument.cs
public interface IInstrument : ISampleSource
{
    /// <summary>Display name of the instrument.</summary>
    string Name { get; }

    /// <summary>Stable registry type id, used to recreate this instrument when loading a project.</summary>
    string TypeId { get; }

    /// <summary>Editable parameters, rendered generically by the instrument inspector.</summary>
    IReadOnlyList<Parameter> Parameters { get; }

    /// <summary>Starts a note. <paramref name="velocity"/> is 0..1.</summary>
    void NoteOn(int midiNote, float velocity);

    /// <summary>Releases a note.</summary>
    void NoteOff(int midiNote);

    /// <summary>Releases all sounding notes (e.g. on stop or track change).</summary>
    void AllNotesOff();

    void ControlChange(int controller, int value) { }   // CC 0..127 (default no-op)
    void PitchBend(int value14) { }                      // 0..16383, centre 8192 (default no-op)
    void ChannelAftertouch(int value) { }                // 0..127 (default no-op)

    /// <summary>Creates a fresh copy of this instrument with the same parameters (for track duplication).</summary>
    IInstrument Clone();
}
```

A few notes on the members:

- **`midiNote`** is a MIDI note number: 60 is middle C, 69 is A4 (440 Hz). To turn that into a
  frequency use the helper `MusicalMath.NoteToFrequency(midiNote)`.
- **`velocity`** is already normalised to `0..1` for you (how hard the key was hit).
- **`TypeId`** is a short, *stable* string (e.g. `"oscillator"`). It is saved into project files and
  used to recreate your instrument on load, so never change it once shipped.
- **`ControlChange` / `PitchBend` / `ChannelAftertouch`** are optional (they have default no-op
  implementations) — implement them only if your instrument reacts to the mod wheel, pitch bend, etc.
- **`Clone()`** must return a new instance with the same settings. It is used when a track is
  duplicated.

You *could* implement `IInstrument` directly (the full SFZ
[`SamplerInstrument`](../Ongenet.Core/Audio/Instruments/Sampler/SamplerInstrument.cs) does, because it
needs custom note routing). But for almost everything, there is a much easier base class.

---

## 3. The voice model: `PolyphonicInstrument`

**Polyphony** means playing several notes at once. Each sounding note is a **voice**. The standard
pattern is: one small DSP object per voice, a fixed pool of them, and a manager that hands a free voice
to each `NoteOn`. Ongenet gives you that manager for free.

### `Voice` — one sounding note

```10:34:Ongenet.Core/Audio/Instruments/Voice.cs
public abstract class Voice
{
    /// <summary>Whether this voice is currently producing sound.</summary>
    public bool IsActive { get; protected set; }

    /// <summary>The MIDI note this voice is playing (valid while active).</summary>
    public int Note { get; protected set; }

    /// <summary>The engine format. Set on <see cref="Start"/>.</summary>
    protected AudioFormat Format { get; private set; }

    /// <summary>Begins playing a note. Overrides should call base first.</summary>
    public virtual void Start(int midiNote, float velocity, AudioFormat format)
    {
        Note = midiNote;
        Format = format;
        IsActive = true;
    }

    /// <summary>Begins the release phase. The voice keeps rendering until its tail finishes.</summary>
    public abstract void Release();

    /// <summary>Adds this voice's output into <paramref name="buffer"/> (interleaved).</summary>
    public abstract void Render(Span<float> buffer);
}
```

The lifecycle of a voice is: **`Start` → render many blocks → `Release` → render a little more (the
release tail) → set `IsActive = false`**. That last step is crucial: when your envelope finishes, set
`IsActive = false` so the manager can reuse the voice. Voices are pooled and reused forever, so — say
it with me — **do not allocate inside `Render`.**

### `PolyphonicInstrument` — the voice manager

You implement two things (`CreateVoice` and your parameters); it handles the rest — allocating the
pool once, routing notes to free voices, **stealing** the oldest voice when all are busy, and summing
voices each block.

```13:60:Ongenet.Core/Audio/Instruments/PolyphonicInstrument.cs
public abstract class PolyphonicInstrument : IInstrument
{
    private readonly Voice[] _voices;
    // ...
    protected PolyphonicInstrument(int polyphony = 16)
    {
        _voices = new Voice[polyphony];
        _startOrder = new uint[polyphony];
        for (var i = 0; i < polyphony; i++)
        {
            _voices[i] = CreateVoice();
        }
    }

    /// <summary>Creates one voice for the pool. Called once per voice at construction.</summary>
    protected abstract Voice CreateVoice();
    // ...
    public void NoteOn(int midiNote, float velocity)
    {
        lock (_lock)
        {
            var index = FindFreeVoice();
            if (index < 0) index = FindOldestVoice();   // voice stealing
            _voices[index].Start(midiNote, velocity, _format);
            _startOrder[index] = _counter++;
        }
    }
}
```

Things worth understanding:

- **The pool is allocated once**, in the constructor, by calling your `CreateVoice()` `polyphony`
  times. Pass a different number to the base constructor (`: base(8)`) if you want more or fewer
  voices. More voices = more CPU when you play big chords.
- **Voice stealing:** if you play a 17th note on a 16-voice synth, the *oldest* voice is cut and
  reused. That is what `FindOldestVoice` does, using a monotonic counter.
- **Threading:** `NoteOn`/`NoteOff` take a lock (they can be called from the UI/MIDI thread), but the
  audio thread's `RenderVoices` reads voices *without* locking — each voice just checks its own
  `IsActive`. You don't have to think about this; it is handled for you.
- **`TypeId` quirk:** because a class can't have both a `const string TypeId` *and* an interface
  property of the same name, the base routes the interface through a protected hook. You write
  `public const string TypeId = "…";` plus `protected override string GetTypeId() => TypeId;`.

For most instruments, the default `Render` (which just sums voices) is all you need. If you want a
*global* effect over the whole instrument (e.g. one shared reverb after all voices), override `Render`,
sum the voices into a scratch buffer with `RenderVoices(scratch)`, process that, then add it to the
output — that scratch buffer must be a pre-allocated field, not a local `new`.

---

## 4. The DSP toolkit (reuse, don't reinvent)

Before writing your own oscillator or filter, check
[`Ongenet.Core/Audio/Dsp`](../Ongenet.Core/Audio/Dsp). These are the shared, audio-thread-safe
building blocks. You'll wire them together inside your voice. The most useful for instruments:

| Building block | What it is (in plain terms) | Key methods |
| --- | --- | --- |
| `WaveOscillator` | A tone generator. Sine/triangle/saw/square/noise/custom. The "vibrating string". | `SetSampleRate`, `SetFrequency`, `ResetPhase`, `Next()` → one sample |
| `UnisonOscillator` | Several detuned oscillators stacked for a fat, wide sound. | `Configure(voices, detuneCents, width, blend)`, `SetBaseFrequency`, `Render(out l, out r)` |
| `DahdsrEnvelope` | A volume shape over time (Delay-Attack-Hold-Decay-Sustain-Release). The "fade in/out". | `SetSampleRate`, `Gate()`, `Release()`, `Process()` → 0..1, `IsActive` |
| `Lfo` | A slow oscillator used to *modulate* things (wobble pitch, sweep a filter). | `SetRate`, `Reset`, `Next()` |
| `Biquad` + `BiquadCoefficients` | A filter — boost/cut frequency ranges (low-pass = "darker", etc.). | `Compute(mode, freq, q, sampleRate)` then `Process(in coeffs, x)` |
| `OnePole` | A cheap one-pole filter, also great for *smoothing* a parameter so it doesn't zipper. | `SetLowpass`, `SetSmoothTime`, `ProcessLP/HP`, `Reset` |
| `HermiteInterpolator` | High-quality "read between samples" — essential for pitching a sample up/down. | `Sample(ym1, y0, y1, y2, t)` |
| `DelayLine` | A ring buffer that remembers the last N samples (echoes, choruses, combs). | `Resize`, `Clear`, `ReadInt/ReadFrac`, `Write` |
| `WaveShaper` / `DistortionStack` | Non-linear shaping — saturation, drive, distortion. | `Shape(x, type, drive, bias)` |
| `AudioMath` | Helpers: dB↔linear, clamp, soft-clip, lerp, equal-power pan gains. | `Db2Lin`, `Lin2Db`, `Clamp`, `SoftClip`, `Lerp`, `PanGains` |
| `MusicalMath` | Music helpers, notably note number → frequency. | `NoteToFrequency(midiNote)` |

> There is also a simpler local `Oscillator` and `AdsrEnvelope` living in `Audio/Instruments/` (used by
> the first example below). They are deliberately minimal — good for learning. The `Dsp/` versions are
> the fuller-featured ones you'll graduate to.

---

## 5. The parameter system

Parameters are the knobs and switches shown in the instrument inspector. They are **not** a separate
copy of your state — they are thin wrappers that *get and set your own fields through delegates*. That
keeps a single source of truth: the field. The audio thread reads the field directly; the UI reads and
writes it through the parameter.

Three concrete kinds live in [`Audio/Parameters`](../Ongenet.Core/Audio/Parameters):

```11:23:Ongenet.Core/Audio/Parameters/FloatParameter.cs
public FloatParameter(string name, double min, double max, Func<double> get, Action<double> set,
    string format = "0.##", string unit = "", double skew = 1.0)
    : base(name)
{
    Min = min;
    Max = max;
    _get = get;
    _set = set;
    DefaultValue = get(); // the owner's initial value (its code default) — used by "Reset to default"
    Format = format;
    Unit = unit;
    Skew = skew <= 0 ? 1.0 : skew;
}
```

- **`FloatParameter`** — a continuous value (slider/knob). `min`/`max` set the range; `format` is a
  .NET number format; `unit` is a suffix like `"Hz"`, `"s"`, `"%"`; `skew` bends the knob curve so a
  value like frequency feels natural (use `skew > 1` for fine control near the bottom).
- **`BoolParameter`** — an on/off switch. `new BoolParameter("Mono", () => Mono, v => Mono = v)`.
- **`ChoiceParameter`** — a dropdown. `new ChoiceParameter("Waveform", options, () => index, i => …)`.

You expose them as a `Parameters` list, built lazily so it's only constructed once. The optional
`{ Group = "Amp Envelope" }` initializer groups knobs into a labelled section in the inspector.

**How to read a parameter on the audio thread:** there is no special API — you just read the backing
property. Read it *per block* (or per sample) if you want a turned knob to affect notes that are
already sounding; capture it in `Start()` if you want each note frozen to the value it began with.

**Automation comes for free.** Because parameters expose `min`/`max` and a get/set, the user can
right-click a knob and draw an automation curve; the engine writes the parameter before each block.
You don't write any automation code — just expose the parameter.

---

## 6. Worked example: a complete oscillator synth

Here is the entire first built-in instrument,
[`OscillatorInstrument.cs`](../Ongenet.Core/Audio/Instruments/OscillatorInstrument.cs). It is a
polyphonic synth: each voice is one oscillator shaped by an ADSR amplitude envelope. Read it top to
bottom — it is a faithful template for almost any subtractive synth.

```12:51:Ongenet.Core/Audio/Instruments/OscillatorInstrument.cs
public sealed class OscillatorInstrument : PolyphonicInstrument
{
    /// <summary>Registry id for this instrument type.</summary>
    public const string TypeId = "oscillator";

    protected override string GetTypeId() => TypeId;

    /// <summary>Current waveform. Read live by every voice while rendering.</summary>
    public Waveform Waveform { get; set; } = Waveform.Sawtooth;

    // Envelope parameters, applied to a voice when its note starts.
    public double AttackSeconds { get; set; } = 0.005;
    public double DecaySeconds { get; set; } = 0.08;
    public double SustainLevel { get; set; } = 0.7;
    public double ReleaseSeconds { get; set; } = 0.2;

    public override string Name => "Oscillator";

    private IReadOnlyList<Parameter>? _parameters;

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Waveform", new[] { "Sine", "Sawtooth", "Square" },
            () => (int)Waveform, i => Waveform = (Waveform)i) { Group = "Oscillator" },
        new FloatParameter("Attack", 0.001, 2.0, () => AttackSeconds, v => AttackSeconds = v, "0.000", "s") { Group = "Amp Envelope" },
        new FloatParameter("Decay", 0.001, 2.0, () => DecaySeconds, v => DecaySeconds = v, "0.000", "s") { Group = "Amp Envelope" },
        new FloatParameter("Sustain", 0.0, 1.0, () => SustainLevel, v => SustainLevel = v) { Group = "Amp Envelope" },
        new FloatParameter("Release", 0.001, 3.0, () => ReleaseSeconds, v => ReleaseSeconds = v, "0.000", "s") { Group = "Amp Envelope" }
    };

    public override IInstrument Clone() => new OscillatorInstrument
    {
        Waveform = Waveform,
        AttackSeconds = AttackSeconds,
        DecaySeconds = DecaySeconds,
        SustainLevel = SustainLevel,
        ReleaseSeconds = ReleaseSeconds
    };

    protected override Voice CreateVoice() => new OscillatorVoice(this);
    // ... voice below ...
}
```

The instrument itself is mostly bookkeeping: an id, a name, some public fields for the knob values, the
`Parameters` list that wraps those fields, a `Clone()` that copies them, and `CreateVoice()`. All the
actual sound happens in the voice:

```54:109:Ongenet.Core/Audio/Instruments/OscillatorInstrument.cs
    /// <summary>One oscillator + envelope. References its instrument to read live parameters.</summary>
    private sealed class OscillatorVoice : Voice
    {
        // Per-voice output gain so a stack of voices stays clear of clipping.
        private const float VoiceGain = 0.22f;

        private readonly OscillatorInstrument _instrument;
        private readonly Oscillator _oscillator = new();
        private readonly AdsrEnvelope _envelope = new();
        private float _velocity;

        public OscillatorVoice(OscillatorInstrument instrument) => _instrument = instrument;

        public override void Start(int midiNote, float velocity, AudioFormat format)
        {
            base.Start(midiNote, velocity, format);
            _velocity = velocity;

            _oscillator.SetSampleRate(format.SampleRate);
            _oscillator.SetFrequency(MusicalMath.NoteToFrequency(midiNote));
            _oscillator.ResetPhase();

            _envelope.SetSampleRate(format.SampleRate);
            _envelope.AttackSeconds = _instrument.AttackSeconds;
            _envelope.DecaySeconds = _instrument.DecaySeconds;
            _envelope.SustainLevel = _instrument.SustainLevel;
            _envelope.ReleaseSeconds = _instrument.ReleaseSeconds;
            _envelope.Gate();
        }

        public override void Release() => _envelope.Release();

        public override void Render(Span<float> buffer)
        {
            var channels = Format.Channels < 1 ? 1 : Format.Channels;
            var frames = buffer.Length / channels;

            for (var frame = 0; frame < frames; frame++)
            {
                // Pick up live waveform changes.
                _oscillator.Waveform = _instrument.Waveform;

                var sample = _oscillator.Next() * _envelope.Process() * _velocity * VoiceGain;

                var baseIndex = frame * channels;
                for (var ch = 0; ch < channels; ch++)
                {
                    buffer[baseIndex + ch] += sample;
                }

                if (!_envelope.IsActive)
                {
                    IsActive = false;
                    return;
                }
            }
        }
    }
```

Walk through `Render`, because it is the heart of every instrument:

1. Work out `channels` and `frames` from the buffer length.
2. For each frame, generate **one mono sample** = oscillator output × envelope level × velocity × a
   gain that keeps stacked voices from clipping.
3. **Add** that sample into every channel of the frame (`buffer[baseIndex + ch] += sample`) — mono
   source spread across the stereo buffer. Note the `+=`: additive rendering.
4. When the envelope has fully released (`!IsActive`), flip `IsActive = false` and bail — the manager
   reclaims the voice.

The oscillator and envelope it uses are tiny and worth reading once to demystify "DSP":

- [`Oscillator.cs`](../Ongenet.Core/Audio/Instruments/Oscillator.cs) keeps a `_phase` in `[0,1)` and
  advances it by `frequency / sampleRate` each sample, mapping that phase to a sine/saw/square shape.
- [`AdsrEnvelope.cs`](../Ongenet.Core/Audio/Instruments/AdsrEnvelope.cs) is a little state machine
  (Attack → Decay → Sustain → Release) that returns a level in `[0,1]` per sample.

That's the whole trick: generate a number, shape it, add it in, repeat 44,100 times a second.

Other built-ins are variations on this same shape and make great references:
[`FmSynthInstrument`](../Ongenet.Core/Audio/Instruments/FmSynthInstrument.cs) (two-operator FM),
[`TripleOscInstrument`](../Ongenet.Core/Audio/Instruments/TripleOscInstrument.cs) (three oscillators +
filter), [`PaddaInstrument`](../Ongenet.Core/Audio/Instruments/PaddaInstrument.cs) (unison + an
internal effect chain, overriding `Render`), and
[`KickaInstrument`](../Ongenet.Core/Audio/Instruments/KickaInstrument.cs) (a one-shot drum synth).

---

## 7. Worked example: a sampler

A **sampler** plays back a recorded audio clip instead of generating a tone. To play a note at a
different pitch, you read through the recording faster or slower. Reading "between" stored samples
needs interpolation — that's what `HermiteInterpolator` (or simple linear interpolation) is for.

The smallest example is
[`BasicSamplerInstrument`](../Ongenet.Core/Audio/Instruments/BasicSamplerInstrument.cs). It is still a
`PolyphonicInstrument`, but it also implements `ISampleHost` so the inspector can load an audio file
into it:

```csharp
public sealed class BasicSamplerInstrument : PolyphonicInstrument, ISampleHost
{
    public const string TypeId = "sampler";
    protected override string GetTypeId() => TypeId;

    public void LoadSample(AudioSampleBuffer sample, string name);   // from ISampleHost

    protected override Voice CreateVoice() => new SampleVoice(this);
}
```

Inside the voice, "pitching" is just choosing a read-speed:

```csharp
// Read the sample 2x faster for each octave above the sample's root note (C4 = 60).
double pitchRatio = Math.Pow(2.0, (Note - 60) / 12.0);
// Each output sample advances the read position by pitchRatio; interpolate between stored samples.
```

For a full multi-sample instrument (different recordings per key and per velocity, sustain pedal,
round-robin, disk streaming), study
[`Sampler/SamplerInstrument.cs`](../Ongenet.Core/Audio/Instruments/Sampler/SamplerInstrument.cs). It
implements `IInstrument` directly rather than using `PolyphonicInstrument`, because it needs richer note
handling — a good example of when to step outside the base class.

### Optional extension interfaces

Mix these in when your instrument needs more than notes and parameters:

| Interface | Gives you | Implemented by |
| --- | --- | --- |
| `ISampleHost` | A "Load sample" button + the sample saved into the project | `BasicSamplerInstrument`, `TripleOscInstrument` |
| `IPresetProvider` | A built-in preset picker (`PresetNames`, `LoadPreset(i)`) | `KickaInstrument`, `PaddaInstrument` |
| `IPreviewRenderer` | A little waveform preview in the inspector | `KickaInstrument` |
| `IProjectStatefulComponent` | Save/load extra state beyond parameters | `SamplerInstrument` |
| `IRuntimeCloneable` | Faster undo/redo without re-decoding samples | `SamplerInstrument` |

---

## 8. Registering your instrument

The app discovers instruments through the
[`InstrumentRegistry`](../Ongenet.Core/Audio/Instruments/InstrumentRegistry.cs). Each entry is an
`InstrumentInfo(id, displayName, factory, category)`. To make your instrument appear in the library and
the "add instrument" menu, add one line to the `_builtIn` list:

```19:29:Ongenet.Core/Audio/Instruments/InstrumentRegistry.cs
    private readonly List<InstrumentInfo> _builtIn = new()
    {
        new InstrumentInfo(OscillatorInstrument.TypeId, "Oscillator", () => new OscillatorInstrument(), CatSynth),
        new InstrumentInfo(TripleOscInstrument.TypeId, "3x Osc", () => new TripleOscInstrument(), CatSynth),
        new InstrumentInfo(FmSynthInstrument.TypeId, "FM Synth", () => new FmSynthInstrument(), CatSynth),
        new InstrumentInfo(PaddaInstrument.TypeId, "Padda", () => new PaddaInstrument(), CatSynth),
        new InstrumentInfo(BasicSamplerInstrument.TypeId, "Basic Sampler", () => new BasicSamplerInstrument(), CatSampler),
        new InstrumentInfo(Sampler.SamplerInstrument.TypeId, "Sampler", () => new Sampler.SamplerInstrument(), CatSampler),
        new InstrumentInfo(GranularInstrument.TypeId, "Granular", () => new GranularInstrument(), CatSampler),
        new InstrumentInfo(KickaInstrument.TypeId, "Kicka", () => new KickaInstrument(), CatDrum)
    };
```

Your line, e.g.:

```csharp
new InstrumentInfo(MySynthInstrument.TypeId, "My Synth", () => new MySynthInstrument(), CatSynth),
```

The category (`"Synth"`, `"Sampler"`, `"Drum"`) just decides which group it appears under. The registry
is a singleton registered in
[`ServiceCollectionExtensions`](../Ongenet.Core/DependencyInjection/ServiceCollectionExtensions.cs), and
the inspector/library read `registry.Available` — so nothing else needs wiring. (Plugins like CLAP/LV2
add themselves at runtime via the same `Register` method; you don't need that for a built-in.)

---

## 9. Build your own — step by step

1. **Create the class** in `Ongenet.Core/Audio/Instruments/MySynthInstrument.cs`, extending
   `PolyphonicInstrument`.
2. **Add the id**: `public const string TypeId = "mysynth";` and
   `protected override string GetTypeId() => TypeId;`. Implement `public override string Name => "My Synth";`.
3. **Add public fields** for each knob value, with sensible defaults.
4. **Expose `Parameters`** lazily, wrapping those fields with `FloatParameter`/`BoolParameter`/`ChoiceParameter`.
5. **Implement `Clone()`** to copy every field.
6. **Implement `CreateVoice()`** returning your nested `Voice` subclass.
7. **Write the voice:** allocate DSP objects (oscillators, filters, envelopes) in the constructor;
   configure them in `Start()` using `format.SampleRate` and `MusicalMath.NoteToFrequency(midiNote)`;
   in `Render()` loop frames, compute a sample, `+=` it into all channels, and set `IsActive = false`
   when your envelope finishes.
8. **Register it** with one line in `InstrumentRegistry._builtIn`.
9. **Run** `dotnet run --project Ongenet.Desktop`, add an instrument track, pick your synth, and play
   it on the computer keyboard (see [main-window-layout.md](main-window-layout.md) for the key map).

### The cardinal rules (a checklist for clean audio)

- **Add, never overwrite** in `Render` (`buffer[i] += …`).
- **Never allocate** inside `Render`/`Process` — no `new`, no LINQ, no boxing. Pre-allocate in the
  constructor or `Prepare`/`Start`.
- **Always set `IsActive = false`** when a voice's tail has finished, or you'll waste CPU and never
  free voices.
- **Use the sample rate from `format`**, not a hard-coded 44,100 — devices commonly run at 48 kHz.
- **Spread mono to all channels**; respect `format.Channels`.
- **Keep `TypeId` stable forever** — it lives in saved projects.

---

## Where to go next

- [creating-effects.md](creating-effects.md) — the sibling guide for audio effects (in-place
  processing, tempo, sidechain, MIDI).
- [audio-engine.md](audio-engine.md) — how the engine calls your `Render`, the signal flow, and
  real-time safety in depth.
- [main-window-layout.md](main-window-layout.md) — where instruments show up in the UI.
