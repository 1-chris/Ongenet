# Developing & building Ongenet

See [README.md](README.md) for the project overview and what each subproject does.

## Getting started (development)

Requires the **.NET 10 SDK**. All projects target .NET 10.

```bash
dotnet build Ongenet.sln
dotnet run --project Ongenet.Desktop
```

### Audio (PortAudio) for development

Audio output goes through PortAudio, which is **not** bundled in a plain `dotnet run`. The app still
launches without it — it just runs silently and logs that no device was available.

- **Linux**: install your distro's PortAudio (`libportaudio2` on Debian/Ubuntu, `portaudio` on
  Fedora/Nobara/Arch). On a PipeWire system this routes through PipeWire automatically.
- **Windows / macOS**: see the packaging section — the simplest path locally is to drop a
  `portaudio.dll` / `libportaudio.2.dylib` next to the built executable. The loader (`PortAudioNative`)
  probes the app directory first, then the system search path.

## Building distributable packages

The repo includes two helper scripts at the solution root:

| Script | What it does |
| --- | --- |
| `build-portaudio.sh` | Builds the native PortAudio libraries into `native/<rid>/` — `linux-x64/libportaudio.so.2` (native gcc + cmake) and `win-x64/portaudio.dll` (MinGW-w64 cross-compile, runtime statically linked). Skips any target whose toolchain is missing. |
| `publish-desktop.sh` | Self-contained publish of `Ongenet.Desktop` for all platforms, bundling the matching PortAudio lib and zipping each to `dist/Ongenet-<rid>.zip`. |

`publish-desktop.sh` runs `build-portaudio.sh` first, then produces **self-contained, single-file**
executables (the .NET runtime is bundled, so target machines need no .NET install — that's why each
executable is ~100 MB). It publishes `linux-x64`, `win-x64`, `osx-arm64`, and `osx-x64`.

```bash
./publish-desktop.sh                 # all platforms → dist/Ongenet-<rid>.zip
./publish-desktop.sh linux-x64       # only the listed RID(s)
./publish-desktop.sh --symbols       # keep .pdb debug symbols (default strips them for smaller size)
./publish-desktop.sh --no-zip        # leave the publish folders, don't zip
./publish-desktop.sh --no-native     # skip rebuilding the native PortAudio libs
```

Run targets inside each package:

- **Linux**: `./Ongenet.bin` (renamed from `Ongenet.Desktop` so desktop environments don't mistake the
  `.Desktop` suffix for a `.desktop` launcher).
- **Windows**: `Ongenet.Desktop.exe`
- **macOS**: `./Ongenet.Desktop`

`dist/` and `native/` are git-ignored build artifacts.

### Toolchains for the native PortAudio build

`build-portaudio.sh` clones PortAudio v19.7.0 (override with `PORTAUDIO_TAG`, or point `PORTAUDIO_SRC`
at an existing source tree) and needs, per target:

- **linux-x64**: `gcc`, `cmake`, and ALSA dev headers (`alsa-lib-devel` / `libasound2-dev`).
  For first-class PipeWire/JACK support also install `pipewire-jack-audio-connection-kit-devel`
  (Fedora/Nobara) — the script then enables PortAudio's JACK backend automatically.
- **win-x64**: `cmake` plus the MinGW-w64 cross toolchain, including the C++ compiler:
  `sudo dnf install mingw64-gcc mingw64-gcc-c++ mingw64-winpthreads-static` (Fedora/Nobara).
- **macOS**: cannot be cross-built from Linux (needs Apple's SDK). Build `libportaudio.2.dylib` on a
  Mac or CI runner (`brew install portaudio`) and drop it into `native/osx-arm64/` and `native/osx-x64/`;
  `publish-desktop.sh` will bundle whatever is present.

The publish run prints a **PortAudio bundling summary** at the end so you can see which packages shipped
the native library.
