# Scripting

## Quick steps

1. Open the **Scripting** centre tab (after **Video**), or use **Pop out** for a separate window.
2. Pick a factory script or load/save a `.cs` file from **`Documents/Ongenet/Scripts/`**.
3. Edit in the built-in IDE (syntax highlighting, diagnostics, IntelliSense); click **Run** for batch automation or **Start live** for event handlers.
4. Read **`api.Log()`** output in the console.

## Portable export

- **Export project** — generates a portable C# script that rebuilds the open project via `api.*` calls (no audio PCM; re-link sample paths).
- **Export preset** — exports the selected track's instrument slot or FX chain as a pasteable script snippet.
- Save exported scripts with **Save as…** into `Documents/Ongenet/Scripts/` to reuse on any machine.

## Details

Ongenet embeds a Roslyn C# scripting host with a curated `IScriptingApi`:

- **Project** — tempo, time signature, key, playback mode, clear/rebuild
- **Transport** — play, stop, seek, loop region, playhead
- **Tracks** — all track kinds, mute/solo/arm, routing, sends
- **Devices** — instruments, effects, presets, parameters
- **Clips** — MIDI notes/CC, audio metadata, automation, patterns, session clips
- **Timeline meta** — markers, chord track, drum maps, video paths, overlays, triggers
- **Video** — `GetVideoEnabled` / `SetVideoEnabled`, `GetVideoElements` / `AddVideoElement`, `GetVideoTriggers` / `AddVideoTrigger`
- **MIDI bulk** — quantize, transpose, velocity scale, humanize, chance
- **Live handlers** — transport state, beat grid, clip changes (Start live)

Scripts cannot access arbitrary filesystem paths or spawn processes — see [Dev: Scripting](/dev/scripting.html) for the full API and security limits.

### Batch example

```csharp
api.SetTempo(128.0);
foreach (var track in api.GetTracks())
    api.Log($"{track.Name}: {track.Volume:P0}");
```

### Live example

```csharp
api.OnTransportStateChanged(state =>
    api.Log(state == ScriptTransportState.Playing ? "Playing" : "Stopped"));
```

### Video example

```csharp
api.SetVideoEnabled(true);
foreach (var el in api.GetVideoElements())
    api.Log($"Overlay: {el.Name} ({el.Kind})");
```

## Related

- [Getting started](getting-started.md)
- [Video & composition](video-and-composition.md)
- [Dev: Scripting API](/dev/scripting.html)
- [Keyboard shortcuts](keyboard-shortcuts.md)
