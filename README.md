# Ongenet

A Digital Audio Workstation (DAW) built on Avalonia, with a clean native-free Core and a thin,
swappable device/UI layer around it.

## Projects

| Project | Native deps | Description |
| --- | --- | --- |
| `Ongenet.Core` | none | The heart of the app, fully platform-agnostic. Audio models (project / tracks / clips / MIDI notes), the lock-free audio **engine** (sequencer, per-track mixing, metering, automation), the **instrument** framework (Oscillator, 3x Osc, FM, Sampler, Granular) and **effects** chain (filter, EQ, dynamics, modulation, delay/reverb…), the shared DSP toolkit (`Audio/Dsp`), the parameter framework, WAV decode/encode, and the app services (project, transport, selection, recording, edit-mode) plus DI registration, an in-process event aggregator, and logging. Depends only on the BCL. |
| `Ongenet.Audio` | PortAudio | The audio device backend. A P/Invoke layer over **PortAudio** (`PortAudioOutput` implementing Core's `IAudioOutput`). This is the only project that touches a native audio library; the engine itself depends solely on the `IAudioOutput` seam, so the device layer is swappable. |
| `Ongenet.Clap` | CLAP plugins | CLAP plugin hosting: a direct interop over the [CLAP](https://cleveraudio.org/) ABI that scans for, loads, and bridges third-party `.clap` instruments and effects (incl. their plugin GUIs) into Core's instrument/effect registries. Plugins are discovered at runtime; none are required to run the app. |
| `Ongenet.Desktop` | Avalonia | The Avalonia desktop UI. Hand-rolled MVVM (`ViewModelBase` / `RelayCommand`), DI bootstrap in `App.axaml.cs`, a Catppuccin Mocha theme, the arrange/timeline view, piano roll, instrument & effect inspectors, mixer/meters, the editable automation lanes, and a debug Log window. References all of the above. |

All projects target **.NET 10**.

## Instruments

Five built-in instruments ship in `Ongenet.Core` (`Audio/Instruments`), all registered in the
`InstrumentRegistry`. Any CLAP instrument you have installed is discovered at runtime and appears
alongside them.

| Instrument | Description |
| --- | --- |
| **Oscillator** | Polyphonic single-oscillator synth — one waveform per voice, shaped by an ADSR envelope. |
| **3x Osc** | Triple-oscillator subtractive synth in the spirit of FL Studio's 3x Osc: three oscillators, each with its own waveform, coarse/fine tuning and phase offset. |
| **FM Synth** | Two-operator FM — a sine carrier phase-modulated by a sine modulator (adjustable ratio and index), ADSR-shaped. |
| **Basic Sampler** | Plays one loaded audio sample pitched across the keyboard (resampled around C4) with an attack/release envelope. |
| **Granular** | Granular synth that continuously spawns short, overlapping windowed grains from a moving playhead over a loaded source sample. |

## Effects

Fifteen built-in effects ship in `Ongenet.Core` (`Audio/Effects`), registered in the `EffectRegistry`
and grouped by category. CLAP effects are likewise discovered at runtime and slot into the same chain.

| Category | Effects |
| --- | --- |
| **EQ & Filter** | EQ, Filter |
| **Dynamics** | Compressor, Limiter, Gate |
| **Modulation** | Chorus, Phaser, Flanger, Tremolo |
| **Delay & Reverb** | Delay, Reverb |
| **Distortion** | Distortion, Bitcrusher |
| **Utility** | Stereo Width, Utility |

## Building & running

Requires the **.NET 10 SDK**.

```bash
dotnet build Ongenet.sln
dotnet run --project Ongenet.Desktop
```

For development setup (audio/PortAudio notes), and for building self-contained, packaged releases for
Linux/Windows/macOS, see **[DEVELOPMENT.md](DEVELOPMENT.md)**.
