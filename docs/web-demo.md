# Ongenet on the web (WebAssembly)

`Ongenet.Web` is a browser build of the app — the **same** engine and UI as the desktop, compiled to
WebAssembly with Avalonia's browser backend and deployed as static files to GitHub Pages
(**onge.net/app/**). It is a demo: some desktop features are intentionally dropped or stubbed, and audio
runs on the main thread so it can glitch under load. The desktop app is unchanged.

## Project layout (the split)

The UI used to live in the desktop exe. It is now a shared library that both heads reference:

| Project | What it is | Targets |
| --- | --- | --- |
| `Ongenet.Core` | Engine, DSP, instruments, effects, persistence (portable, no UI) | `net10.0` |
| `Ongenet.App` | **Shared Avalonia UI library** — `App`, all views/view-models, controls, theming, assets (CLR + XAML namespaces: `Ongenet.App.*`). | `net10.0` |
| `Ongenet.Desktop` | Desktop **exe head** — native audio (PortAudio/OS), MIDI, CLAP/LV2. Publishes as `Ongenet`. | `net10.0` |
| `Ongenet.Web` | Browser **exe head** — Web Audio backend, browser-safe service stubs, single-view shell. | `net10.0-browser` |
| `Ongenet.Audio` / `Ongenet.Clap` / `Ongenet.Lv2` | Native audio + plugin hosting (desktop only). | `net10.0` |

The heads are named purely by platform (`Ongenet.Desktop`, `Ongenet.Web`, and later `Ongenet.Android` /
`Ongenet.iOS`); `Ongenet.App` is the shared application they all build on. Each head plugs its platform
pieces into the shared `App` through `Ongenet.App.Platform.IPlatformServices`
(`DesktopPlatform` / `WebPlatform`): which audio backend(s) exist, the MIDI source, optional plugin hosts,
and which shell to show (a desktop `Window` vs the in-canvas `MainView`). `App` itself is portable and
supports both the classic-desktop and single-view (browser) lifetimes.

Because the shared UI stays a plain `net10.0` assembly, the browser head just references it — no second
compilation of the UI is needed.

## Audio in the browser

`WebAudioBackend` implements the engine's `IAudioBackend`/`IAudioOutput` seam over the Web Audio API:

- A **`ScriptProcessorNode`** (in `wwwroot/ongen-audio.js`) pulls one interleaved block per callback by
  calling the exported `AudioInterop.RenderBlock`, which runs the engine's render callback.
- This is deliberately **not** an `AudioWorklet` + `SharedArrayBuffer` ring buffer. A worklet ring would
  need cross-origin isolation (COOP/COEP headers), which **GitHub Pages cannot set**. The ScriptProcessor
  runs on the main thread and needs no special headers, so it works from plain static hosting. The cost is
  glitching when the UI is busy — acceptable for a demo. Upgrading to a worklet is the future perf path if
  real headers ever become available (e.g. behind a CDN that can set them, or a `coi-serviceworker` shim).
- The `AudioContext` starts suspended until a user gesture; `ongen-audio.js` resumes it on first interaction.
- Audio **input/recording** is not implemented (no `getUserMedia` capture yet).

## What's dropped or stubbed in the browser

- **CLAP / LV2 plugins** — native code, impossible in WASM. Built-in instruments and effects (3xOsc, Padda,
  Kicka, Granular, Sampler, all effects) work — they come from the in-process registries, not native libs.
- **MIDI input** — `BrowserMidiInputService` reports no devices. Web MIDI (`navigator.requestMIDIAccess`)
  is the eventual replacement.
- **Filesystem** — no library scan, no preset files (`Browser{LibraryScan,Preset,AppSettings}Service` are
  empty/in-memory). Built-in instruments/effects still populate their library tabs.
- **Project open/save (`.ongen`)** — not wired in the browser shell yet. The format is `Stream`-based, so a
  download/upload path is straightforward to add later.
- **Multiple windows** — the browser is single-canvas. `MainView` hosts the workspace; secondary windows
  (Settings/History/Log/plugin GUIs) are not shown.
- **ffmpeg import** — desktop shells out to `ffmpeg`; there is no subprocess in the browser. WAV imports via
  the built-in parser; other formats would need `ffmpeg.wasm` or the browser's `decodeAudioData`.

## Build & deploy

Local build:

```bash
dotnet workload install wasm-tools          # one-time

# Run locally with the built-in WASM dev server (opens the app in your browser):
dotnet run --project Ongenet.Web

# Or produce the static bundle for hosting:
dotnet publish Ongenet.Web/Ongenet.Web.csproj -c Release
# Static bundle: Ongenet.Web/bin/Release/net10.0-browser/browser-wasm/AppBundle/
```

`dotnet run` uses the `runtimeconfig.template.json` (`wasmHostProperties.perHostConfig`, host `browser`,
`html-path: index.html`) to launch the dev server. Alternatively serve the `AppBundle` folder with any
static server (`python3 -m http.server` from that folder, or `dotnet serve`) — the app is at `index.html`.

Deployment is automated by **`.github/workflows/deploy-web.yml`** on push to `main`. **Set the repo's Pages
source to "GitHub Actions"** (Settings → Pages) — the older "Deploy from a branch → /docs" mode never builds
the WASM app (GitHub doesn't run dotnet for branch-based Pages), it only serves committed files. The workflow:

1. Installs `wasm-tools`, publishes `Ongenet.Web`.
2. Assembles `_site/`: the existing homepage and assets from `./docs` (incl. `index.html` and `CNAME`) at the
   root, the app `AppBundle` at `/app/`, plus a **`.nojekyll`** file (mandatory — Jekyll otherwise strips the
   `_framework/` directory because of the leading underscore).
3. Publishes to GitHub Pages, so the homepage stays at `onge.net/` and the app is served at `onge.net/app/`.

The app uses `<base href="./">`, so it works from the `/app/` sub-path with no rebuild. The custom domain is
carried by `docs/CNAME` (`onge.net`), which the workflow copies to the site root.
