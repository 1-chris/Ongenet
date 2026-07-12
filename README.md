# Ongenet

A **free, open-source** Digital Audio Workstation (DAW) built on Avalonia, with a clean native-free
Core and a thin, swappable device/UI layer around it. Licensed under the [MIT License](LICENSE).

**Website:** [onge.net](https://onge.net/) &middot; **Downloads:**
[GitHub Releases](https://github.com/1-chris/Ongenet/releases) &middot; **Try in browser:**
[web demo](https://onge.net/app/) &middot; **Docs:**
[Guides](https://onge.net/articles/guides/) ·
[Dev tutorials](https://onge.net/dev/) ·
[API reference](https://onge.net/api/)

## Projects


| Project                         | Native deps             | Description                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| ------------------------------- | ----------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Ongenet.Core`                  | none                    | The heart of the app, fully platform-agnostic. Audio models (project / tracks / clips / MIDI notes), the lock-free audio **engine** (sequencer, per-track mixing, metering, automation), the **instrument** framework (Oscillator, 3x Osc, FM, Basic Sampler, Granular, Padda, Kicka, SFZ Sampler) and **effects** chain (filter, EQ, dynamics, modulation, delay/reverb…), the shared DSP toolkit (`Audio/Dsp`), the parameter framework, WAV decode/encode, a cross-platform **MIDI** model (running-status parser, learn/transport mappings), and the app services (project, transport, selection, recording, edit-mode, MIDI input/mapping) plus DI registration, an in-process event aggregator, and logging. Depends only on the BCL. |
| `Ongenet.App`                   | Avalonia                | The **shared UI library** used by every head (desktop / web / Android): the `App` composition root + DI, all views & view-models, custom controls, the Catppuccin **theming** system, arrange/timeline, piano roll, inspectors, mixer/meters, editable automation lanes, the unified **Settings** window, a debug Log window, and the embeddable **3D controls**. Each head injects its platform pieces (audio backend, MIDI, plugins, GPU engine, shell) through `IPlatformServices`.                                                                                                                                                                                                                                                      |
| `Ongenet.Engine3D.Abstractions` | none                    | Portable, dependency-free **3D scene model** (meshes, materials, orbit camera, lights, the immutable per-frame `SceneSnapshot`) plus the engine contracts (`I3DEngineFactory` / `I3DRenderSession`). Referenced by both the UI and the native engine, so the UI never touches GPU code and the engine never touches Avalonia. BCL only.                                                                                                                                                                                                                                                                                                                                                                                                     |
| `Ongenet.Engine3D`              | Vulkan / MoltenVK       | The **native GPU 3D engine** behind Ongenet's embeddable 3D controls. A Render Hardware Interface (RHI) over **Silk.NET**, with a **Vulkan** backend that renders scenes offscreen — native on Windows/Linux and on macOS via **MoltenVK** (bundled; no Vulkan SDK needed). Desktop-only; injected into the shared UI via DI, so the web/Android heads never pull native GPU code.                                                                                                                                                                                                                                                                                                                                                          |
| `Ongenet.Audio`                 | OS audio + MIDI         | The audio **and MIDI** device backend. P/Invoke layers over each platform's native **audio** API — ALSA (with PipeWire/JACK/PulseAudio routing) on Linux, **CoreAudio** on macOS, **WASAPI** on Windows — and each platform's native **MIDI** API — the **ALSA sequencer** on Linux (works with PipeWire/JACK), **WinMM** on Windows and **CoreMIDI** on macOS, behind single `IAudioBackend` / `IMidiInputBackend` seams. This is the only project that touches native audio/MIDI libraries; Core depends solely on the device seams, so the backend is swappable.                                                                                                                                                                         |
| `Ongenet.Clap`                  | CLAP plugins            | CLAP plugin hosting: a direct interop over the [CLAP](https://cleveraudio.org/) ABI that scans for, loads, and bridges third-party `.clap` instruments and effects (incl. their plugin GUIs) into Core's instrument/effect registries. Plugins are discovered at runtime; none are required to run the app.                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `Ongenet.Lv2`                   | LV2 plugins             | LV2 plugin hosting, written from scratch over the [LV2](https://lv2plug.in/) ABI — no `lilv`/`suil`. A **Turtle/RDF** parser discovers `.lv2` bundles; audio runs through the port-based `connect_port`/`run` model (control ports become automatable parameters, MIDI is delivered via an LV2 Atom sequence), with the **URID-map**, **Options** and **Worker** host features and native **X11 plugin-UI** embedding. Instruments and effects are bridged into Core's registries; discovered at runtime, none required.                                                                                                                                                                                                                    |
| `Ongenet.Vst`                   | VST2 + VST3 plugins     | VST2 **and** VST3 plugin hosting, both written from scratch over the public ABIs — no Steinberg SDK or wrapper libraries. **VST2** drives the flat `AEffect` dispatcher (params, `processReplacing`, `effProcessEvents` MIDI, `effEditOpen` GUI) with a full `audioMaster` host callback. **VST3** implements the COM-style `IPluginFactory` → `IComponent`/`IAudioProcessor`/`IEditController` model with host-side `IComponentHandler`/`IHostApplication`/`IPlugFrame`, `process()` over `ProcessData`, note/parameter input via `IEventList`/`IParameterChanges`, and the `IPlugView` editor. Cross-platform (Windows/macOS/Linux, x64 + arm64), with native X11 GUI embedding on Linux. Optional out-of-process isolation for VST3 effects via `Ongenet.PluginHost`. Discovered at runtime; none required.           |
| `Ongenet.Scripting`             | Roslyn                  | C# scripting host — expanded `IScriptingApi` (project, tracks, devices, clips, automation, patterns, export) plus `ProjectScriptExporter` / `PresetScriptExporter` for portable C# codegen. Factory scripts under `Ongenet.App/Scripts/`; user scripts in `Documents/Ongenet/Scripts/`. Desktop-only. |
| `Ongenet.Scripting.Editor`      | Avalonia (desktop)      | Custom in-house script IDE control — syntax overlay, line gutter, completion/signature popups built on Avalonia primitives + Roslyn IDE services. Referenced by `Ongenet.Desktop` only.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| `Ongenet.PluginHost`            | VST3 (child process)    | Headless child executable for optional plugin crash isolation. Communicates with the desktop host over named pipes; loads VST3 effects out-of-process when **Settings → General → Plugins → Isolate plugins** is enabled.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| `Ongenet.Desktop`               | Avalonia (+ all native) | The **desktop head**: a thin exe that wires the native stack — Avalonia desktop backends, `Ongenet.Audio`, the CLAP/LV2/VST plugin hosts, `Ongenet.Scripting`, `Ongenet.PluginHost`, and the `Ongenet.Engine3D` GPU engine — into the shared `Ongenet.App` UI via `DesktopPlatform`. MVVM, DI bootstrap, the classic `MainWindow`. Publishes as `Ongenet`.                                                                                                                                                                                                                                                                                                                                                                                                                             |
| `Ongenet.Web`                   | none (browser)          | The **browser / WebAssembly head** (`net10.0-browser`): reuses `Ongenet.App` + `Ongenet.Core` with a Web Audio backend and browser-safe stubs. A demo build deployed to GitHub Pages (no native audio/plugin/GPU projects).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `Ongenet.Android`               | AAudio                  | The **Android (tablet) head** (`net10.0-android`): reuses the shared UI + portable engine with a native **AAudio** backend, shown in the same single-view shell as the web head. Sideloaded APK.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |


All projects target **.NET 10** (the browser head `net10.0-browser`, the Android head `net10.0-android`).

## Workspace views

Beyond the arrange timeline and piano roll, Ongenet ships several dedicated views for mixing and
pattern-based composition:


| View                 | Description                                                                                                                                                                                                                                                            |
| -------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Mixer**            | Full channel-strip mixer with per-track volume, pan, mute/solo, meters, aux sends, **input monitoring** (Off/Auto/On), and an editable **routing matrix**.                                                                                                                                                                  |
| **Session**          | Ableton-style clip launcher grid — trigger, gate, toggle or repeat clips while the transport rolls; supports Arrangement, Session-only and Hybrid playback modes.                                                                                                      |
| **Channel Rack**     | Pattern-channel overview for the step sequencer — mute channels, pick the active pattern and jump into note editing.                                                                                                                                                   |
| **Step Sequencer**   | Per-pattern step grid for drum and melodic programming; pattern clips on the timeline expand into MIDI at playback time.                                                                                                                                               |
| **Notation**         | Staff view from MIDI clips — MusicXML import/export, transpose, articulations/dynamics, chord symbols, and **PDF export**.                                                                                                                                              |
| **Export dialog**    | Offline bounce to WAV, FLAC, MP3, or OGG — master mix, per-track stems, beat region (including by arrangement marker), or 5.1/7.1 surround — faster than real time with automation, sends, PDC and effect tails honoured. Optional video mux when ffmpeg is installed. |
| **Tempo Map**        | View → Tempo Map — edit master-track tempo automation points at the playhead.                                                                                                                                                                                           |
| **Section Playlist** | View → Section Playlist — ordered arrangement-marker sections for song-structure playback.                                                                                                                                                                                |
| **Ableton Link**     | Optional tempo/phase sync with other Link-enabled apps; continuous beat-follow while playing when the native library is present (isolated in `Ongenet.Link`; degrades to a no-op stub otherwise).                                                                      |
| **MIDI output**      | Route instrument tracks to external hardware synths; optional **MIDI clock** output (Settings → Audio).                                                                                                                                                               |
| **Control surfaces** | Configurable MCU / Launchpad / HUI profiles in Settings (Settings → Control Surface) with transport feedback and learnable Push2/APC40 mixer mapping.                                                                                                                  |
| **Metronome**        | Standalone click toggle on the transport bar (independent of record count-in).                                                                                                                                                                                         |
| **Tap tempo**        | Transport-bar **Tap** button sets project BPM from your tap rhythm.                                                                                                                                                                                                    |
| **Audio Editor**     | Edison-class standalone multitrack sample editor — View → Audio Editor or clip context menu **Open in Audio Editor**. Full waveform editing (cut/copy/paste, spectral overlay, normalize, fades) shared with the Sample Inspector. |
| **Pitch Editor**     | Built-in VariAudio-class polyphonic pitch editing — analyze note segments, drag pitch per blob, real-time playback with crossfades. Clip context menu **Open Pitch Editor**. |
| **Scripting**        | Centre **Scripting** tab — in-app C# IDE, expanded `IScriptingApi` (tracks, devices, automation, patterns), **Export project/preset** as portable scripts, batch Run or Start live; optional pop-out window. |




## Feature matrix

Ongenet is a **broad, open-source desktop DAW**: arrangement + session + FL-style patterns, deep
mixing/routing, scratch-built CLAP/LV2/VST/AU hosting, Field modular patching, standalone audio
editing, polyphonic pitch editing, C# scripting, and optional plugin crash isolation. Strong for
beat-making, songwriting, vocal tuning, and hybrid live/arrange work.

### Comparison & scope (Ongenet terminology)

| Competitor term | Ongenet equivalent |
|---|---|
| Bitwig **Grid** / FL **Patcher** | **Field** (instrument + effect) |
| FL **Factory** / browser packs | **Library**: Projects tab + Inst/FX Presets (`.ongenpreset`) |
| Ableton **Session View** | **Session** tab + session clips |
| FL **Channel Rack / Patterns** | **Channel Rack** + pattern tracks/clips |
| **Render / bounce** | **Export dialog** + clip render + freeze |
| Ableton **Instrument/Drum Rack** | **Instrument rack** + drum pad grid + macros |

### Interchange & collaboration

**ADM BWF export** (ITU-R BS.2076) delivers immersive masters with open metadata for broadcast handoff.
Timeline **AAF/OMF XML** and custom timeline XML support post-house interchange. Collaboration uses
self-hosted versioned folder sync.


| Area                                        | Status                                                                                                               |
| ------------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| Arrange / mix / record / patterns / session | **Strong** — production-ready for beat-driven, songwriting, and hybrid live/arrange workflows                        |
| Plugin hosting (CLAP, LV2, VST2/VST3, AU)   | **Strong** — PDC, offline stems, native plugin editors; optional VST3 effect isolation via `Ongenet.PluginHost` |
| Scripting / automation                      | **Strong** — Roslyn C# scripts, full project metadata API, portable project/preset export, in-app IDE, live handlers |
| Field modular engine                        | **Strong** — Bitwig Grid-class patching as instrument and effect                                                   |
| Comping / take lanes                        | **Good** — multi-lane loop recording, toggle comp regions, crossfade flatten, warp-aware bake                        |
| Export delivery                             | **Strong** — WAV/FLAC/MP3/OGG, stems, surround (5.1/7.1), ADM BWF, AAF/OMF XML handoff, timeline XML |
| Instrument / drum rack                      | **Good** — macros + drum pad grid in track inspector |
| Chord track / expression maps               | **Good** — global harmony regions + VST expression map editor |
| Hybrid tracks                               | **Good** — audio + MIDI clips on one lane |
| Edison-class audio editor                   | **Strong** — standalone multitrack Audio Editor window + shared Sample Inspector waveform tools |
| Polyphonic pitch editing                    | **Strong** — built-in VariAudio-class note segments, analyze + edit + real-time playback |
| Control Room                                | **Good** — monitor/cue profiles in Settings |
| ARA / third-party pitch plugins             | **Partial** — optional ARA2 SDK seam (`ENABLE_ARA`); native pitch editor is the primary path |
| Notation / scoring                          | **Good** — staff view, tuplets/articulations/dynamics, chord symbols, MusicXML I/O, transpose, basic PDF export      |
| Post / video                                | **Good** — ffmpeg sync preview, in/out trim, optional muxed export                                                   |
| Surround monitoring                         | **Conditional** — immersive pan + offline 5.1/7.1 export; live monitoring requires 6/8-channel output device       |
| Hardware control surfaces                   | **Good** — MCU/HUI/Launchpad/Push2/APC40 profiles; learn UI for mixer CC mapping                                     |
| Collaboration                               | **Good** — folder sync + versioned push; self-hosted collab                                                   |
| Input monitoring / tap tempo / MIDI clock   | **Good** — per-track software input monitor (mixer), tap tempo, MTC/LTC, MIDI clock out to hardware                  |
| Routing matrix                              | **Good** — editable track output targets and send levels from the mixer routing matrix window                        |
| Accessibility                               | **Good** — screen-reader landmarks on transport, timeline, mixer, piano roll, session, Field, library, export       |
| Windows pro audio                           | **Good** — WASAPI exclusive low-latency; ASIO driver registry enumeration on Windows                          |
| Autosave / crash recovery                   | **Good** — periodic autosave backups + recovery prompt on launch (Settings → on by default)                        |
| Content library                             | **Good** — nine demo songs/templates + Field/FX presets via the Library sidebar                                      |


The right-hand library sidebar has two distinct content sources for getting started:


| Tab / location                            | What it is                                                                                                                          | Format                                                                                              |
| ----------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| **Projects**                              | Full demo songs and templates — First Light, Undertow, Ascension, Dust & Vinyl, House Starter, Static Bloom, Techno Starter, Trap Beat, Field Modular. | No files; built via `[BuiltInProjects](Ongenet.Core/Music/BuiltInProjects.cs)`                      |
| **Inst Presets / FX Presets / FX Chains** | Saved instrument, effect and chain presets (plus factory `.ongenpreset` files materialized on first run from built-in instruments). | `.ongenpreset` under the config folder via `[PresetLibrary](Ongenet.App/Services/PresetLibrary.cs)` |


Use **Projects** to explore finished songs, and **Inst/FX Presets** for drag-and-drop starting points — whether factory materialized on first run or presets you've saved yourself.

## Instruments

Eleven built-in instruments ship in `Ongenet.Core` (`Audio/Instruments`), all registered in the
`InstrumentRegistry`, plus the **Field** modular instrument registered at startup. Any CLAP, LV2, VST2 or VST3 instrument you have installed is discovered at
runtime and appears alongside them.


| Instrument        | Description                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| ----------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Oscillator**    | Polyphonic single-oscillator synth — one waveform per voice, shaped by an ADSR envelope.                                                                                                                                                                                                                                                                                                                                                                         |
| **3x Osc**        | Triple-oscillator subtractive synth in the spirit of FL Studio's 3x Osc: three oscillators, each with its own waveform, coarse/fine tuning and phase offset.                                                                                                                                                                                                                                                                                                     |
| **Wavetable**     | Wavetable synth with morphing wavetable playback and a 3D wavetable preview in the inspector.                                                                                                                                                                                                                                                                                                                                                                  |
| **FM Synth**      | Two-operator FM — a sine carrier phase-modulated by a sine modulator (adjustable ratio and index), ADSR-shaped.                                                                                                                                                                                                                                                                                                                                                  |
| **Basic Sampler** | Plays one loaded audio sample pitched across the keyboard (resampled around C4) with an attack/release envelope.                                                                                                                                                                                                                                                                                                                                                 |
| **Granular**      | Granular synth that continuously spawns short, overlapping windowed grains from a moving playhead over a loaded source sample.                                                                                                                                                                                                                                                                                                                                   |
| **Padda**         | Lush pad synth: two unison oscillator layers plus a sine sub and noise feed a modulated resonant filter, then an internal drive → chorus → delay → reverb chain. Ships an init patch and five presets; a loaded sample becomes a "Custom" waveform.                                                                                                                                                                                                              |
| **Kicka**         | Kick-drum synth spanning drumkit, trance, EDM, hardcore and hardstyle (plus Zaag/Piep variations). Each one-shot splits into a transient "tok" and a pitch-swept, distortion-stacked tail over a clean parallel sub, with the low end kept mono. The inspector preview matches playback.                                                                                                                                                                         |
| **Perca**         | Companion drum synth for claps, hi-hats and percussion one-shots — pairs with Kicka for full kit programming.                                                                                                                                                                                                                                                                                                                                                  |
| **Sampler (SFZ)** | Multi-sample SFZ instrument: parses an `.sfz` patch and maps notes/velocities to regions (velocity layers + round-robin), each played through its own voice with envelopes and a filter. A few global macros are exposed as automatable parameters.                                                                                                                                                                                                              |
| **Field**         | A modular node-graph instrument (in the spirit of Bitwig's Grid): patch oscillators, envelopes, filters, modulators and math nodes on a zoomable canvas to build any synth. Every knob has a modulation inlet, whole instruments/effects/plugins are available as module nodes, and built-in presets reconstruct every other instrument as an editable graph. Also available as an **effect**. See [docs/creating-field-nodes.md](docs/creating-field-nodes.md). |




## Effects

Twenty-four built-in effects ship in `Ongenet.Core` (`Audio/Effects`), registered in the `EffectRegistry`
and grouped by category, plus the **Field** modular effect registered at startup. CLAP, LV2, VST2 and VST3 effects are likewise discovered at runtime and slot into the
same chain.


| Category           | Effects                                                                         |
| ------------------ | ------------------------------------------------------------------------------- |
| **EQ & Filter**    | EQ, Mid/Side EQ, Filter                                                          |
| **Dynamics**       | Compressor, Multiband (OTT), Limiter, Gate, Sidechain                            |
| **Modulation**     | Chorus, Phaser, Flanger, Tremolo, **Stuttero**                                   |
| **Delay & Reverb** | Delay, Reverb                                                                    |
| **Distortion**     | Distortion, Clipper, Bitcrusher                                                  |
| **Pitch**          | Vocoder, Auto-Tune                                                               |
| **Utility**        | Stereo Width, Utility, Live Difference                                           |
| **Visualizer**     | **3D Scope**                                                                    |
| **Modular**        | **Field** (node-graph effect — the same modular engine as the Field instrument) |


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

## Clip rendering

Right-click any **MIDI clip**, **audio clip** or **group summary** on the timeline and choose
**Render clip to new track** to offline-bounce it through the full effect chain — instrument slot
pre-FX, track post-FX and ancestor group buses (master FX excluded) — with automation applied.
The result lands as a beat-aligned audio clip on a new track below the source, and a progress sweep
animates across the clip while rendering. Group summaries flatten every descendant track in the mix,
including nested group FX, onto a track outside the group.

## 3D engine

Ongenet ships a small, GPU-accelerated **3D engine** for hardware-rendered custom controls. It's a
**Vulkan** renderer (native on Windows/Linux, **MoltenVK** on macOS) behind a clean Render
Hardware Interface seam, with a portable scene model (`Ongenet.Engine3D.Abstractions`) and an embeddable
Avalonia control (`Engine3DView`) that composes with the rest of the UI like any other control. Visuals
are theme-aware and can be opened in resizable pop-out windows. The **3D Scope** effect is a worked demo
of an audio-modulated 3D visual — see the tutorial in
**[docs/creating-3d-visual-effects.md](docs/creating-3d-visual-effects.md)**. The engine is desktop-only
and degrades gracefully to a placeholder where no GPU is available (web/Android, or no Vulkan device).

## Plugins

Beyond the built-ins, Ongenet hosts third-party **CLAP**, **LV2**, **VST2**, **VST3**, and **AU** (macOS)
plugins. Every format is implemented from scratch — direct ABI interop, **no wrapper libraries** (no `lilv`, `suil`,
CLAP helper libs or the Steinberg VST SDK) — and discovered at startup from the standard per-OS locations
(plus `CLAP_PATH` / `LV2_PATH` / `VST_PATH` / `VST3_PATH`). Instrument plugins appear in the Instruments
tab; audio-effect plugins appear under **Plugins** in the add-effect menu. Nothing is bundled and none
are required — with no plugins installed, the app runs exactly as before.


| Format   | Notes                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **CLAP** | Direct [CLAP](https://cleveraudio.org/) ABI interop: scans `.clap` modules, exposes their parameters, and bridges note/audio/parameter flow.                                                                                                                                                                                                                                                                                                                                    |
| **LV2**  | `.lv2` bundle discovery via a **Turtle/RDF** parser; the port-based `connect_port`/`run` model (control ports → automatable parameters, MIDI via an LV2 Atom sequence); the **URID-map**, **Options** and **Worker** host features, so sampler- and engine-class plugins (e.g. Cardinal / VCV Rack) load too.                                                                                                                                                                   |
| **VST2** | The flat `AEffect` ABI: scans `.dll`/`.so`/`.vst` modules, drives `processReplacing`, sends notes via `effProcessEvents`, exposes normalised parameters, and opens the native editor via `effEditOpen` — backed by a full `audioMaster` host callback (time info, sample rate, can-do, size-window).                                                                                                                                                                            |
| **VST3** | The COM-style ABI: `.vst3` bundle discovery via `IPluginFactory`, the `IComponent`/`IAudioProcessor`/`IEditController` model with component↔controller connection and state transfer, `process()` over `ProcessData`, notes via `IEventList` and parameter changes via `IParameterChanges`, and the `IPlugView` editor — with host-side `IComponentHandler`/`IHostApplication`/`IPlugFrame`. Cross-platform TUID byte layout and arch-specific bundle resolution (x64 / arm64). |
| **AU**   | macOS **Audio Unit** hosting via the Component Manager — music devices as instruments, effects in the chain, native Cocoa editor embedding. |


- **Native plugin GUIs** open in their own window (*Open plugin UI*). On Linux the UI is embedded into a
GL-compatible X11 surface, so even heavyweight OpenGL UIs (Cardinal, Surge XT) render correctly.
- Plugin parameters are first-class: shown in the inspector, automatable, and bindable via **MIDI learn**.
- Plugins survive save/reload — a `.ongen` project re-creates them by stable id (the CLAP module/id, the
LV2 plugin URI, the VST2 module + unique id, or the VST3 bundle + class id) as long as the plugin is
still installed.



## MIDI

Full external MIDI controller support, with **no extra dependencies** — each platform's native MIDI API
is called directly via P/Invoke from `Ongenet.Audio`, behind a single `IMidiInputBackend` seam.


| Platform    | Backend                                                                                                                                                               |
| ----------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Linux**   | ALSA **sequencer** (`snd_seq`) — sees hardware, software, PipeWire/JACK-bridged and Bluetooth (BLE) MIDI. Falls back to ALSA rawmidi if the sequencer is unavailable. |
| **Windows** | WinMM (`winmm.dll`)                                                                                                                                                   |
| **macOS**   | CoreMIDI                                                                                                                                                              |


- **Play live** — any connected keyboard or pad controller plays the selected track's instrument with
velocity; held notes light up the on-screen keyboard.
- **Record** — hardware performances are captured into MIDI clips with the same arm → count-in → record
flow, with optional **input quantize** (1/4 … 1/32, including triplets).
- **MIDI learn** — right-click any knob, slider or switch and move a control on your device to bind a CC
to it. Mappings are saved with the project (`.ongen`) and survive undo / redo.
- **Transport control** — map controller buttons or pads to play-pause, stop and record.
- **MIDI clock output** — optional 24 ppqn clock to external hardware sequencers (Settings → Audio).
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

Deep-dive feature guides:

| Guide | Topic |
| --- | --- |
| [docs/scripting.md](docs/scripting.md) | C# scripting host (`IScriptingApi`, Roslyn, factory scripts) |
| [docs/audio-editor.md](docs/audio-editor.md) | Standalone multitrack Audio Editor |
| [docs/polyphonic-pitch.md](docs/polyphonic-pitch.md) | Built-in VariAudio-class pitch editor |
| [docs/plugin-isolation.md](docs/plugin-isolation.md) | Optional out-of-process VST3 effect sandbox |

## Acknowledgements

Some audio analysis and processing in Ongenet is inspired by well-known open-source projects.
Ongenet does not ship or link their libraries; the implementations are original C# in
`Ongenet.Core`.

- **[Rubber Band Library](https://breakfastquay.com/rubberband/)** (Particular Programs Ltd, GPL) —
duration-preserving sample pitch shift is a pure .NET port of Rubber Band's R2 offline engine
(study pass, adaptive chunk increments, laminar phase linking, Hann overlap-add, Hermite resample)
in `Ongenet.Core/Audio/Dsp/` — no native Rubber Band library is linked.
- **[Queen Mary qm-dsp](https://github.com/c4dm/qm-dsp)** (Centre for Digital Music, GPL) and
**[Mixxx](https://github.com/mixxxdj/mixxx)** (GPL) — musical key detection ports Mixxx's default
Queen Mary GetKeyMode pipeline (decimation, Constant-Q chromagram, HPCP averaging, Krumhansl
profiles, median filter) in `Ongenet.Core/Audio/Files/QueenMaryKeyDetector.cs`; tempo detection uses
the Queen Mary complex-domain onset function and `TempoTrackV2` beat tracker from Mixxx's default
beat analyzer (`Ongenet.Core/Audio/Files/QueenMaryTempoDetector.cs`).

Thank you to the authors and maintainers of these projects for publishing their work.