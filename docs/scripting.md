# Scripting

Ongenet ships a **C# scripting host** (`Ongenet.Scripting`) for batch automation and live event handlers — similar in spirit to Max for Live scripts, but simpler: run `.cs` files against the open project without restarting the app.

Desktop only. Web and Android register [`NullScriptingHost`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Services/IScriptingHost.cs) and [`NullScriptingApi`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Services/NullScriptingApi.cs).

## Quick start

1. Open the **Scripting** centre tab (after **Video**), or use **Pop out** for a separate window.
2. Select a factory script (or **Load script…** / **Save as…** / **New** in the built-in editor).
3. Click **Run** for a one-shot batch script, or **Start live** for handler subscriptions.
4. Read messages in the **Output** panel; use **Undo** to revert mutating changes.

Factory scripts load from `{exe}/Scripts/` on build. User scripts load from **`Documents/Ongenet/Scripts/`** (same name overrides factory scripts).

## Writing a batch script

Scripts compile and run via [Roslyn](https://github.com/dotnet/roslyn) (`Microsoft.CodeAnalysis.CSharp.Scripting`). A global `api` object implementing [`IScriptingApi`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Services/IScriptingApi.cs) is available:

```csharp
// Set project tempo to 128 BPM
api.SetTempo(128.0);

// Rename every track with a prefix
foreach (var pair in api.GetTrackNames())
    api.RenameTrack(pair.Key, "Mix — " + pair.Value);

// Quantize all MIDI clips to 1/16 (0.25 beats)
api.QuantizeAllMidiClips(0.25);

// Log to the Scripts output panel
api.Log($"Project: {api.GetProjectName()} @ {api.GetTempo()} BPM");
```

Imports available by default: `System`, `System.Collections.Generic`, `Ongenet.Core.Services`.

## Writing a live script

Use **Start live** instead of **Run**. Live scripts register handlers that stay active until **Stop live** or **Unload**:

```csharp
api.OnTransportStateChanged(state =>
{
    if (state == ScriptTransportState.Playing)
        api.Log($"Playing at {api.GetPlayheadBeats():F1} beats");
});

api.OnBeat(beat => api.Log($"Bar boundary at {beat:F1}"), gridBeats: 4.0);

api.OnClipChanged(clip => api.Log($"Clip changed: {clip.Name}"));
```

Live limits:

- Handlers run on the UI thread (not the audio thread).
- Keep handler work minimal (&lt; 10 ms recommended).
- One live script at a time.
- Repeated handler exceptions auto-stop the live script.

## API surface (`IScriptingApi`)

The authoritative list is [`IScriptingApi.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Services/IScriptingApi.cs) with DTOs in [`ScriptDtos.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Services/ScriptDtos.cs). Grouped summary:

### Output

| Method | Description |
| --- | --- |
| `Log(message)` | Append a line to the Scripts output panel |
| `ClearOutput()` | Clear output (host clears before each Run/Start live) |
| `OutputLines` | Read-only log lines |

### Project lifecycle

| Method | Description |
| --- | --- |
| `ClearProject()` | Reset to an empty project (used by exported scripts) |
| `GetProjectName()` / `SetProjectName(name)` | Project display name |
| `GetTempo()` / `SetTempo(bpm)` | Master tempo |
| `GetTimeSignature()` / `SetTimeSignature(num, den)` | Time signature |
| `GetBarCount()` / `SetBarCount(bars)` | Arrangement length |
| `GetKeySignature()` / `SetKeySignature(root, scale)` | Global key/scale |
| `GetPlaybackMode()` / `SetPlaybackMode(mode)` | Arrangement / Session / Hybrid |
| `GetLaunchQuantizeBeats()` / `SetLaunchQuantizeBeats(beats)` | Session launch grid |
| `GetMpeSettings()` / `SetMpeSettings(...)` | MPE zone config |
| `GetActiveGroove()` / `SetActiveGroove(...)` | Active swing template |

### Transport

| Method | Description |
| --- | --- |
| `GetTransportState()` | `Stopped` or `Playing` |
| `Play()` / `Stop()` | Start/stop playback |
| `SeekToBeat(beat)` | Move start marker and playhead |
| `GetPlayheadBeats()` | Current playhead |
| `GetLoopRegion()` / `SetLoopRegion(start, end)` | Loop region |
| `SetLoopActive(active)` | Enable/disable loop (derived from region) |

### Tracks

| Method | Description |
| --- | --- |
| `GetTracks()` / `GetTrack(id)` | Read track snapshots (kind, mute/solo/arm, routing) |
| `AddInstrumentTrack` / `AddAudioTrack` / `AddGroupTrack` / `AddReturnTrack` / `AddHybridTrack` | Create tracks |
| `Add*WithId(id, ...)` / `AddMasterTrackWithId` | Stable-id variants for portable export |
| `RenameTrack`, `RemoveTrack`, `SetTrackVolume/Pan/Muted/Soloed/Armed/Color/Parent/...` | Track edits |

### Instruments & effects

| Method | Description |
| --- | --- |
| `GetInstruments(trackId)` / `GetEffects(trackId, slot?)` | Read device chains |
| `SetInstrument`, `RemoveInstrument`, `SetInstrumentEnabled` | Instrument rack |
| `SetInstrumentParameter` / `Bool` / `Choice` | Parameter writes |
| `LoadInstrumentPreset(track, slot, name)` | Built-in preset by name |
| `AddEffect`, `RemoveEffect`, `SetEffectEnabled`, `SetEffectParameter`, `LoadEffectPreset` | Insert & pre-FX chains |

### Clips & MIDI

| Method | Description |
| --- | --- |
| `GetClips(trackId?)`, `GetMidiNotes(clipId)`, `GetAudioClipMetadata(clipId)` | Read clips |
| `CreateMidiClip` / `CreateMidiClipWithId`, `CreateAudioClip(WithId)` | Create clips (audio = metadata only) |
| `MoveClip`, `ResizeClip`, `DuplicateClip`, `DeleteClip`, `RenameClip` | Clip edits |
| `AddMidiNote(s)`, `ClearMidiNotes`, `AddMidiControlChange` | Per-note/CC edits |
| `QuantizeAllMidiClips`, `TransposeAllMidiClips`, … | Bulk MIDI transforms |

### Automation, routing, patterns, timeline

| Area | Methods |
| --- | --- |
| Automation | `GetAutomationLanes`, `AddAutomationLane`, `AddAutomationPoint`, `AddTrackModulator` |
| Sends / multi-out | `GetSends`, `AddSend`, `GetMultiOutputRoutes`, `AddMultiOutputRoute` |
| Patterns / session | `GetPatterns`, `AddPatternWithId`, `AddPatternChannel`, `SetPatternSteps`, `Get/AddPatternClip`, `Get/AddSessionClip` |
| Markers / chord / maps / video tracks | `Get/AddMarker`, `Get/AddSection`, `Get/AddChordRegion`, `Get/AddDrumMap`, `Get/AddExpressionMap`, `Get/AddVideoTrack`, `Get/AddControlRoomProfile` |

### Video composition

| Method | Description |
| --- | --- |
| `GetVideoEnabled()` / `SetVideoEnabled(enabled)` | Per-project video on/off |
| `GetVideoElements()` / `AddVideoElement(info)` | Overlay layers (kind, path, bounds, z-order, opacity) |
| `GetVideoTriggers()` / `AddVideoTrigger(info)` | Clip/MIDI-driven show/hide/fade rules |

`ScriptVideoElementInfo` fields: `Id`, `Name`, `Kind` (`Image`, `AnimatedGif`, `Video`, `Waveform`), `SourcePath`, `X`, `Y`, `Width`, `Height`, `Rotation`, `ZOrder`, `Opacity`, `DefaultVisible`.

`ScriptVideoTriggerInfo` fields: `Id`, `TargetElementId`, `Source` (`ArrangementClip`, `SessionClip`, `MidiNote`), `TrackId`, `ClipId`, `MidiNote`, `Moment` (`ClipStart`, `ClipEnd`, `NoteOn`, `NoteOff`), `Action` (`Show`, `Hide`, `Toggle`, `FadeIn`, `FadeOut`), `FadeDurationSeconds`.

Example:

```csharp
api.SetVideoEnabled(true);
var logoId = Guid.NewGuid();
api.AddVideoElement(new ScriptVideoElementInfo(
    logoId, "Logo", ScriptVideoElementKind.Image, "/path/logo.png",
    0.02, 0.02, 0.2, 0.2, 0, 1, 1.0, false));
var clips = api.GetClips();
if (clips.Count > 0)
{
    api.AddVideoTrigger(new ScriptVideoTriggerInfo(
        Guid.NewGuid(), logoId, ScriptVideoTriggerSource.ArrangementClip,
        null, clips[0].Id, null, ScriptVideoTriggerMoment.ClipStart,
        ScriptVideoTriggerAction.FadeIn, 0.5));
}
```

### Export (desktop)

| Method | Description |
| --- | --- |
| `ExportProjectAsScript(options?)` | Full project → portable C# (`api.*` calls) |
| `ExportInstrumentSlotAsScript(track, slot)` | Single instrument slot preset script |
| `ExportEffectChainAsScript(track, slot?)` | Track inserts or slot pre-FX chain |
| `ExportPresetAsScript(track, slot?, effectIndex?)` | Convenience wrapper |

Mutating calls go through [`IHistoryCapture`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Services/IHistoryCapture.cs) so **Undo** restores script changes.

### Live handlers

| Method | Description |
| --- | --- |
| `OnTransportStateChanged(handler)` | Fires on play/stop |
| `OnBeat(handler, gridBeats?)` | Throttled beat grid callbacks (default 1 beat) |
| `OnClipChanged(handler)` | Fires on clip property/note edits |
| `StopLive()` | Dispose all live subscriptions |

## Portable export

Use **Export project** or **Export preset** in the Scripting tab toolbar (or call the export methods from your own scripts).

Generated scripts:

- Emit ordered `api.*` calls with **stable `Guid`s** (`*WithId` helpers) so cross-references (sends, pattern refs, session clips) round-trip
- **Exclude audio PCM** — file paths, warp/fade metadata, and TODO comments for recorded/in-memory audio
- Prefer **built-in preset names** when detectable; otherwise inline parameter literals
- Include project metadata: tempo, key, MPE, patterns, automation, markers, chord track, drum maps, expression maps, video paths, etc.

Example header:

```csharp
// Generated by Ongenet — portable project script (no audio PCM)
// Re-run: Scripting tab → Run. Re-link audio paths marked TODO.
api.ClearProject();
api.SetProjectName("Demo");
api.SetTempo(128);
// ...
```

### Export limitations

- **No audio blobs** — re-link `AudioFilePath` or replace clips manually
- **External plugins** — CLAP/VST `TypeId`s must exist on the target machine
- **Field graphs / exotic state** — parameters export; complex custom state may need manual follow-up
- **Desktop only** — `NullScriptingApi` throws on export methods

## Host contract (`IScriptingHost`)

| Member | Description |
| --- | --- |
| `IsEnabled` | `true` for Roslyn host; `false` for null host |
| `LoadedScripts` | Names of currently loaded scripts |
| `ActiveLiveScript` | Name of the running live script, or null |
| `LoadScript(path)` | Compile a `.cs` file and add it to the cache |
| `LoadScriptFromText(name, code, path?)` | Compile in-memory script text (optional disk path for Save) |
| `UpdateScriptSource(name, code)` | Recompile a loaded script from edited buffer |
| `UnloadScript(name)` | Remove a loaded script |
| `Invoke(name, entryPoint, args?)` | Run batch script (`"Run"`) |
| `StartLive(name, uiContext?)` | Activate live handlers (`"Register"`) |
| `StopLive(name)` | Tear down live handlers |
| `GetScriptPath(name)` / `GetScriptSource(name)` | Reload and preview support |

Implementation: [`RoslynScriptingHost`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Scripting/RoslynScriptingHost.cs), [`ScriptingRuntime`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Scripting/ScriptingRuntime.cs).

## Factory scripts

Shipped under [`Ongenet.App/Scripts/`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Scripts/):

| File | What it does |
| --- | --- |
| `SetTempo120.cs` | Sets tempo to 120 BPM |
| `RenameTracksPrefix.cs` | Prefixes all track names |
| `QuantizeAllMidi.cs` | Quantizes MIDI clips to 1/16 |
| `SetLoopRegion.cs` | Sets a 16-beat loop |
| `TransposeAll.cs` | Transposes MIDI +12 semitones |
| `DuplicateClipsOffset.cs` | Duplicates every clip |
| `LiveTransportLog.cs` | Live transport/beat logging example |
| `ExportProjectExample.cs` | Documents portable project export |
| `ApplyPresetExample.cs` | Load a built-in instrument preset via script |

## Architecture

```
MainWindow → Scripting tab (ScriptingPanelViewModel / ScriptingPanelView)
        │ optional Pop out → ScriptingPopOutWindow (same VM)
        ▼
IScriptEditorFactory ──desktop──► ScriptEditorControl + ScriptEditorSession
        │                              │
        │                              ▼
        │                     ScriptEditorWorkspace (Roslyn IDE services)
        │
        ▼
IScriptingHost  ──desktop──►  RoslynScriptingHost
        │                              │
        │                              ▼
        │                     IScriptingApi (ScriptingApi)
        │                              │
        │                     ScriptingRuntime (live handlers)
        │                              │
        └──web/Android──► NullScriptingHost
                                       │
                    IProjectService / ITransportService / IHistoryCapture
```

`DesktopPlatform` registers `RoslynScriptingHost` and `ScriptingApi`. Shared `App` DI falls back to null stubs for non-desktop heads.

## Security & limits

- Scripts run **in-process** with full project access — only run scripts you trust
- No file I/O or network API is exposed on `IScriptingApi`
- Live/audio-thread callbacks are **not** supported — UI-thread handlers only; no per-sample DSP
- Compile errors surface as squiggles in the editor and in the status line

## In-app IDE

Open the **Scripting** centre tab (after **Video**) for the full workflow:

- **Script list** — factory scripts under the app folder plus `Documents/Ongenet/Scripts`
- **Editor** — custom in-house control (`Ongenet.Scripting.Editor`) with line numbers, Roslyn syntax highlighting, diagnostics, completion (`.` and Ctrl+Space), and signature help
- **Output** — `api.Log()` and live handler output
- **Export project** / **Export preset** — generate portable C# from the open project or selected track
- **Pop out** — optional separate window bound to the same singleton view model

Run and Start live flush unsaved buffer text to the host via `UpdateScriptSource` (no manual Save required).

## Extending the API

1. Add methods to [`IScriptingApi`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Services/IScriptingApi.cs) and DTOs to [`ScriptDtos.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Services/ScriptDtos.cs).
2. Implement them in [`ScriptingApi`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Scripting/ScriptingApi.cs) (capture undo for mutations).
3. Document the extended API here and add a factory script example under `Ongenet.App/Scripts/`.
4. If export-related, update [`ProjectScriptExporter`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Scripting/Export/ProjectScriptExporter.cs) / [`PresetScriptExporter`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Scripting/Export/PresetScriptExporter.cs).
5. Cover load/invoke in [`ScriptingHostTests`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core.Tests/Services/ScriptingHostTests.cs) and [`ScriptingApiTests`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core.Tests/Services/ScriptingApiTests.cs).

## Related

- [Main window layout](main-window-layout.md) — centre Scripting tab
- [Web demo](web-demo.md) — scripting is stubbed in the browser
