# Ongenet

A Digital Audio Workstation (DAW) built on Avalonia, with a clean native-free Core and a thin,
swappable device/UI layer around it.

## Projects

| Project | Native deps | Description |
| --- | --- | --- |
| `Ongenet.Core` | none | The heart of the app, fully platform-agnostic. Audio models (project / tracks / clips / MIDI notes), the lock-free audio **engine** (sequencer, per-track mixing, metering, automation), the **instrument** framework (Oscillator, 3x Osc, FM, Basic Sampler, Granular, Padda, Kicka, SFZ Sampler) and **effects** chain (filter, EQ, dynamics, modulation, delay/reverb…), the shared DSP toolkit (`Audio/Dsp`), the parameter framework, WAV decode/encode, a cross-platform **MIDI** model (running-status parser, learn/transport mappings), and the app services (project, transport, selection, recording, edit-mode, MIDI input/mapping) plus DI registration, an in-process event aggregator, and logging. Depends only on the BCL. |
| `Ongenet.Audio` | PortAudio | The audio **and MIDI** device backend. P/Invoke layers over **PortAudio** (`PortAudioOutput` for audio I/O) and each platform's native **MIDI** API — the **ALSA sequencer** on Linux (works with PipeWire/JACK), **WinMM** on Windows and **CoreMIDI** on macOS, behind a single `IMidiInputBackend` seam. This is the only project that touches native audio/MIDI libraries; Core depends solely on the device seams, so the backend is swappable. |
| `Ongenet.Clap` | CLAP plugins | CLAP plugin hosting: a direct interop over the [CLAP](https://cleveraudio.org/) ABI that scans for, loads, and bridges third-party `.clap` instruments and effects (incl. their plugin GUIs) into Core's instrument/effect registries. Plugins are discovered at runtime; none are required to run the app. |
| `Ongenet.Desktop` | Avalonia | The Avalonia desktop UI. Hand-rolled MVVM (`ViewModelBase` / `RelayCommand`), DI bootstrap in `App.axaml.cs`, a Catppuccin Mocha theme, the arrange/timeline view, piano roll, instrument & effect inspectors, mixer/meters, the editable automation lanes, a unified **Settings** window (audio / MIDI / theme), app-settings persistence, and a debug Log window. References all of the above. |

All projects target **.NET 10**.

## Instruments

Eight built-in instruments ship in `Ongenet.Core` (`Audio/Instruments`), all registered in the
`InstrumentRegistry`. Any CLAP instrument you have installed is discovered at runtime and appears
alongside them.

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

For development setup (audio/PortAudio notes), and for building self-contained, packaged releases for
Linux/Windows/macOS, see **[DEVELOPMENT.md](DEVELOPMENT.md)**.
