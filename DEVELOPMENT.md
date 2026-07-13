# Developing & building Ongenet

See [README.md](README.md) for the project overview and what each subproject does, and
[docs/web-demo.md](docs/web-demo.md) for the WebAssembly head in depth.

This guide covers building, running and developing Ongenet on **Windows**, **Linux** and **macOS**.

The repository lives at **<https://github.com/1-chris/Ongenet>**.

---

## Tutorials

Deep-dive guides for extending and understanding Ongenet live in [`docs/`](docs/) and are published as HTML on the website under [onge.net/dev/](https://onge.net/dev/):

| Guide | What it covers |
| --- | --- |
| [Creating new instruments](docs/creating-instruments.md) | Build a synth or sampler from scratch — the voice model, audio buffers, reusing the DSP toolkit, parameters, and how it wires into the app. Written for DSP newcomers. |
| [Creating new effects](docs/creating-effects.md) | Build an audio effect — in-place processing, the DSP building blocks (filters, delay lines, envelope followers), parameters, and the advanced seams (tempo, sidechain, MIDI). Written for DSP newcomers. |
| [The Field modular system & creating nodes](docs/creating-field-nodes.md) | How the Field node-graph instrument/effect works — signals, the voice/global partition, unified CV modulation, module wrappers, the editor UI — and how to add a new node type. |
| [Creating 3D visual effects](docs/creating-3d-visual-effects.md) | Embed a GPU-rendered, audio-modulated 3D visual in an effect — the 3D engine architecture, the portable scene model, tapping audio for the UI, building a reusable visualization, theme-awareness, and the pop-out window. Builds on the effects guide. |
| [Main window layout & controls](docs/main-window-layout.md) | A tour of the UI: every region of the main window, the transport, timeline, piano roll, mixer, library and inspectors, plus the full keyboard-shortcut list. |
| [The theming system](docs/theming.md) | How live theming works: the semantic colour tokens, in-place brush mutation, `ThemedControl`, JSON import/export, and how to add tokens, themes and theme-aware controls. |
| [The audio engine & OS audio APIs](docs/audio-engine.md) | How the engine renders a block, the signal flow (instruments → effects → buses → master), real-time safety, and how the device layer hooks into PipeWire/PulseAudio/JACK/ALSA, CoreAudio and WASAPI. |
| [Audio Editor](docs/audio-editor.md) | Standalone multitrack sample editor — waveform tools, lanes, relationship to Sample Inspector. |
| [Scripting](docs/scripting.md) | C# automation scripts, expanded `IScriptingApi`, portable project/preset export, factory scripts, security limits. |
| [Polyphonic pitch editing](docs/polyphonic-pitch.md) | Built-in VariAudio-class editor — analyze, edit segments, playback and flatten. |
| [Plugin crash isolation](docs/plugin-isolation.md) | Optional out-of-process VST3 effect hosting via `Ongenet.PluginHost`. |

---

## 1. Prerequisites

| Tool | Needed for | Notes |
| --- | --- | --- |
| **.NET 10 SDK** | Everything | The single hard requirement. Every project targets `net10.0` (the browser head targets `net10.0-browser`, the Android head `net10.0-android`). |
| `wasm-tools` workload | Building/running `Ongenet.Web` | One-time `dotnet workload install wasm-tools`. Not needed for the desktop app. |
| `android` workload + Android SDK + **JDK 21** | Building `Ongenet.Android` | One-time `dotnet workload install android`, then provision the SDK once and install a full JDK 21 — see [§6](#6-the-android-head-tablets). **Not** needed for the desktop or web heads. Android Studio is **not** required (we sideload an APK). |
| `zip` | Packaging releases | Used by `publish-desktop.sh`; it falls back to `tar.gz` if absent. |
| `ffmpeg` (runtime) | Importing non-WAV audio, stem separation (demucs path), video preview/mux/composite | The desktop app shells out to `ffmpeg` to transcode imported audio and for video frame decode, mux, and composited export. Optional — WAV works without it; video sync still tracks time without ffmpeg. |
| `demucs` (runtime) | High-quality 4-stem separation | Optional; without it, **Export → Separate stems** uses a built-in heuristic splitter. |
| ARA SDK + `ENABLE_ARA` | Melodyne-class ARA2 plugins | Optional; default build uses monophonic pitch offset + stub host. |
| Vulkan / MoltenVK | 3D Scope and Field visualizers | Optional; controls show placeholders when no GPU backend is available. |

### Automation design (v1)

Ongenet uses **automation lanes** under tracks (right-click a control → *Automate*), not Bitwig-style
**automation clips** as first-class timeline objects. Lanes record and playback curves during arrange
and offline export. Linked clip groups (`LinkedClipGroupId`) cover pattern reuse; alias-style editing is
via **Clip → Link clones** in the timeline context menu.

via **Clip → Link clones** in the timeline context menu.

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

### Avalonia UI conventions

- Use **`PlaceholderText`** on `TextBox` (and `ComboBox`) for hint text — **`Watermark` is obsolete** and
  triggers AVLN5001 build warnings. Grep for `Watermark=` before committing UI changes.

### Pattern tracks (FL-style)

1. Right-click the arrangement → **Add Pattern Track** (or use the track header context menu).
2. Add instrument tracks with your drum/synth sounds, then in **Track Controls** add them as pattern rows
   (or add audio samples as sampler-backed rows).
3. Double-click empty space on the pattern track lane to create a **pattern clip**.
4. Edit steps in the bottom **Pattern** tab; reorder rows in Track Controls — step data follows each row.

### Video composition (desktop)

Reference sync, overlays, and export are implemented in Core + App:

| Component | Role |
| --- | --- |
| [`ITempoMapService`](Ongenet.Core/Services/Interfaces/ITempoMapService.cs) / [`TempoMapService`](Ongenet.Core/Services/Implementation/TempoMapService.cs) | Beat ↔ wall-clock conversion for sync (honours tempo map) |
| [`VideoTriggerEngine`](Ongenet.Core/Services/VideoTriggerEngine.cs) | Evaluates clip/MIDI triggers; owns [`VideoCompositionRuntime`](Ongenet.Core/Services/VideoTriggerEngine.cs) opacity/fade state |
| [`FfmpegVideoCompositor`](Ongenet.Core/Audio/Files/FfmpegVideoCompositor.cs) | Offline composited MP4 (background + overlays via `filter_complex`) |
| [`FfmpegVideoMuxer`](Ongenet.Core/Audio/Files/FfmpegVideoMuxer.cs) | Mux bounced WAV with reference video |
| [`LiveVideoDecoder`](Ongenet.Core/Audio/Files/LiveVideoDecoder.cs) | Streaming RGB frames for live preview |

[`VideoTrackViewModel`](Ongenet.App/ViewModels/Panels/VideoTrackViewModel.cs) wires transport ticks, session-clip events, and MIDI input into `VideoTriggerEngine`. **ffmpeg** must be on the PATH for preview frames and MP4 export; without it sync time still updates.

User guide: [Video & composition](https://onge.net/articles/guides/video-and-composition.html).

### Comping (take lanes)

1. Enable **Loop rec** on the transport for loop comping — each pass creates a new take lane automatically.
2. Click comp regions in take lanes to toggle which takes are audible (multi-select supported).
3. Arm a take lane for the next recording pass from the lane header.
4. **Flatten comp** bakes selected regions (warp-aware, with crossfades) into one audio clip.

### Control surfaces

Choose **MCU Transport**, **Launchpad Session**, **HUI Transport**, **Push 2**, or **APC40** under
**Settings → Control Surface**. Learnable mixer CC mappings are stored per profile. When no profile
is selected, the legacy combined MCU + Launchpad behaviour remains for backward compatibility.

### Windows low-latency audio

Enable **WASAPI exclusive mode** under **Settings → Audio** on Windows for lower output latency.

### Groove pool

Import user grooves as `.ongenet-groove` JSON files from **Track Controls → Groove**, or extract
swing from a MIDI clip. User grooves are saved with the project.

### Window layouts

Use **View → Save layout** / **Load layout** in the main window menu to persist multi-monitor
workspace bounds.

### Collaboration sync

Use **File → Share to sync folder** to export a read-only project manifest and copy for folder-based
collaboration (configure the sync folder in settings).

### Accessibility

Core UI regions (transport, timeline, mixer, settings) expose screen-reader names via Avalonia
automation properties. See [docs/main-window-layout.md](docs/main-window-layout.md) for shortcuts.

### Input monitoring

On audio tracks, set **Input monitoring** on the mixer strip to **Off**, **Auto** (when armed), or **On**.
Software monitoring mixes the live input device into the master output so you can hear what you are recording.

### Tap tempo & MIDI clock

Use the transport-bar **Tap** button to set project BPM from your tap rhythm. Enable **MIDI clock output**
under **Settings → Audio** to send 24 ppqn clock to an external MIDI output device while playing.

### Piano-roll quantize & groove

In the piano roll toolbar, **Quantize** snaps the selected clip's notes to the current grid; **Groove**
applies the project's active groove template. The **Expression** toggle shows an MPE expression lane.

### Tempo map & section playlist

**View → Tempo Map** opens a window to add and edit master-track tempo automation points at the playhead.
**View → Section Playlist** builds an ordered song-structure playlist from arrangement markers and steps
through sections during playback.

### Routing matrix

Open **Routing Matrix** from the mixer to edit track output targets, send levels, and multi-out plugin routes.

### Notation PDF

In the **Notation** tab, **Export PDF…** renders the current staff view to a PDF file (alongside MusicXML import/export).

### Automation (lane-only design)

Ongenet does **not** use separate automation clip regions on the timeline. Parameter automation lives on
**indented automation lanes** under each track (right-click an automatable control → **Create automation
track**). Points are stored per-lane in beats; the engine evaluates them during playback and defers to
manual input while recording is armed on that lane. This keeps automation co-located with the track it
modulates and avoids a second clip-editing paradigm.

### Stem separation

Right-click an audio clip → **Separate stems (4-way)** to split into vocals/drums/bass/other tracks
(offline). The export dialog also exposes stem separation for the selected clip. When the external
**demucs** CLI and **ffmpeg** are installed, separation quality improves; otherwise a built-in heuristic
is used.

### Retrospective MIDI capture

Use **Cap MIDI** on the transport bar to capture notes from the retrospective buffer (keyboard, preview, or hardware
MIDI) into a new MIDI clip at the playhead without pressing Record first.

### Linked clips

**Create linked copy** on a clip context menu duplicates placement while sharing note/audio content.
Linked clips show a 🔗 suffix; **Unlink** breaks the group without changing shared data until **Make
unique** is used.

### Global key & scale

Set **Key** and scale on the transport bar; the piano roll scale snap controls follow the same project
settings.

### Audio to MIDI

**Convert to MIDI…** on an audio clip opens a guided wizard (analyze → create track) for monophonic or
polyphonic detection.

---

## 2. Getting the source & first build

```bash
git clone https://github.com/1-chris/Ongenet
cd Ongenet
dotnet build Ongenet.sln       # restores NuGet packages and builds every project
```

The first build restores packages and may take a minute; subsequent builds are incremental.

> **Building only the desktop/web heads:** the solution now includes `Ongenet.Android` (`net10.0-android`),
> so a full `dotnet build Ongenet.sln` needs the Android toolchain from [§6](#6-the-android-head-tablets).
> If you only work on the desktop or web heads, build those projects directly instead — they have **no**
> Android dependency:
>
> ```bash
> dotnet build Ongenet.Desktop      # desktop head only
> dotnet build Ongenet.Web          # web head only (needs wasm-tools)
> ```
>
> `publish-desktop.sh` likewise builds only `Ongenet.Desktop`, so producing desktop packages never
> requires the Android SDK or JDK.

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

Deployment to GitHub Pages is automated by `.github/workflows/deploy-web.yml` on push to `main`. The
workflow runs `./scripts/build-site.sh`, which:

1. Builds API-doc projects and runs **DocFX** (`site/docfx.json`) — user [guides](https://onge.net/articles/guides/), [dev tutorials](https://onge.net/dev/), and [API reference](https://onge.net/api/)
2. Publishes `Ongenet.Web` and copies the bundle to `/app/`
3. Assembles `_site/` (marketing homepage from `site/homepage/`, assets, screenshots, `.nojekyll`)

### Building the site locally

Requires the .NET 10 SDK only (DocFX is restored via `dotnet tool restore` — no Node, Jekyll, or Ruby):

```bash
./scripts/build-site.sh              # full site + WASM demo
BUILD_WASM=0 ./scripts/build-site.sh # docs only (faster)
dotnet docfx site/docfx.json --serve # preview while editing markdown
```

See [docs/web-demo.md](docs/web-demo.md) for the WASM architectural split.

### Running the WASM head alone

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

---

## 6. The Android head (tablets)

`Ongenet.Android` is the Android **head**: a thin `net10.0-android` app (Avalonia.Android) that reuses the
shared `Ongenet.App` UI and the portable `Ongenet.Core` engine, exactly like the desktop and web heads. It
runs under Avalonia's single-view lifetime and shows the **same shared `MainView`** the browser uses
(`Ongenet.App/Views/MainView.axaml`), so there is no Android-specific UI to maintain. Audio goes through a
native **AAudio** backend that lives in `Ongenet.Audio` alongside the ALSA/CoreAudio/WASAPI backends
(`Ongenet.Audio/Native/Android/AndroidNativeBackend.cs`, P/Invoking `libaaudio.so`). It is built for
**tablets** (sensor-landscape, large screens) and is designed to be **sideloaded** — no Android Studio or
emulator needed.

The platform pieces are wired in `Ongenet.Android`: `AndroidApp` (the `[Application]` class that boots
Avalonia) and `AndroidPlatform : IPlatformServices` (registers the AAudio backend and Android-safe service
stubs for settings/library/preset/MIDI). MIDI input, audio capture, and on-device file import are stubbed
(the library/preset tabs start empty); the built-in instruments and effects work.

### One-time setup

1. **Install the Android workload:**

   ```bash
   dotnet workload install android
   ```

2. **Install a full JDK 21** (the .NET Android tooling requires *exactly* 21, and a real JDK with
   `javac`/`jar`, not a JRE). On Fedora/Nobara:

   ```bash
   sudo dnf install java-21-openjdk-devel       # lands in /usr/lib/jvm/java-21-openjdk
   ```

   (The system's default JDK can be a newer version — the Android build is pointed at JDK 21 explicitly.)

3. **Provision the Android SDK** once (downloads the platform, build-tools and platform-tools into
   `~/Android/Sdk`; no Android Studio involved):

   ```bash
   dotnet build Ongenet.Android/Ongenet.Android.csproj -t:InstallAndroidDependencies \
     -p:AndroidSdkDirectory=$HOME/Android/Sdk \
     -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk \
     -p:AcceptAndroidSDKLicenses=True
   ```

### Building the APK

The helper script builds a sideloadable, debug-signed APK and copies it to `dist/Ongenet-<version>.apk`:

```bash
./publish-android.sh                 # Release APK  → dist/Ongenet-<version>.apk
./publish-android.sh --debug         # Debug build instead
./publish-android.sh --no-copy       # leave it in bin/, don't copy to dist/
```

It auto-detects the SDK (`$HOME/Android/Sdk`, or `$ANDROID_SDK`/`$ANDROID_HOME`) and a JDK 21 via
`$JAVA21_HOME`, `$JAVA_HOME`, common Linux paths under `/usr/lib/jvm`, or Homebrew `openjdk@21` on macOS.
`Directory.Build.props` applies the same JDK probe when you run `dotnet build` without `-p:JavaSdkDirectory`.
To build by hand without the script:

```bash
dotnet build Ongenet.Android/Ongenet.Android.csproj -c Debug \
  -p:AndroidSdkDirectory=$HOME/Android/Sdk \
  -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk
# APK: Ongenet.Android/bin/Debug/net10.0-android/net.onge.ongenet-Signed.apk
```

### Getting it onto a tablet

No emulator required — sideload the APK:

```bash
adb install -r dist/Ongenet-<version>.apk      # over USB with debugging enabled
```

…or just copy the `.apk` to the device and open it with a file manager (allow "install from unknown
sources" for that app). The APK is signed with the Android **debug** key, which is fine for sideloading; a
Play Store upload would instead use a real signing keystore and an `.aab` (`AndroidPackageFormat=aab`).

> **JDK version errors (`XA0030`)?** The tooling rejects anything other than JDK 21. Point the build at a
> full JDK 21 with `-p:JavaSdkDirectory=…` or set `JAVA_HOME` / `JAVA21_HOME` (must contain `bin/javac`
> and `bin/jar`). On Windows, install Temurin 21 and set `JAVA_HOME` before building.

---

## 7. Building distributable desktop packages

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

## 8. Continuous integration & releases

| Workflow | Trigger | What it does |
| --- | --- | --- |
| `.github/workflows/desktop-build.yml` | push/PR to `main`, `v*` tags, manual | Builds self-contained desktop packages for every RID via `publish-desktop.sh` **and** a sideloadable Android APK via `publish-android.sh` (its own JDK 21 + android-workload job), uploads them as artifacts, and — on a `v*` tag — attaches them all to one GitHub Release. |
| `.github/workflows/deploy-web.yml` | push to `main`, manual | Installs `wasm-tools`, runs `./scripts/build-site.sh` (DocFX site + WASM publish), assembles `_site/` (homepage, guides, dev docs, API at `/`, app at `/app/`), and deploys to GitHub Pages. |

### Cutting a release

1. Bump `<Version>` in `Directory.Build.props` (the single source of truth — it flows into every
   assembly's version and is shown at runtime in the title bar).
2. Commit, tag `vX.Y.Z`, and push the tag. The Build & Release workflow publishes the GitHub Release with
   all desktop platform packages **and** the Android APK (`Ongenet-X.Y.Z.apk`) attached.

---

## 9. GPU 3D engine & embeddable visuals

Ongenet has a small, GPU-accelerated **3D engine** used for rich custom controls (the **3D Scope** effect
is the first consumer). It is **desktop-only** and split into two projects so the shared UI and the
web/Android heads never pull native GPU code:

- **`Ongenet.Engine3D.Abstractions`** — a portable, dependency-free **scene model** (`Scene`, `SceneNode`,
  `MeshData`, `Material`, orbit `Camera`, `Light`, the immutable per-frame `SceneSnapshot`) plus the engine
  **contracts** (`I3DEngineFactory`, `I3DRenderSession`, `FrameResult`). BCL only — referenced by both the
  UI and the native engine.
- **`Ongenet.Engine3D`** — the native renderer. A hand-written **Render Hardware Interface** (RHI, in
  `Rhi/`) over **Silk.NET**, with a **Vulkan** backend (`Vulkan/`) that renders a scene offscreen and reads
  it back to a BGRA buffer. The RHI seam leaves room for D3D12/native-Metal backends later without touching
  the UI or scene model.

**How it embeds in Avalonia.** The shared UI (`Ongenet.App`) resolves an `I3DEngineFactory` from DI at
runtime and degrades to a placeholder when it's absent (web/Android) or reports no GPU. The pieces:

- `Controls/Engine3DView` — an Avalonia `Control` that owns a session, runs the GPU render on a background
  thread (triple-buffered), and presents finished frames. Exposes a mutable `Scene` + an `OnUpdate` hook,
  with drag-to-orbit / scroll-to-zoom. Driven at display refresh rate via the `FrameTicker`.
- `Controls/Engine3D/ReadbackPresenter` — Phase-1 bridge: copies the engine's BGRA pixels into a
  `WriteableBitmap`. Universal and composes perfectly with the UI (no native child surface / airspace).
  `CompositionInteropPresenter` + `Engine3DInterop` are the zero-copy seam for a future shared-texture path.
- `Controls/Engine3DVisualHost` + `IEngine3DVisualization` — the **reusable** layer: drop the host anywhere,
  give it a visualization factory, and it builds/animates the visual, tracks the **theme** live, and offers
  a generic *"Open in window"* button that re-hosts the same visual in a resizable `Engine3DVisualWindow`.

**macOS / MoltenVK.** `Ongenet.Engine3D` references **`Silk.NET.MoltenVK.Native`**, which bundles
`libMoltenVK.dylib` (Vulkan-on-Metal). Silk.NET's loader finds it automatically, so **no Vulkan SDK install
is required** on macOS — `dotnet run` just works. Windows and Linux use the system Vulkan loader
(`vulkan-1.dll` / `libvulkan.so.1`); if it's missing, the factory reports unavailable and 3D controls show
their placeholder (the rest of the app is unaffected). Shaders are GLSL compiled to SPIR-V at runtime via
`Silk.NET.Shaderc` (its native lib is bundled too), so there is no build-time `glslang`/`glslc` step.

To build an audio-modulated 3D visual of your own, follow
[docs/creating-3d-visual-effects.md](docs/creating-3d-visual-effects.md).

---

## 10. Project layout (quick reference)

| Project | Targets | Role |
| --- | --- | --- |
| `Ongenet.Core` | `net10.0` | Portable engine, DSP, instruments, effects, persistence — no UI, BCL only. |
| `Ongenet.App` | `net10.0` | Shared Avalonia UI library used by every head. Owns the desktop `MainWindow` and the shared single-view `MainView` (used by the web + Android heads), plus the embeddable 3D controls. |
| `Ongenet.Engine3D.Abstractions` | `net10.0` | Portable 3D scene model + engine contracts (no Avalonia, no GPU, BCL only). |
| `Ongenet.Engine3D` | `net10.0` | Native GPU 3D engine (hand-written Vulkan RHI; MoltenVK on macOS). Desktop-only; injected via DI. |
| `Ongenet.Desktop` | `net10.0` | Desktop exe head (native audio/MIDI + plugins + the GPU 3D engine). Publishes as `Ongenet`. |
| `Ongenet.Web` | `net10.0-browser` | Browser exe head (Web Audio, browser-safe stubs). |
| `Ongenet.Android` | `net10.0-android` | Android (tablet) head (AAudio backend, Android-safe stubs, single-view shell). Sideloaded APK. |
| `Ongenet.Audio` | `net10.0` | Native audio + MIDI backends (ALSA/CoreAudio/WASAPI/**AAudio**; ALSA seq/WinMM/CoreMIDI). |
| `Ongenet.Clap` / `Ongenet.Lv2` / `Ongenet.Vst` | `net10.0` | Plugin hosting (CLAP / LV2 / VST2+VST3). |
| `Ongenet.Link` | `net10.0` | Ableton Link tempo/phase sync (GPL isolated assembly). |
| `Ongenet.Ara` | `net10.0` | Celemony ARA2 hosting seam (stub without SDK). |
| `Ongenet.Core.Tests` | `net10.0` | xUnit tests for Core + LV2. |

Each head plugs its platform pieces into the shared `App` through
`Ongenet.App.Platform.IPlatformServices` (`DesktopPlatform` / `WebPlatform` / `AndroidPlatform`).

### Ableton Link (optional native)

`Ongenet.Link` wraps [libabl-link](https://github.com/Ableton/link/tree/master/extensions/abl_link)
(the plain-C Ableton Link wrapper). Without the native library the desktop head registers
`NullLinkSession` and the transport Link toggle stays hidden.

Build and enable the native session:

```bash
git clone https://github.com/Ableton/link.git third_party/link
cmake -S third_party/link -B build/link -DCMAKE_BUILD_TYPE=Release
cmake --build build/link --target abl_link
# macOS: libabl-link.dylib  Linux: libabl-link.so  Windows: abl-link.dll
```

Copy or symlink the shared library next to the `Ongenet` executable (or onto the loader path). When
`libabl-link` is present under `third_party/link/build` or `build/link`, MSBuild auto-enables native Link
via `Directory.Build.props` (`OngenetLinkNative=true`) and copies the library into the output folder.
You can also build manually with the compile flag:

```bash
dotnet build Ongenet.Desktop/Ongenet.Desktop.csproj -p:DefineConstants=ONGENET_LINK_NATIVE
```

At runtime `LinkSessionFactory` probes `NativeLibrary.TryLoad("abl-link")` and falls back to
`NullLinkSession` when the library is missing. When enabled, the transport bar syncs tempo, play/stop, and
maps the start marker to the shared Link phase on play.

### Celemony ARA (optional SDK)

`Ongenet.Ara` compiles without the Celemony ARA SDK. VST3 ARA plug-ins are detected via the
`ARA Main Factory Class` category (see Celemony `ARAVST3.h`); binding and editor hosting are stubbed
until you opt in:

1. Clone the [ARA SDK](https://github.com/Celemony/ARA_API) and set `ARA_SDK_PATH` to its root
   (the folder containing `ARA_API/` and `ARA_Library/`).
2. Add SDK references to `Ongenet.Ara.csproj` (example — adjust paths for your platform):

   ```xml
   <PropertyGroup Condition="'$(ARA_SDK_PATH)' != ''">
     <DefineConstants>$(DefineConstants);ENABLE_ARA</DefineConstants>
   </PropertyGroup>
   <ItemGroup Condition="'$(DefineConstants)' != '' and $(DefineConstants.Contains('ENABLE_ARA'))">
     <Compile Include="$(ARA_SDK_PATH)/ARA_Library/Dispatch/ARAHostDispatch.cpp" Link="ara/ARAHostDispatch.cpp" />
     <None Include="$(ARA_SDK_PATH)/ARA_API/**" Link="ara-sdk/%(RecursiveDir)%(Filename)%(Extension)" />
   </ItemGroup>
   ```

3. Build with the SDK path and flag:

   ```bash
   export ARA_SDK_PATH=/path/to/ARA_API
   dotnet build Ongenet.Desktop/Ongenet.Desktop.csproj -p:DefineConstants=ENABLE_ARA
   ```

4. Wire real document controllers in `AraHost.CreateSdkDocument` / `OpenSdkEditor` (the `#if ENABLE_ARA`
   seam is structured; today it still returns `StubAraDocument` until SDK bindings are added).

The clip context menu **Open ARA Editor** appears when the track hosts an ARA-capable VST3; an **ARA**
badge marks clips with an active ARA region.

---

## 7. Audio performance profiling

The engine exposes lightweight diagnostics via `Ongenet.Core.Audio.AudioDiagnostics`:

- `LastBlockMicroseconds` — duration of the last completed render block
- `Snapshot()` — block count, average/max block time, macOS ring underrun count, ring fill level

On macOS, `MacAudioOutput` increments the underrun counter when the CoreAudio consumer reads
past the end of the producer ring (audible crackle/dropout).

### Climax2 stress test (Ascension demo)

1. Launch the desktop app (loads the **Ascension** demo by default).
2. Set the loop region to bars **152–168** (Climax2 peak).
3. Play for at least **2 minutes**.
4. Inspect diagnostics (e.g. log or debugger watch on `AudioDiagnostics.Snapshot()`):
   - **Target:** `MaxBlockMicroseconds` < **8000** (~20% headroom under a 512-frame @ 48 kHz budget of ~10.7 ms)
   - **Target:** `UnderrunCount` = **0**
5. Toggle **F8** in the main window to overlay UI frame/render timing and confirm the timeline stays smooth.

Re-run after performance changes and compare average/max block time and underrun count.

---

## Completeness checklist

Ongenet targets a **feature-complete open-source DAW**.

| Phase | Scope | Status |
| --- | --- | --- |
| **1 — Polish** | Section playlist UI, export naming, video/ffmpeg fallback, stem separation UX | **Done** |
| **2 — Producer parity** | ASIO enumeration, instrument/drum rack, piano-roll operators, accessibility | **Done** |
| **3 — Specialist** | Chord track, expression maps, LTC, hybrid tracks, audio editor, LV2 UI seam | **Done** |
| **4 — Pro hardening** | Plugin isolation (`Ongenet.PluginHost`), AAF/OMF handoff, collab sync | **Done** |
| **5 — Broadcast** | Control Room, ADM BWF immersive export | **Done** |
| **6 — Ecosystem** | Roslyn scripting host, library templates, stem separation presets | **Done** |
| **7 — Parity gaps** | Edison-class Audio Editor, polyphonic pitch editor, scripting UI, plugin sandbox | **Done** |

### Parity features

- **Audio Editor** — standalone multitrack window ([docs/audio-editor.md](docs/audio-editor.md))
- **Polyphonic pitch** — built-in note-segment editor ([docs/polyphonic-pitch.md](docs/polyphonic-pitch.md))
- **Scripting** — Scripting centre tab: in-app IDE (`Ongenet.Scripting.Editor`), expanded `IScriptingApi`, **Export project/preset** (`ProjectScriptExporter`, `PresetScriptExporter`), live handlers, output console, pop-out window, user scripts folder ([docs/scripting.md](docs/scripting.md))
- **Plugin isolation** — optional VST3 effect sandbox ([docs/plugin-isolation.md](docs/plugin-isolation.md))

### Competitor glossary

| Competitor term | Ongenet equivalent |
| --- | --- |
| Bitwig **Grid** / FL **Patcher** | **Field** |
| FL **Factory** | **Library** (Projects + presets) |
| Ableton **Session View** | **Session** tab |
| FL **Channel Rack / Patterns** | **Channel Rack** + pattern tracks |
| Ableton **Instrument/Drum Rack** | **Instrument rack** + drum pad grid |

### Interchange & licensing

- **Immersive delivery** — **ADM BWF** export (ITU-R BS.2076) with XML sidecar for broadcast handoff
- **Post interchange** — structured **AAF/OMF XML** and custom timeline XML for Nuendo/Pro Tools pipelines
- **Collaboration** — self-hosted versioned folder sync (File → Collaboration)
