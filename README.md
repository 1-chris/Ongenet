# Ongenet

A Digital Audio Workstation (DAW) built on Avalonia, with a clean native-free Core and a thin,
swappable device/UI layer around it.

## Projects

| Project | Native deps | Description |
| --- | --- | --- |
| `Ongenet.Core` | none | The heart of the app, fully platform-agnostic. Audio models (project / tracks / clips / MIDI notes), the lock-free audio **engine** (sequencer, per-track mixing, metering, automation), the **instrument** framework (Oscillator, 3x Osc, FM, Basic Sampler, Granular, Padda, Kicka, SFZ Sampler) and **effects** chain (filter, EQ, dynamics, modulation, delay/reverb…), the shared DSP toolkit (`Audio/Dsp`), the parameter framework, WAV decode/encode, a cross-platform **MIDI** model (running-status parser, learn/transport mappings), and the app services (project, transport, selection, recording, edit-mode, MIDI input/mapping) plus DI registration, an in-process event aggregator, and logging. Depends only on the BCL. |
| `Ongenet.App` | Avalonia | The **shared UI library** used by every head (desktop / web / Android): the `App` composition root + DI, all views & view-models, custom controls, the Catppuccin **theming** system, arrange/timeline, piano roll, inspectors, mixer/meters, editable automation lanes, the unified **Settings** window, a debug Log window, and the embeddable **3D controls**. Each head injects its platform pieces (audio backend, MIDI, plugins, GPU engine, shell) through `IPlatformServices`. |
| `Ongenet.Engine3D.Abstractions` | none | Portable, dependency-free **3D scene model** (meshes, materials, orbit camera, lights, the immutable per-frame `SceneSnapshot`) plus the engine contracts (`I3DEngineFactory` / `I3DRenderSession`). Referenced by both the UI and the native engine, so the UI never touches GPU code and the engine never touches Avalonia. BCL only. |
| `Ongenet.Engine3D` | Vulkan / MoltenVK | The **native GPU 3D engine** behind Ongenet's embeddable 3D controls. A hand-written Render Hardware Interface (RHI) over **Silk.NET**, with a **Vulkan** backend that renders scenes offscreen — native on Windows/Linux and on macOS via **MoltenVK** (bundled; no Vulkan SDK needed). Desktop-only; injected into the shared UI via DI, so the web/Android heads never pull native GPU code. |
| `Ongenet.Audio` | OS audio + MIDI | The audio **and MIDI** device backend. P/Invoke layers over each platform's native **audio** API — ALSA (with PipeWire/JACK/PulseAudio routing) on Linux, **CoreAudio** on macOS, **WASAPI** on Windows — and each platform's native **MIDI** API — the **ALSA sequencer** on Linux (works with PipeWire/JACK), **WinMM** on Windows and **CoreMIDI** on macOS, behind single `IAudioBackend` / `IMidiInputBackend` seams. This is the only project that touches native audio/MIDI libraries; Core depends solely on the device seams, so the backend is swappable. |
| `Ongenet.Clap` | CLAP plugins | CLAP plugin hosting: a direct interop over the [CLAP](https://cleveraudio.org/) ABI that scans for, loads, and bridges third-party `.clap` instruments and effects (incl. their plugin GUIs) into Core's instrument/effect registries. Plugins are discovered at runtime; none are required to run the app. |
| `Ongenet.Lv2` | LV2 plugins | LV2 plugin hosting, written from scratch over the [LV2](https://lv2plug.in/) ABI — no `lilv`/`suil`. A hand-written **Turtle/RDF** parser discovers `.lv2` bundles; audio runs through the port-based `connect_port`/`run` model (control ports become automatable parameters, MIDI is delivered via an LV2 Atom sequence), with the **URID-map**, **Options** and **Worker** host features and native **X11 plugin-UI** embedding. Instruments and effects are bridged into Core's registries; discovered at runtime, none required. |
| `Ongenet.Vst` | VST2 + VST3 plugins | VST2 **and** VST3 plugin hosting, both written from scratch over the public ABIs — no Steinberg SDK or wrapper libraries. **VST2** drives the flat `AEffect` dispatcher (params, `processReplacing`, `effProcessEvents` MIDI, `effEditOpen` GUI) with a full `audioMaster` host callback. **VST3** implements the COM-style `IPluginFactory` → `IComponent`/`IAudioProcessor`/`IEditController` model with host-side `IComponentHandler`/`IHostApplication`/`IPlugFrame`, `process()` over `ProcessData`, note/parameter input via `IEventList`/`IParameterChanges`, and the `IPlugView` editor. Cross-platform (Windows/macOS/Linux, x64 + arm64), with native X11 GUI embedding on Linux. Discovered at runtime; none required. |
| `Ongenet.Desktop` | Avalonia (+ all native) | The **desktop head**: a thin exe that wires the native stack — Avalonia desktop backends, `Ongenet.Audio`, the CLAP/LV2/VST plugin hosts, and the `Ongenet.Engine3D` GPU engine — into the shared `Ongenet.App` UI via `DesktopPlatform`. Hand-rolled MVVM, DI bootstrap, the classic `MainWindow`. Publishes as `Ongenet`. |
| `Ongenet.Web` | none (browser) | The **browser / WebAssembly head** (`net10.0-browser`): reuses `Ongenet.App` + `Ongenet.Core` with a Web Audio backend and browser-safe stubs. A demo build deployed to GitHub Pages (no native audio/plugin/GPU projects). |
| `Ongenet.Android` | AAudio | The **Android (tablet) head** (`net10.0-android`): reuses the shared UI + portable engine with a native **AAudio** backend, shown in the same single-view shell as the web head. Sideloaded APK. |

All projects target **.NET 10** (the browser head `net10.0-browser`, the Android head `net10.0-android`).

## Instruments

Eight built-in instruments ship in `Ongenet.Core` (`Audio/Instruments`), all registered in the
`InstrumentRegistry`. Any CLAP, LV2, VST2 or VST3 instrument you have installed is discovered at
runtime and appears alongside them.

| Instrument | Description |
| --- | --- |
| **Oscillator** | Polyphonic single-oscillator synth — one waveform per voice, shaped by an ADSR envelope. |
| **3x Osc** | Triple-oscillator subtractive synth in the spirit of FL Studio's 3x Osc: three oscillators, each with its own waveform, coarse/fine tuning and phase offset. |
| **FM Synth** | Two-operator FM — a sine carrier phase-modulated by a sine modulator (adjustable ratio and index), ADSR-shaped. |
| **Basic Sampler** | Plays one loaded audio sample pitched across the keyboard (resampled around C4) with an attack/release envelope. |
| **Granular** | Granular synth that continuously spawns short, overlapping windowed grains from a moving playhead over a loaded source sample. |
| **Padda** | Lush pad synth: two unison oscillator layers plus a sine sub and noise feed a modulated resonant filter, then an internal drive → chorus → delay → reverb chain. Ships an init patch and five presets; a loaded sample becomes a "Custom" waveform. |
| **Kicka** | Kick-drum synth spanning drumkit, trance, EDM, hardcore and hardstyle (plus Zaag/Piep variations). Each one-shot splits into a transient "tok" and a pitch-swept, distortion-stacked tail over a clean parallel sub, with the low end kept mono. The inspector preview matches playback. |
| **Sampler (SFZ)** | Multi-sample SFZ instrument: parses an `.sfz` patch and maps notes/velocities to regions (velocity layers + round-robin), each played through its own voice with envelopes and a filter. A few global macros are exposed as automatable parameters. |

## Effects

Twenty built-in effects ship in `Ongenet.Core` (`Audio/Effects`), registered in the `EffectRegistry`
and grouped by category. CLAP, LV2, VST2 and VST3 effects are likewise discovered at runtime and slot into the
same chain.

| Category | Effects |
| --- | --- |
| **EQ & Filter** | EQ, Filter |
| **Dynamics** | Compressor, Limiter, Gate, Sidechain |
| **Modulation** | Chorus, Phaser, Flanger, Tremolo, **Stuttero** |
| **Delay & Reverb** | Delay, Reverb |
| **Distortion** | Distortion, Bitcrusher |
| **Pitch** | Vocoder, Auto-Tune |
| **Utility** | Stereo Width, Utility |
| **Visualizer** | **3D Scope** |

**Stuttero** is our own Stutter Edit-style stutter / beat-repeat performance effect: it captures
incoming audio and chops it into tempo-synced slices (1/4 down to 1/512), shaped by a drawable
per-slice gate curve and a reorderable multi-FX rack (tape-stop, lo-fi, comb, phaser, chorus,
low-pass). "Gestures" bundle those settings with time-variant curves (stutter-rate sweep, filter
cutoff, per-module depth) and fire either from the transport (Auto) or from mapped MIDI keys (MIDI
mode), with a hold-to-freeze buffer.

**3D Scope** is a pass-through visualizer that shows off Ongenet's GPU 3D controls: it never alters the
audio, it only taps it and renders the live signal as a smoothed waveform in 3D — drawn at an angle, with
fading "snapshot" trails receding into the distance — at display refresh rate. Its colours follow the
active Catppuccin theme (and update live), and the whole visual can be popped out into a freely resizable
window.

## 3D engine

Ongenet ships a small, GPU-accelerated **3D engine** for hardware-rendered custom controls. It's a
hand-written **Vulkan** renderer (native on Windows/Linux, **MoltenVK** on macOS) behind a clean Render
Hardware Interface seam, with a portable scene model (`Ongenet.Engine3D.Abstractions`) and an embeddable
Avalonia control (`Engine3DView`) that composes with the rest of the UI like any other control. Visuals
are theme-aware and can be opened in resizable pop-out windows. The **3D Scope** effect is a worked demo
of an audio-modulated 3D visual — see the tutorial in
**[docs/creating-3d-visual-effects.md](docs/creating-3d-visual-effects.md)**. The engine is desktop-only
and degrades gracefully to a placeholder where no GPU is available (web/Android, or no Vulkan device).

## Plugins

Beyond the built-ins, Ongenet hosts third-party **CLAP**, **LV2**, **VST2** and **VST3** plugins. Every
format is implemented from scratch — direct ABI interop, **no wrapper libraries** (no `lilv`, `suil`,
CLAP helper libs or the Steinberg VST SDK) — and discovered at startup from the standard per-OS locations
(plus `CLAP_PATH` / `LV2_PATH` / `VST_PATH` / `VST3_PATH`). Instrument plugins appear in the Instruments
tab; audio-effect plugins appear under **Plugins** in the add-effect menu. Nothing is bundled and none
are required — with no plugins installed, the app runs exactly as before.

| Format | Notes |
| --- | --- |
| **CLAP** | Direct [CLAP](https://cleveraudio.org/) ABI interop: scans `.clap` modules, exposes their parameters, and bridges note/audio/parameter flow. |
| **LV2** | `.lv2` bundle discovery via a hand-written **Turtle/RDF** parser; the port-based `connect_port`/`run` model (control ports → automatable parameters, MIDI via an LV2 Atom sequence); the **URID-map**, **Options** and **Worker** host features, so sampler- and engine-class plugins (e.g. Cardinal / VCV Rack) load too. |
| **VST2** | The flat `AEffect` ABI: scans `.dll`/`.so`/`.vst` modules, drives `processReplacing`, sends notes via `effProcessEvents`, exposes normalised parameters, and opens the native editor via `effEditOpen` — backed by a full `audioMaster` host callback (time info, sample rate, can-do, size-window). |
| **VST3** | The COM-style ABI: `.vst3` bundle discovery via `IPluginFactory`, the `IComponent`/`IAudioProcessor`/`IEditController` model with component↔controller connection and state transfer, `process()` over `ProcessData`, notes via `IEventList` and parameter changes via `IParameterChanges`, and the `IPlugView` editor — with host-side `IComponentHandler`/`IHostApplication`/`IPlugFrame`. Cross-platform TUID byte layout and arch-specific bundle resolution (x64 / arm64). |

- **Native plugin GUIs** open in their own window (*Open plugin UI*). On Linux the UI is embedded into a
  GL-compatible X11 surface, so even heavyweight OpenGL UIs (Cardinal, Surge XT) render correctly.
- Plugin parameters are first-class: shown in the inspector, automatable, and bindable via **MIDI learn**.
- Plugins survive save/reload — a `.ongen` project re-creates them by stable id (the CLAP module/id, the
  LV2 plugin URI, the VST2 module + unique id, or the VST3 bundle + class id) as long as the plugin is
  still installed.

## MIDI

Full external MIDI controller support, with **no extra dependencies** — each platform's native MIDI API
is called directly via P/Invoke from `Ongenet.Audio`, behind a single `IMidiInputBackend` seam.

| Platform | Backend |
| --- | --- |
| **Linux** | ALSA **sequencer** (`snd_seq`) — sees hardware, software, PipeWire/JACK-bridged and Bluetooth (BLE) MIDI. Falls back to ALSA rawmidi if the sequencer is unavailable. |
| **Windows** | WinMM (`winmm.dll`) |
| **macOS** | CoreMIDI |

- **Play live** — any connected keyboard or pad controller plays the selected track's instrument with
  velocity; held notes light up the on-screen keyboard.
- **Record** — hardware performances are captured into MIDI clips with the same arm → count-in → record
  flow, with optional **input quantize** (1/4 … 1/32, including triplets).
- **MIDI learn** — right-click any knob, slider or switch and move a control on your device to bind a CC
  to it. Mappings are saved with the project (`.ongen`) and survive undo / redo.
- **Transport control** — map controller buttons or pads to play-pause, stop and record.
- **Expression** — pitch bend, mod wheel, sustain and aftertouch are passed through to the instrument.
- Device selection, mappings and a live input-activity readout live in the **Settings** window; the
  chosen device and other preferences persist to the standard per-OS config location
  (`%AppData%` on Windows, `~/Library/Application Support` on macOS, `$XDG_CONFIG_HOME`/`~/.config` on Linux).

## Building & running

Requires the **.NET 10 SDK**.

```bash
dotnet build Ongenet.sln
dotnet run --project Ongenet.Desktop
```

For development setup (audio notes), and for building self-contained, packaged releases for
Linux/Windows/macOS, see **[DEVELOPMENT.md](DEVELOPMENT.md)**.
