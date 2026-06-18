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

## Building & running

Requires the **.NET 10 SDK**.

```bash
dotnet build Ongenet.sln
dotnet run --project Ongenet.Desktop
```

For development setup (audio/PortAudio notes), and for building self-contained, packaged releases for
Linux/Windows/macOS, see **[DEVELOPMENT.md](DEVELOPMENT.md)**.
