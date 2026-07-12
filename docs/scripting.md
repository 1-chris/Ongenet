# Scripting

Ongenet ships a **C# scripting host** (`Ongenet.Scripting`) for batch automation — similar in spirit to
Max for Live scripts, but simpler: run `.cs` files against the open project without restarting the app.

Desktop only. Web and Android register [`NullScriptingHost`](../Ongenet.Core/Services/IScriptingHost.cs).

## Opening the Scripts panel

**Tools → Scripts** opens the Scripts window. Factory scripts auto-load from the `Scripts/` folder
next to the desktop executable (copied from [`Ongenet.App/Scripts/`](../Ongenet.App/Scripts/) on build).

Use **Load…** to pick additional `.cs` files, then **Run** on the selected script.

## Writing a script

Scripts compile and run via [Roslyn](https://github.com/dotnet/roslyn) (`Microsoft.CodeAnalysis.CSharp.Scripting`).
A global `api` object implementing [`IScriptingApi`](../Ongenet.Core/Services/IScriptingApi.cs) is available:

```csharp
// Set project tempo to 128 BPM
api.SetTempo(128.0);

// Rename every track with a prefix
foreach (var pair in api.GetTrackNames())
    api.RenameTrack(pair.Key, "Mix — " + pair.Value);

// Quantize all MIDI clips to 1/16 (0.25 beats)
api.QuantizeAllMidiClips(0.25);
```

Imports available by default: `System`, `System.Collections.Generic`, `Ongenet.Core.Services`.

### API surface (`IScriptingApi`)

| Method | Description |
| --- | --- |
| `GetProjectName()` | Current project display name |
| `SetTempo(double bpm)` | Set master tempo (captures undo) |
| `GetTrackNames()` | `IReadOnlyDictionary<Guid, string>` of track id → name |
| `RenameTrack(Guid id, string name)` | Rename a track by id (captures undo) |
| `QuantizeAllMidiClips(double gridBeats)` | Snap all MIDI note starts to the given grid (captures undo) |

Mutating calls go through [`IHistoryCapture`](../Ongenet.Core/Services/IHistoryCapture.cs) so **Undo** restores script changes.

### Host contract (`IScriptingHost`)

| Member | Description |
| --- | --- |
| `IsEnabled` | `true` for Roslyn host; `false` for null host |
| `LoadedScripts` | Names of currently loaded scripts (filename without extension) |
| `LoadScript(path)` | Compile a `.cs` file and add it to the cache |
| `UnloadScript(name)` | Remove a loaded script |
| `Invoke(scriptName, entryPoint, args?)` | Run the script; entry point must be `"Run"` (or empty) |

Implementation: [`RoslynScriptingHost`](../Ongenet.Scripting/RoslynScriptingHost.cs).

## Factory scripts

Shipped under [`Ongenet.App/Scripts/`](../Ongenet.App/Scripts/):

| File | What it does |
| --- | --- |
| [`SetTempo120.cs`](../Ongenet.App/Scripts/SetTempo120.cs) | Sets tempo to 120 BPM |
| [`RenameTracksPrefix.cs`](../Ongenet.App/Scripts/RenameTracksPrefix.cs) | Prefixes all track names |
| [`QuantizeAllMidi.cs`](../Ongenet.App/Scripts/QuantizeAllMidi.cs) | Quantizes MIDI clips to 1/16 |

## Architecture

```
Tools → Scripts (ScriptsViewModel / ScriptsWindow)
        │
        ▼
IScriptingHost  ──desktop──►  RoslynScriptingHost
        │                              │
        │                              ▼
        │                     IScriptingApi (ScriptingApi)
        │                              │
        └──web/Android──► NullScriptingHost
                                       │
                    IProjectService / ITransportService / IHistoryCapture
```

`DesktopPlatform` registers `RoslynScriptingHost` and `ScriptingApi`. Shared `App` DI falls back to `NullScriptingHost` for non-desktop heads.

## Security & limits

- Scripts run **in-process** with full project access — only run scripts you trust
- No file I/O or network API is exposed on `IScriptingApi`
- Live audio-thread callbacks are **not** supported — batch/automation only
- Compile errors surface as exceptions in the Scripts window status line

## Extending the API

1. Add methods to [`IScriptingApi`](../Ongenet.Core/Services/IScriptingApi.cs).
2. Implement them in [`ScriptingApi`](../Ongenet.Scripting/ScriptingApi.cs) (capture undo for mutations).
3. Document the extended API here and add a factory script example under `Ongenet.App/Scripts/`.
4. Cover load/invoke in [`ScriptingHostTests`](../Ongenet.Core.Tests/Services/ScriptingHostTests.cs).

## Related

- [Main window layout](main-window-layout.md) — Tools menu
- [Web demo](web-demo.md) — scripting is stubbed in the browser
