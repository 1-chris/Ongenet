# Developing & building Ongenet

See [README.md](README.md) for the project overview and what each subproject does, and
[docs/web-demo.md](docs/web-demo.md) for the WebAssembly head in depth.

This guide covers building, running and developing Ongenet on **Windows**, **Linux** and **macOS**.

The repository lives at **<https://github.com/1-chris/Ongenet>**.

---

## Tutorials

Deep-dive guides for extending and understanding Ongenet live in [`docs/`](docs/):

| Guide | What it covers |
| --- | --- |
| [Creating new instruments](docs/creating-instruments.md) | Build a synth or sampler from scratch — the voice model, audio buffers, reusing the DSP toolkit, parameters, and how it wires into the app. Written for DSP newcomers. |
| [Creating new effects](docs/creating-effects.md) | Build an audio effect — in-place processing, the DSP building blocks (filters, delay lines, envelope followers), parameters, and the advanced seams (tempo, sidechain, MIDI). Written for DSP newcomers. |
| [Main window layout & controls](docs/main-window-layout.md) | A tour of the UI: every region of the main window, the transport, timeline, piano roll, mixer, library and inspectors, plus the full keyboard-shortcut list. |
| [The theming system](docs/theming.md) | How live theming works: the semantic colour tokens, in-place brush mutation, `ThemedControl`, JSON import/export, and how to add tokens, themes and theme-aware controls. |
| [The audio engine & OS audio APIs](docs/audio-engine.md) | How the engine renders a block, the signal flow (instruments → effects → buses → master), real-time safety, and how the device layer hooks into PipeWire/PulseAudio/JACK/ALSA, CoreAudio and WASAPI. |

---

## 1. Prerequisites

| Tool | Needed for | Notes |
| --- | --- | --- |
| **.NET 10 SDK** | Everything | The single hard requirement. Every project targets `net10.0` (the browser head targets `net10.0-browser`). |
| `wasm-tools` workload | Building/running `Ongenet.Web` | One-time `dotnet workload install wasm-tools`. Not needed for the desktop app. |
| `zip` | Packaging releases | Used by `publish-desktop.sh`; it falls back to `tar.gz` if absent. |
| `ffmpeg` (runtime) | Importing non-WAV audio | The desktop app shells out to `ffmpeg` to transcode imported audio. Optional — WAV works without it. |

Everything else — the audio backend, MIDI, and all plugin hosting (CLAP/LV2/VST2/VST3) — is reached by
P/Invoke to libraries the OS already ships, so there is **nothing native to compile or install** for a
plain build and run.

### Installing the .NET 10 SDK

- **Windows** — `winget install Microsoft.DotNet.SDK.10`, the installer from
  <https://dotnet.microsoft.com/download/dotnet/10.0>, or Visual Studio 2022+ with the .NET workload.
- **macOS** — `brew install --cask dotnet-sdk`, or the pkg installer from the link above. Works on both
  Apple Silicon (arm64) and Intel (x64).
- **Linux** — your distro's package (`sudo dnf install dotnet-sdk-10.0` / `sudo apt install dotnet-sdk-10.0`),
  the official [install script](https://learn.microsoft.com/dotnet/core/install/linux-scripted-manual), or
  Microsoft's apt/yum feeds.

Verify with:

```bash
dotnet --version    # should print 10.x
```

### IDE (optional)

The project is developed in **JetBrains Rider** (an `Ongenet.sln` + `.idea/` are checked in), but nothing
is IDE-specific. Visual Studio 2022+, VS Code with the C# Dev Kit, or a plain terminal all work — the CLI
commands below are the source of truth.

---

## 2. Getting the source & first build

```bash
git clone https://github.com/1-chris/Ongenet
cd Ongenet
dotnet build Ongenet.sln       # restores NuGet packages and builds every project
```

The first build restores packages and may take a minute; subsequent builds are incremental.

---

## 3. Running the desktop app (Windows / Linux / macOS)

```bash
dotnet run --project Ongenet.Desktop
```

This is identical on all three platforms. `Ongenet.Desktop` is the desktop **head**: a thin exe that wires
the native stack (Avalonia desktop backends, `Ongenet.Audio`, and the CLAP/LV2/VST plugin hosts) into the
shared `Ongenet.App` UI library.

For a faster, optimized run:

```bash
dotnet run --project Ongenet.Desktop -c Release
```

> **Note:** in `Release` the desktop project is configured for self-contained single-file publish
> (`PublishSingleFile`/`SelfContained`). For day-to-day development use the default `Debug` configuration —
> it builds faster and enables the Avalonia DevTools (F12) for live visual-tree inspection.

### Audio during development

Audio runs on each OS's native backend via P/Invoke, so there is **nothing extra to install** for
`dotnet run`. If no device is available the app still launches — it runs silently and logs that fact.

| Platform | Audio backend | MIDI backend |
| --- | --- | --- |
| **Windows** | WASAPI (part of the OS) | WinMM (`winmm.dll`) |
| **macOS** | CoreAudio (part of the OS) | CoreMIDI |
| **Linux** | **Four separate native drivers** — PipeWire, PulseAudio, JACK and ALSA — each P/Invoking its own library directly. The app picks the best available at startup. | ALSA sequencer (`snd_seq`); sees hardware, software, PipeWire/JACK-bridged and Bluetooth MIDI. Falls back to ALSA rawmidi. |

On Linux the native backend is **not** just ALSA: it ships four independent drivers — `PipeWireAudioDriver`
(`libpipewire-0.3.so.0`), `PulseAudioDriver` (`libpulse.so.0` / `libpulse-simple.so.0`), `JackAudioDriver`
(`libjack.so.0`) and `AlsaAudioDriver` (`libasound.so.2`). Each probes for its native library at runtime,
enumerates its own devices, and opens float32-interleaved streams; whichever subsystems are present all
appear in the device picker. None is required — a missing library just means that driver contributes no
devices. See [docs/audio-engine.md](docs/audio-engine.md) for the full picture.

### Where settings & config live

Preferences (audio/MIDI device choice, theme, MIDI mappings, etc.) persist to the standard per-OS config
location:

- **Windows** — `%AppData%`
- **macOS** — `~/Library/Application Support`
- **Linux** — `$XDG_CONFIG_HOME` or `~/.config`

### Plugins (optional)

Third-party CLAP, LV2, VST2 and VST3 plugins are discovered at startup from the standard per-OS locations,
plus the `CLAP_PATH`, `LV2_PATH`, `VST_PATH` and `VST3_PATH` environment variables. Nothing is bundled and
none are required — with no plugins installed the app runs exactly the same.

---

## 4. Running the tests

```bash
dotnet test Ongenet.sln
```

Tests live in `Ongenet.Core.Tests` (xUnit) and cover the portable Core engine plus the LV2 host. They have
no audio-device or platform dependency, so they run anywhere the SDK does, including in CI.

---

## 5. The web (WebAssembly) head

`Ongenet.Web` compiles the same engine and UI to WebAssembly and runs in the browser. It's a demo build —
some desktop features are stubbed (see [docs/web-demo.md](docs/web-demo.md) for the full list). Audio uses
a Web Audio `ScriptProcessorNode`; there are no native/plugin projects referenced.

```bash
dotnet workload install wasm-tools             # one-time

# Run locally with the built-in WASM dev server (opens in your browser):
dotnet run --project Ongenet.Web

# Or produce the static bundle for hosting:
dotnet publish Ongenet.Web/Ongenet.Web.csproj -c Release
# Bundle: Ongenet.Web/bin/Release/net10.0-browser/browser-wasm/AppBundle/
```

The published `AppBundle/` can be served by any static server (e.g. `python3 -m http.server` from that
folder). The app is at `index.html` and uses `<base href="./">`, so it works from a sub-path too.

Deployment to GitHub Pages (`onge.net/app/`) is automated by `.github/workflows/deploy-web.yml` on push to
`main`. See [docs/web-demo.md](docs/web-demo.md) for the Pages setup and the architectural split.

---

## 6. Building distributable desktop packages

The repo includes a helper script at the solution root that produces **self-contained, single-file**
executables (the .NET runtime is bundled, so target machines need no .NET install — that's why each
executable is ~100 MB):

```bash
./publish-desktop.sh                 # all platforms → dist/Ongenet-<rid>.zip
./publish-desktop.sh linux-x64       # only the listed RID(s)
./publish-desktop.sh --symbols       # keep .pdb debug symbols (default strips them for smaller size)
./publish-desktop.sh --no-zip        # leave the publish folders, don't zip
```

It publishes `linux-x64`, `linux-arm64`, `win-x64`, `osx-arm64` and `osx-x64`. Because audio uses each
OS's native libraries via P/Invoke (PipeWire/PulseAudio/JACK/ALSA on Linux, CoreAudio on macOS, WASAPI on
Windows), **nothing native is compiled or bundled** — every target is a plain `dotnet publish`, so the
only toolchain requirement beyond the .NET SDK is `zip` (for packaging; the script falls back to `tar.gz`
otherwise).

> The script is a Bash script. On **Windows** run it from Git Bash / WSL, or invoke `dotnet publish`
> directly (see below).

### Cross-publishing

`dotnet publish` cross-publishes between platforms for pure-.NET targets — e.g. `win-x64` and `osx-x64`
are built on Linux/Apple-Silicon runners in CI. You can publish a single RID by hand without the script:

```bash
dotnet publish Ongenet.Desktop/Ongenet.Desktop.csproj -c Release --self-contained true -r linux-x64
```

### Run targets inside each package

- **Linux** (`linux-x64` / `linux-arm64`): `./Ongenet.bin` — the apphost is renamed from `Ongenet` so
  desktop environments don't mistake the name for a `.desktop` launcher.
- **Windows**: `Ongenet.exe`
- **macOS**: `./Ongenet`

`dist/` is a git-ignored build artifact.

---

## 7. Continuous integration & releases

| Workflow | Trigger | What it does |
| --- | --- | --- |
| `.github/workflows/desktop-build.yml` | push/PR to `main`, `v*` tags, manual | Builds self-contained packages for every RID via `publish-desktop.sh`, uploads them as artifacts, and — on a `v*` tag — attaches them to a GitHub Release. |
| `.github/workflows/deploy-web.yml` | push to `main`, manual | Installs `wasm-tools`, publishes `Ongenet.Web`, assembles the Pages site (homepage from `docs/` at `/`, app at `/app/`), and deploys to GitHub Pages. |

### Cutting a release

1. Bump `<Version>` in `Directory.Build.props` (the single source of truth — it flows into every
   assembly's version and is shown at runtime in the title bar).
2. Commit, tag `vX.Y.Z`, and push the tag. The desktop-build workflow publishes the GitHub Release with
   all platform packages attached.

---

## 8. Project layout (quick reference)

| Project | Targets | Role |
| --- | --- | --- |
| `Ongenet.Core` | `net10.0` | Portable engine, DSP, instruments, effects, persistence — no UI, BCL only. |
| `Ongenet.App` | `net10.0` | Shared Avalonia UI library used by both heads. |
| `Ongenet.Desktop` | `net10.0` | Desktop exe head (native audio/MIDI + plugins). Publishes as `Ongenet`. |
| `Ongenet.Web` | `net10.0-browser` | Browser exe head (Web Audio, browser-safe stubs). |
| `Ongenet.Audio` | `net10.0` | Native audio + MIDI backends (ALSA/CoreAudio/WASAPI; ALSA seq/WinMM/CoreMIDI). |
| `Ongenet.Clap` / `Ongenet.Lv2` / `Ongenet.Vst` | `net10.0` | Plugin hosting (CLAP / LV2 / VST2+VST3). |
| `Ongenet.Core.Tests` | `net10.0` | xUnit tests for Core + LV2. |

Each head plugs its platform pieces into the shared `App` through
`Ongenet.App.Platform.IPlatformServices` (`DesktopPlatform` / `WebPlatform`).
