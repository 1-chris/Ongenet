# The audio engine & OS audio APIs

This guide explains how Ongenet actually makes sound: how the engine renders one block of audio, how
audio flows from instruments through effects and buses to the master output, what "real-time safety"
means and why it matters, and how the engine talks to each operating system's audio APIs
(PipeWire/PulseAudio/JACK/ALSA on Linux, CoreAudio on macOS, WASAPI on Windows).

It's written for people new to audio programming. If you haven't read
[creating-instruments.md](creating-instruments.md) and [creating-effects.md](creating-effects.md),
start there — they introduce the vocabulary (samples, frames, blocks, interleaving) used here.

The engine lives in [`Ongenet.Core/Audio`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio) (portable, no native code). The
device drivers live in [`Ongenet.Audio`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Audio) (the only project that P/Invokes native
audio libraries). The two meet at one small interface, `IAudioOutput`, so the whole DSP side never knows
which OS it's running on.

---

## 1. The pull model: the sound card asks, the engine answers

The most important idea: **audio is pulled, not pushed.** Your sound card runs a relentless clock. Many
times a second it says "give me the next few hundred samples to play, *right now*." The engine's only
job is to fill the buffer it's handed before the deadline. Miss the deadline and you hear a click or a
dropout.

That request is the `AudioRenderCallback`:

```5:10:Ongenet.Core/Audio/IAudioOutput.cs
/// <summary>
/// Pulls audio from the engine: the device repeatedly calls back asking for the next block of
/// interleaved samples to play.
/// </summary>
/// <param name="buffer">Buffer to fill, length = frames × channels (interleaved).</param>
public delegate void AudioRenderCallback(Span<float> buffer);
```

The engine registers its `Render` method as that callback when it starts:

```196:202:Ongenet.Core/Audio/AudioEngine.cs
    public void Start()
    {
        if (_output.IsRunning) return;
        RebuildTracks();
        if (!CanReuseMasterSpectrum()) PrepareMasterMeters();
        _output.Start(Render);
    }
```

From then on, the OS driver calls `Render(buffer)` on a dedicated high-priority **audio thread**, over
and over, for as long as audio is running. Everything in the rest of this document happens *inside* that
one call.

### The buffer format

Every buffer in Ongenet — from the sound card down to each instrument and effect — is the same:
**32-bit float, interleaved by channel**, at the engine's current sample rate.

```7:11:Ongenet.Core/Audio/AudioFormat.cs
public readonly record struct AudioFormat(int SampleRate, int Channels)
{
    /// <summary>A common default: 44.1 kHz stereo.</summary>
    public static AudioFormat Default => new(44100, 2);
}
```

- **Interleaved** means stereo samples alternate: `[L, R, L, R, …]`.
- **`frames = buffer.Length / channels`** — a *frame* is one moment across all channels.
- The native drivers below all run at **48 kHz** by default; never hard-code 44,100 in DSP — read the
  rate from the format. (If the device's real rate differs, `IAudioOutput` raises `FormatChanged` and the
  engine re-`Prepare`s everything.)

### Block size is not fixed

How many frames you get per callback (the *block size*) varies by driver — roughly 256 frames on ALSA,
512 on PulseAudio, variable on PipeWire/WASAPI/CoreAudio. DSP code must work for any block length, which
is why instruments and effects always compute `frames` from the buffer length rather than assuming a
constant.

---

## 2. Rendering one block, step by step

Here's the top of `AudioEngine.Render` — the entry point for every block:

```557:573:Ongenet.Core/Audio/AudioEngine.cs
    private void Render(Span<float> buffer)
    {
        FlushPendingRebuild();

        var blockStart = Stopwatch.GetTimestamp();
        var blockStartAllocated = GC.GetAllocatedBytesForCurrentThread();
        buffer.Clear();

        var channels = _output.Format.Channels < 1 ? 1 : _output.Format.Channels;
        var frames = buffer.Length / channels;
        AudioDiagnostics.SetBlockBudget(frames, _output.Format.SampleRate);

        // Count-in runs before content: emit metronome clicks, keep the playhead parked.
        if (_countingIn) ProcessCountIn(frames);

        var playing = _playing;
        var prevBeat = _currentBeat;
```

Notice `buffer.Clear()` near the top — the engine **zeroes the buffer first**, which is exactly why
instruments *add* their output (`+=`) instead of overwriting: many sources sum into the same cleared
buffer with no extra copies. Scratch buffers (`_temp`, `_slotTemp`) are reused fields, grown only when
a bigger block arrives — never freshly allocated per call.

Conceptually, each block does this:

```
Render(buffer):
  clear buffer
  if counting in → metronome clicks
  if playing:
    schedule any MIDI notes that fall in this block (fires NoteOn/NoteOff on instruments)
    apply automation for this block (writes track/effect/instrument parameters)
  for each content track (not a bus):
      for each enabled instrument slot:
          instrument.Render(scratch)              # voices sum into scratch
          run the slot's PRE effects on scratch   # effect.Process(scratch)
          add scratch into the track buffer
      run the track's POST effects on the track buffer
      pre-fader sends → return buses
      apply PDC delay if needed
      apply track volume/pan, sum into the track's parent bus
      post-fader sends → return buses
  for each bus, deepest first:
      run the bus's effects
      apply PDC delay if needed
      apply bus volume/pan, sum into its parent (the master sums into the output buffer)
  hard-clamp ±1.0 + master peak / loudness metering
  (Master insert FX — Peak Limiter, etc. — already ran on the Master bus)
```

That's the whole engine in one breath. The rest of this document expands the two interesting parts: the
**signal flow** (how things sum together) and **real-time safety** (why the rules above exist).

---

## 3. Signal flow: instruments → effects → buses → master

### Two effect chains

Audio passes through effects as **inserts** (in series). There are two insert points:

1. **Per instrument slot (pre):** each instrument can have its own effect chain applied right after it
   renders, before it's mixed with the track's other instruments.
2. **Per track (post):** the whole track (all its instruments + any audio clips) passes through the
   track's effect chain.

```mermaid
flowchart TB
    V[Instrument voices] --> S[Slot PRE effects]
    S --> T[Sum slots → track buffer]
    A[Audio clips] --> T
    T --> P[Track POST effects]
    P --> G[Track volume + pan]
    G --> B[Parent bus]
```

### Buses, sends and the routing tree

Every track has a **parent**: its output sums into a *group bus*, and group buses sum into other buses or into the *master*. Return tracks sit in the same tree as parallel aux paths fed by **sends**.

Track kinds are:

```6:31:Ongenet.Core/Models/Audio/TrackKind.cs
public enum TrackKind
{
    Audio,
    Instrument,
    Midi,
    Hybrid,  // audio + MIDI clips on one lane
    Pattern, // FL-style pattern playlist lane
    Group,   // sums children
    Return,  // aux bus fed by sends
    Master   // root bus
}
```

```27:34:Ongenet.Core/Models/Audio/Track.cs
    /// <summary>
    /// The <see cref="Id"/> of the group/master bus this track's output routes into, or null to route
    /// straight to the master. Drives both audio routing and the timeline's nesting/indentation.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>True for a bus (group, return, or master) that sums child output rather than carrying clips.</summary>
    public bool IsBus => Kind is TrackKind.Group or TrackKind.Return or TrackKind.Master;
```

When the track layout changes, the engine builds a **routing snapshot** — a `Bus` per group/return/master,
each linked to its parent, ordered *deepest-first*. Ordering deepest-first means one linear pass can mix
children into groups, then groups into the master, with no recursion:

```260:263:Ongenet.Core/Audio/AudioEngine.cs
    // Builds the bus graph from the current tracks: a Bus per group/master, linked to its parent bus,
    // ordered deepest-first so a block can be mixed children → groups → master in a single pass.
    private void BuildRouting(Track[] tracks)
    {
```

### Auxiliary sends

In addition to the parent/child tree, each content track can have zero or more **sends** — level-controlled
taps into **return** tracks (reverb, delay, parallel compression, etc.). Sends are configured in the track
inspector and mixed by [`SendMixing`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/SendMixing.cs):

```6:20:Ongenet.Core/Models/Audio/TrackSend.cs
public sealed class TrackSend
{
    public Guid TargetTrackId { get; set; }
    public double Level { get; set; } = 0.5;
    /// <summary>When true, taps the signal before the track fader; otherwise post-fader.</summary>
    public bool PreFader { get; set; }
    public bool Enabled { get; set; } = true;
}
```

Each block the engine runs track **POST inserts** first, then processes **pre-fader sends** (pre-fader,
post-insert — before PDC and the main fader), then **post-fader sends** after the track's volume/pan is
applied to its parent bus. Return tracks run their own insert chains and sum into their parent like any
other bus. Sends are independent of the sidechain tap (below) — sidechain is a cross-track *read*, sends
are a parallel *write*.

```mermaid
flowchart TB
    V[Instrument voices] --> S[Slot PRE effects]
    S --> T[Sum slots → track buffer]
    A[Audio clips] --> T
    T --> P[Track POST effects]
    P --> PF[Pre-fader sends → return buses]
    P --> PDC[PDC delay]
    PDC --> G[Track volume + pan]
    G --> B[Parent group bus]
    G --> PO[Post-fader sends → return buses]
    R[Return bus FX] --> B
```

### Plugin delay compensation (PDC)

Effects and instruments that report latency via [`ILatencyProvider`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/ILatencyProvider.cs)
shift their output in time. Without compensation, a heavy lookahead compressor on one track would sit
*ahead* of a dry neighbour and smear transient alignment across the mix.

[`LatencyCompensator`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/LatencyCompensator.cs) walks the routing graph once per
topology change and computes, for every content track and bus:

1. **Path latency** — instrument/slot pre-FX + track post-FX + ancestor bus FX + send/return paths.
2. **Delay samples** — `maxPathLatency − pathLatency`, applied with a per-track [`PdcDelayLine`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Dsp/PdcDelayLine.cs)
   immediately before the track or bus is summed into its parent.

All paths that reach the master therefore align at the mix point. PDC is honoured in live playback and in
[`OfflineRenderer`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/OfflineRenderer.cs) (export, clip render). Latency is summed
from reported plugin values only — the engine does not guess.

```309:330:Ongenet.Core/Audio/AudioEngine.cs
            Pdc = LatencyCompensator.Compute(tracks)
        };
        // …
                st.PdcDelay = comp.DelaySamples;
                st.EnsurePdc(channels, maxFrames);
        // …
                bus.PdcDelay = comp.DelaySamples;
                bus.EnsurePdc(channels, maxFrames);
```

### How summing and panning work

Mixing one buffer into another applies a per-channel gain and accumulates. Volume and pan are turned
into left/right gains by [`Mixing`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Mixing.cs) — tracks use an equal-power pan law
(constant perceived loudness as you pan), buses use a simpler balance law. After summing, the engine
hard-clamps the output to ±1.0 as a last-resort safety net and measures peak levels for the meters you
see in the transport bar. Proper loudness limiting (and ISP headroom) comes from user-inserted
effects on the **Master** track — typically a clipper followed by a limiter — not from a built-in
engine limiter.

### Mastering insert chains

Mastering is DAW-style: insert FX on the pinned Master track. The canonical factory recipe lives in
[`MasteringChains`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Effects/MasteringChains.cs)
(`CreateFullMaster` / `AddFullMaster`) and is shared by the **Full Master** FX Chains library preset and
demo songs:

1. Corrective EQ (HPF / LPF / air shelf)
2. Mid/Side EQ (side low-cut + side air)
3. Glue compressor
4. Stereo width
5. Soft clipper (optional 2×/4× oversampling)
6. Peak Limiter (Streaming preset, optional oversampling)
7. Spectrum analyser (pass-through; skipped during offline export)

**Multiband OTT is intentionally omitted** from Full Master (use on buses, or drag **Full Master+** /
**Techno Master**). Named limiter and multiband macros live in
[`MasteringPresetBank`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Effects/MasteringPresetBank.cs)
with descriptions shown as UI tooltips.

`MasteringChains.Create(name)` builds **eight** core recipes plus **Audiophile Master** and **Reference Master** (**ten** named recipes total):

| Chain | Role |
| --- | --- |
| **Full Master** | Corrective EQ → mid/side → glue → width → clipper → Peak Limiter (Streaming) → Spectrum |
| **Full Master+** | Full Master with Multiband OTT before width |
| **Streaming Master** | EQ → glue → Peak Limiter (Streaming) — no clipper |
| **Pre-Master** | DC blocker → EQ → mid/side → glue only (no limiter) |
| **Club Loud** | Multiband → width → soft Over → clipper → Peak Limiter (Master) |
| **Podcast / Speech** | HPF → de-esser → glue → Peak Limiter (Safety) |
| **Master Glue** | Glue compressor → width → Peak Limiter (Loud) |
| **Techno Master** | HPF → multiband → width → Exciter → Peak Limiter (Master) |
| **Audiophile Master** | Full Master with **Linear-Phase EQ** replacing the minimum-phase corrective EQ (`Create("audiophile")` / `Create("linear")`; higher latency, experimental) |
| **Reference Master** | Corrective EQ → Match EQ → glue → Peak Limiter (Streaming) → Spectrum (`Create("reference")` / `Create("match")`; capture reference spectrum in UI first) |

**Built-in project genre chains** (same recipes as the FX Chains library):

| Project | Master chain | Notes |
| --- | --- | --- |
| **Ascension** | Full Master | Multiband OTT on **Leads** bus, not on Master |
| **First Light**, **House Starter** | Full Master | |
| **Undertow**, **Trap Beat** | Club Loud | |
| **Dust & Vinyl**, **Field Modular**, **Static Bloom**, **Web Demo** | Streaming Master | Field Modular / Static Bloom add reverb before the limiter |
| **Techno Starter** | Techno Master | |

Desktop startup opens **Ascension**; WASM/Android open **Web Demo**. **First Light** is a library demo only.

**Peak Limiter presets** (`MasteringPresetBank.LimiterPresets`):

| Preset | Ceiling | Spectral | Typical use |
| --- | --- | --- | --- |
| Transparent | −0.3 dBFS | off | Archival / CD |
| Streaming | −1.0 dBFS | off | Lossy streaming headroom |
| Loud | −0.5 dBFS | on | Competitive loudness |
| Master | −0.3 dBFS | on | Club / techno masters |
| Safety | −1.5 dBFS | off | Broadcast / podcast |

**Multiband OTT presets** (`MasteringPresetBank.MultibandPresets`): Transparent, Glue, OTT, Aggressive, Max — depth and high-band boost increase with index.

**Delivery platform targets** (`DeliveryPlatformPresets` in export):

| Platform | Integrated LUFS | True peak |
| --- | --- | --- |
| Spotify | −14 | −1.0 dBTP |
| YouTube | −14 | −1.0 dBTP |
| Apple Music | −16 | −1.0 dBTP |
| Club | −9 | −0.3 dBTP |
| Podcast | −16 | −1.5 dBTP |

**Mastering UI workflow:** On the Master Effects tab, use **Bypass chain**, loudness-measured **Compare**, ordered-chain **A/B** snapshots, and WAV **reference** slots A/B (LUFS-matched audition). The chain picker applies any of the recipes above (eight core chains plus Audiophile and Reference). Title-bar meters can tap **Pre Limiter**, **Post Chain**, or **Post Fader**. The **Spectrum** analyser panel shows peak bars, LUFS, correlation, and a **Scope** goniometer (L vs R Lissajous with mid / anti-phase diagonals); **Tool** supplies peak/LUFS/correlation meters without a goniometer. Delivery targets are shared app-wide via `IMasteringDeliveryTarget` (Master Effects tab, title-bar meter, Export dialog).

**Match EQ** (`MatchEqEffect`) captures a target magnitude spectrum (48 analysis bins) and applies approximate **12-band** peaking EQ toward it via **Blend** and **Smoothness**.

**Two limiters:** `PeakLimiterEffect` (Mastering category — presets, GR meter, spectral mode, oversampling)
is the delivery brickwall. `LimiterEffect` (Dynamics) is a simpler bus limiter kept for drum / bus chains
that do not need mastering macros.

The title-bar master meter can tap **Pre Limiter**, **Post Chain**, or **Post Fader**. Its dBTP reading
uses a 4× polyphase FIR reconstruction meter (BS.1770-style true-peak measurement), not a
sample-peak-only or Hermite/ZOH estimate. Short-term and integrated LUFS use ITU-R BS.1770
K-weighting; loudness reports also include **LRA** (loudness range). The title-bar bars are stereo L/R
(the first two channels); surround export analysis uses all channels with full BS.1770 channel weights.
Clipper and Peak Limiter oversampling is a real FIR **upsample → process nonlinear DSP
at the higher rate → downsample** path; the 1× choice alone is sample-peak based.

The Peak Limiter's optional **Spectral** mode adds a slower spectral-envelope follower to the fast
peak detector. It deliberately holds more reduction after dense/transient energy, reducing bright
bursts at the cost of less transparent release behaviour; preset descriptions call out where it is
enabled. The limiter card plots recent gain reduction and its delivery ceiling.

Stem export can bake Master inserts via
`ExportOptions.IncludeMasterFx`. Master/region export supports pre-master bounce (`BypassMasterFx`),
16-bit TPDF or noise-shaped dither, loudness analysis (integrated LUFS, LRA, momentary/short-term peaks), and optional
platform LUFS normalisation. When normalise boosts gain, `ApplyDeliveryLimiter` re-limits at the delivery
ceiling instead of only attenuating. Rate conversion uses a 48-tap windowed-sinc low-pass. WAV exports
embed LUFS/true-peak in RIFF INFO/ICMT; encoded audio receives ReplayGain and R128 tags. Loudness JSON sidecars include `ReplayGainTrackGainDb` when a target
LUFS is set. Album scripts should call `MatchAlbumLoudness` (via `IScriptingApi`) to align
multiple tracks to a shared target while preserving relative loudness.

Offline render skips `IAnalyserOnlyEffect` processors (Spectrum, Loudness Meter, 3D Scope, Oscilloscope)
and metering-only `ToolEffect` paths for speed.

### Sidechain: reading another track

Some effects need a *second* input — the classic "duck the bass when the kick hits" or a vocoder's
carrier. That's the **sidechain bus**: an effect calls `Sidechain.Request(trackId)` and then
`Sidechain.Read(trackId, …)` to get that track's post-effects audio, all on the same audio thread with
no locks. It's a cross-track read, not a routing send. (See
[creating-effects.md §8](creating-effects.md) and
[`SidechainBus.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Effects/SidechainBus.cs).)

### Multi-output instruments

Some hosted plugins (notably multi-out VST3 instruments) expose more than one stereo bus. Instruments
implementing [`IMultiOutputInstrument`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Instruments/IMultiOutputInstrument.cs) render
the main bus into the track as usual and deliver auxiliary buses through a callback. The engine routes
each extra bus to a destination track configured in the project (`MultiOutputRoute` entries, keyed by
source track + plugin bus index). After all tracks render in parallel, [`FlushMultiOutputRoutes`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/AudioEngine.cs)
adds the pending auxiliary audio into the destination track buffers before the mixdown phase — so a
drum plugin's overhead mics can land on separate tracks with their own FX and sends.

---

## 4. Playback schedulers

Clip and note timing is not hard-coded into `Render`. Instead, each block builds an immutable
[`PlaybackSchedule`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Scheduling/IPlaybackScheduler.cs) snapshot via a pluggable
scheduler, chosen from the current [`PlaybackMode`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Services/Interfaces/IPlaybackModeService.cs):

| Mode | Scheduler | What plays |
| --- | --- | --- |
| **Arrangement** | [`ArrangementScheduler`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Scheduling/ArrangementScheduler.cs) | Timeline clips only — MIDI notes and audio clips on the arrange view |
| **Session** | [`SessionScheduler`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Scheduling/SessionScheduler.cs) | Launched session clips only (no timeline) |
| **Hybrid** | [`HybridScheduler`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Scheduling/SessionScheduler.cs) | Arrangement timeline **plus** any live session overdubs merged together |

In **Arrangement** and **Hybrid** modes, a second pass from [`PatternScheduler`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Scheduling/PatternScheduler.cs)
is merged on top. Pattern clips on the timeline reference shared [`Pattern`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Models/Audio/Pattern.cs)
objects; the scheduler expands step-sequencer data into scheduled MIDI notes on the pattern's target
instrument tracks (respecting mutes and repeat length).

```490:500:Ongenet.Core/Audio/AudioEngine.cs
    private PlaybackSchedule BuildSchedule(PlaybackScheduleContext context)
    {
        var sessionClips = context.Project.SessionClips;
        var launches = _playback.ActiveLaunches;

        return _playback.Mode switch
        {
            PlaybackMode.Session => new SessionScheduler(sessionClips, launches).Build(context),
            PlaybackMode.Hybrid => BuildHybridSchedule(context, sessionClips, launches),
            _ => MergeWithPatterns(_arrangementScheduler.Build(context), context)
        };
    }
```

`MergeWithPatterns` / `BuildHybridSchedule` blend arrangement, session, and pattern scheduler output (pattern expansion is skipped in Session-only mode).

Session clips carry launch modes (Trigger, Gate, Toggle, Repeat) and can reference a source arrangement
clip by id. Launches are quantised to the beat grid via `IPlaybackModeService.LaunchQuantizeBeats`.

---

## 5. Warp maps and tempo-stretched audio

Simple loops use uniform time-stretch (`StretchToTempo`). Clips with **warp markers** get a piecewise-linear
[`WarpMap`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/WarpMap.cs) that maps clip-local beat positions to source-file seconds.
The map is built from explicit markers plus implicit endpoints at the clip start/end:

```30:58:Ongenet.Core/Audio/WarpMap.cs
    public static WarpMap FromClip(Clip clip, double sourceEndSeconds)
    {
        ...
        foreach (var wm in clip.WarpMarkers)
            list.Add((wm.BeatPosition, wm.SourceSeconds));
        ...
    }
```

During playback [`Mixing.RenderWarpedAudioClip`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Mixing.cs) walks the map segment by
segment, applying the local source↔beat ratio (and optional pitch-corrected stretch via Rubber Band) so
free-form tempo ramps and DJ-style marker edits stay sample-accurate on the audio thread.

---

## 6. Real-time safety (the rules, and why)

The audio callback runs under a hard deadline on a high-priority thread. Three things will make it miss
that deadline and produce audible glitches:

1. **Allocating memory.** A `new` array, LINQ, boxing, or string work can trigger the .NET garbage
   collector, which can pause your thread for milliseconds — an eternity in audio time. **Rule: never
   allocate on the audio thread.** Pre-allocate everything in constructors or `Prepare`. (The engine
   itself only grows its scratch buffers when a larger block arrives, then never again.)
2. **Locking.** If the audio thread waits on a lock held by the UI thread, it stalls. **Rule: don't take
   contended locks in `Render`/`Process`.**
3. **Slow or unbounded work.** File I/O, network, decoding — none of it belongs on the audio thread.

So how does the UI safely change things the audio thread is using (add a track, tweak an effect chain,
move a note)? With **immutable snapshots published atomically.** The UI builds a new array off-thread,
then swaps a single `volatile` reference. The audio thread always reads one consistent snapshot:

```73:75:Ongenet.Core/Audio/AudioEngine.cs
    // --- Bus routing (groups + master). Rebuilt whenever the track topology changes; published as a
    //     single immutable snapshot so the audio thread always reads a consistent graph. ---
    private volatile Routing _routing = new();
```

The same pattern appears as `ActiveInstruments`, `ActiveEffects`, `ActiveAutoLanes` on tracks (with
`Commit…()` methods the UI calls to publish changes). Other real-time-safety techniques in the codebase:

- **MIDI from other threads** goes through a `MidiEventFifo` — pushed by the UI/MIDI thread, drained at
  the start of `Process` (see [creating-effects.md §8](creating-effects.md)).
- **Parameter smoothing** with `OnePole` filters avoids "zipper" clicks when a value jumps.
- **Voices never allocate** while rendering; they're pooled and reused.

### Which thread does what

| Thread | Responsibility |
| --- | --- |
| **OS audio thread** | Calls `AudioEngine.Render` each block; runs all instrument/effect DSP and the mix |
| **UI thread** | Edits the project; publishes snapshots via `Commit…()`; reads meters/playhead |
| **MIDI backend thread** | Delivers live hardware MIDI to the preview service |

There's a subtlety worth knowing: **live** notes (playing your keyboard) reach instruments on the
preview path immediately, while **sequenced** notes (clips during playback) are fired on the audio
thread by the engine's scheduler (see §4). Both end up calling the same `NoteOn`/`NoteOff` on your instrument.

---

## 7. The device layer: one seam, many backends

The engine depends only on `IAudioOutput`. Everything OS-specific is hidden behind it:

```17:36:Ongenet.Core/Audio/IAudioOutput.cs
public interface IAudioOutput : IDisposable
{
    /// <summary>The format the device runs at (known after <see cref="Start"/>).</summary>
    AudioFormat Format { get; }

    event Action? FormatChanged;

    /// <summary>Whether the device is currently open and streaming.</summary>
    bool IsRunning { get; }

    /// <summary>Opens the device and begins streaming, pulling blocks via <paramref name="callback"/>.</summary>
    void Start(AudioRenderCallback callback);

    /// <summary>Stops streaming and closes the device.</summary>
    void Stop();
}
```

The concrete backend is chosen at startup by the platform layer
([`DesktopPlatform`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Desktop/DesktopPlatform.cs)): a `LinuxNativeBackend`, `MacNativeBackend`,
or `WinNativeBackend`. The engine never sees the difference — `Start(Render)` is all it does, and blocks
start arriving.

Everything here uses **native OS libraries via P/Invoke** — there is nothing to compile or install. Each
driver simply probes for its system library at runtime and, if present, offers its devices.

---

## 8. Linux: four drivers in parallel

Linux audio is fragmented, so Ongenet ships **four independent drivers** and exposes all of them at
once. Each implements a small internal interface:

```12:38:Ongenet.Audio/Native/INativeAudioDriver.cs
internal interface INativeAudioDriver
{
    /// <summary>Display tag shown in device names, e.g. "ALSA".</summary>
    string HostApi { get; }

    /// <summary>Prefix this driver puts on <see cref="AudioDevice.Id"/>, e.g. "alsa:". Used to route opens.</summary>
    string IdPrefix { get; }

    /// <summary>Whether the subsystem's native library is present and usable on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>Appends this driver's playback/capture devices to the given lists.</summary>
    void Enumerate(List<AudioDevice> outputs, List<AudioDevice> inputs);

    INativeStream OpenOutput(AudioDevice device, int channels, AudioRenderCallback render);

    INativeStream OpenInput(AudioDevice device, int channels, AudioCaptureCallback capture);
}
```

They are registered in preference order:

```20:26:Ongenet.Audio/Native/NativeDriverRegistry.cs
        _drivers = new List<INativeAudioDriver>
        {
            new PipeWireAudioDriver(),
            new PulseAudioDriver(),
            new JackAudioDriver(),
            new AlsaAudioDriver(),
        };
```

Only the **available** ones (whose native library loads) appear; each one enumerates its own devices and
tags them with a host-API name and an id prefix, so all working subsystems show up together in the
device picker and opens are routed back to the right driver.

| Driver | Native library | Id prefix | How it streams |
| --- | --- | --- | --- |
| **PipeWire** ([`PipeWireAudioDriver`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Audio/Native/PipeWireAudioDriver.cs)) | `libpipewire-0.3.so.0` | `pw:` | A `pw_stream` on a `pw_thread_loop`; float32, 48 kHz; the engine renders into the SPA buffer in the process callback |
| **PulseAudio** ([`PulseAudioDriver`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Audio/Native/PulseAudioDriver.cs)) | `libpulse.so.0` / `libpulse-simple.so.0` | `pulse:` | `pa_simple` with `FLOAT32LE`, 48 kHz, on a dedicated thread, 512-frame blocks |
| **JACK** ([`JackAudioDriver`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Audio/Native/JackAudioDriver.cs)) | `libjack.so.0` | `jack:` | A JACK process callback; ports are non-interleaved mono float, de/interleaved at the edge |
| **ALSA** ([`AlsaAudioDriver`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Audio/Native/AlsaAudioDriver.cs)) | `libasound.so.2` | `alsa:` | `snd_pcm` in `FLOAT_LE` interleaved access, ~48 kHz, ~256-frame period, dedicated thread |

The interop declarations (the actual `[DllImport]`/`NativeLibrary.TryLoad` calls) live in
[`Ongenet.Audio/Interop`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Audio/Interop). A driver reports `IsAvailable = false` if its
library can't be loaded, so a machine without JACK simply won't list JACK — nothing breaks.

> **Why so many?** On a typical modern desktop, PipeWire is present and preferred. But ALSA is always
> there as the lowest common denominator, PulseAudio covers older setups, and JACK serves pro-audio
> users. Listing them all lets the user pick the path with the latency/routing they want.

---

## 9. macOS & Windows

The same `IAudioOutput` seam, one backend each:

| OS | Backend | API | Files |
| --- | --- | --- | --- |
| **macOS** | `MacNativeBackend` | CoreAudio — a HAL output AudioUnit, float32 interleaved 48 kHz, render callback → engine | [`Native/Mac`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Audio/Native/Mac), [`Interop/CoreAudioNative.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Audio/Interop/CoreAudioNative.cs) |
| **Windows** | `WinNativeBackend` | WASAPI shared-mode, event-driven, device mix format (float32 interleaved), dedicated render thread | [`Native/Win`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Audio/Native/Win), [`Interop/WasapiNative.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Audio/Interop/WasapiNative.cs) |

In both cases the pattern is identical: the OS hands the driver a buffer (CoreAudio's render callback,
WASAPI's `IAudioRenderClient` buffer), the driver wraps it as a `Span<float>`, and calls the engine's
`render(span)`.

---

## 10. Picking a device at startup

The platform registers the one native backend for the OS; the
[`AudioBackendManager`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/AudioBackendManager.cs) selects it (the one with
`Id == "native"`) and hands its render/capture callbacks straight to the active backend's stream — no
extra indirection on the audio path.

For the actual *device*, the native device service aggregates every available driver's devices and then
**reconciles** a default: keep the previously selected device if it's still present, otherwise prefer
whatever is tagged as the system default, otherwise the first device. The user's saved preference (from
**Settings → Audio**) is applied before the engine starts, and changing the selection while running
simply reopens the stream.

---

## 11. MIDI input (briefly)

MIDI has the same shape: one seam, an OS-native implementation chosen at startup
([`MidiInputBackendFactory`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Audio/Interop/MidiInputBackendFactory.cs)):

| OS | Backend |
| --- | --- |
| **Linux** | ALSA sequencer (`snd_seq`), falling back to ALSA rawmidi |
| **Windows** | WinMM |
| **macOS** | CoreMIDI |

Incoming messages are delivered on the MIDI backend's thread to the preview service, which plays them on
the selected track's instrument. (Clip playback, by contrast, generates MIDI on the audio thread — see
§4.) Live MIDI and the on-screen/computer keyboard all funnel into the same `NoteOn`/`NoteOff` calls your
instrument already implements.

---

## 12. Mental recap for DSP newcomers

1. **The sound card pulls.** The engine fills a buffer on demand; it never decides when it runs.
2. **One format everywhere:** float32, interleaved, at the device sample rate. Read the rate; don't
   assume it.
3. **Block size varies** — never assume a fixed number of frames.
4. **Mixing is additive** into a pre-cleared buffer: instruments add, effects edit in place.
5. **Routing is a tree plus sends:** track → parent group bus → master; parallel **return** buses are
   fed by pre/post-fader sends. Sidechain is a cross-track read, not a send.
6. **PDC aligns paths:** latency-reporting plugins get delay-compensated so everything meets at the master
   in phase.
7. **Schedulers build the clip list:** arrangement, session, hybrid and pattern schedulers produce the
   note/audio schedule each block; live MIDI still uses the preview path.
8. **Warp maps** stretch audio non-uniformly when a clip has beat↔source markers.
9. **Multi-out plugins** can route extra buses to other tracks via `MultiOutputRoute`.
10. **The audio thread is sacred:** no allocations, no locks, no slow work. The UI talks to it via
   immutable snapshots and lock-free FIFOs.
11. **The OS is hidden** behind `IAudioOutput`; Linux exposes PipeWire/PulseAudio/JACK/ALSA in parallel,
   macOS uses CoreAudio, Windows uses **WASAPI** (default) and **ASIO** (registry-enumerated drivers in
   Settings → Audio) — all via P/Invoke, nothing to install.

---

## 13. Immersive export (ADM BWF)

For **5.1 / 7.1** surround exports, the **Export** dialog can write the multichannel BWF audio and an
ADM XML sidecar for ITU-R BS.2076 handoff. ADM XML is currently sidecar-only rather than embedded in
the BWF. The master mix uses the same `OfflineRenderer` path as WAV export.

**AAF/OMF handoff** exports structured timeline XML for Nuendo/Pro Tools pipelines.

---

## 14. Hybrid track scheduling

`TrackKind.Hybrid` lanes accept both audio and MIDI clips. The arrangement scheduler emits instrument
notes from MIDI clips and schedules audio clips on the same track — session/hybrid playback modes merge
pattern and launcher schedules unchanged.

---

## Key files

| File | Role |
| --- | --- |
| [`Audio/AudioEngine.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/AudioEngine.cs) | The render loop, scheduling, automation, routing, sends, PDC, master mix |
| [`Audio/SendMixing.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/SendMixing.cs) | Pre/post-fader aux send accumulation |
| [`Audio/LatencyCompensator.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/LatencyCompensator.cs) | Per-track/bus PDC delay computation |
| [`Audio/Scheduling/`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Scheduling) | Arrangement, session, hybrid and pattern schedulers |
| [`Audio/WarpMap.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/WarpMap.cs) | Beat↔source warp marker mapping |
| [`Audio/MultiOutputRouter.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/MultiOutputRouter.cs) | Plugin multi-out bus routing |
| [`Audio/IAudioOutput.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/IAudioOutput.cs) | The device seam + render callback |
| [`Audio/Mixing.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Mixing.cs) | Volume/pan gain laws and buffer summing |
| [`Audio/AudioBackendManager.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/AudioBackendManager.cs) | Selects the active backend |
| [`Models/Audio/Track.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Models/Audio/Track.cs) / [`TrackKind.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Models/Audio/TrackKind.cs) | Track model + parent routing |
| [`Audio.Native/INativeAudioDriver.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Audio/Native/INativeAudioDriver.cs) / [`NativeDriverRegistry.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Audio/Native/NativeDriverRegistry.cs) | Linux driver seam + registry |
| [`Audio/Files/AdmBwfExporter.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Files/AdmBwfExporter.cs) | ADM BWF immersive export (ITU-R BS.2076) |
| [`Audio.Native/Win/AsioDeviceService.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Audio/Native/Win/AsioDeviceService.cs) | Windows ASIO driver enumeration |

---

## Where to go next

- [creating-instruments.md](creating-instruments.md) — write a source that the engine renders each block.
- [creating-effects.md](creating-effects.md) — write an effect the engine inserts into the signal flow.
- [main-window-layout.md](main-window-layout.md) — the meters, transport and mixer UI over this engine.
