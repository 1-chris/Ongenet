# Developing & building Ongenet

See [README.md](README.md) for the project overview and what each subproject does.

## Getting started (development)

Requires the **.NET 10 SDK**. All projects target .NET 10.

```bash
dotnet build Ongenet.sln
dotnet run --project Ongenet.Desktop
```

### Audio for development

Audio runs on each OS's native backend via P/Invoke to the platform's own audio libraries, so there is
nothing extra to install for a plain `dotnet run`. If no device is available the app still launches — it
just runs silently and logs that fact.

- **Linux**: uses ALSA (`libasound.so.2`), which ships on essentially every desktop. On a
  PipeWire/PulseAudio system playback routes through the server automatically; JACK is used when present.
- **Windows**: WASAPI — part of the OS, nothing to install.
- **macOS**: CoreAudio — part of the OS, nothing to install.

## Building distributable packages

The repo includes a helper script at the solution root:

| Script | What it does |
| --- | --- |
| `publish-desktop.sh` | Self-contained publish of `Ongenet.Desktop` for all platforms, zipping each to `dist/Ongenet-<rid>.zip`. |

`publish-desktop.sh` produces **self-contained, single-file** executables (the .NET runtime is bundled,
so target machines need no .NET install — that's why each executable is ~100 MB). It publishes
`linux-x64`, `linux-arm64`, `win-x64`, `osx-arm64`, and `osx-x64`. Audio uses each OS's native backend
(ALSA/PipeWire/JACK/Pulse, CoreAudio, WASAPI) via the platform's own libraries, so nothing native is
compiled or bundled.

```bash
./publish-desktop.sh                 # all platforms → dist/Ongenet-<rid>.zip
./publish-desktop.sh linux-x64       # only the listed RID(s)
./publish-desktop.sh --symbols       # keep .pdb debug symbols (default strips them for smaller size)
./publish-desktop.sh --no-zip        # leave the publish folders, don't zip
```

Run targets inside each package:

- **Linux** (`linux-x64` / `linux-arm64`): `./Ongenet.bin` (renamed from `Ongenet.Desktop` so desktop
  environments don't mistake the `.Desktop` suffix for a `.desktop` launcher).
- **Windows**: `Ongenet.Desktop.exe`
- **macOS**: `./Ongenet.Desktop`

`dist/` is a git-ignored build artifact.

The audio backend is the OS-native stack on every platform (ALSA/PipeWire/JACK/PulseAudio on Linux,
CoreAudio on macOS, WASAPI on Windows), reached through P/Invoke to libraries the OS already provides.
There are no native libraries to compile, so the publish has no extra toolchain requirements beyond the
.NET SDK (and `zip` for packaging).
