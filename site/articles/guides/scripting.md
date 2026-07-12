# Scripting

## Quick steps

1. Open **Tools → Scripts** from the menu bar.
2. Pick a factory script or write C# in the editor panel.
3. Click **Run** to execute against the current project via the curated `IScriptingApi`.
4. Save custom scripts to your user scripts folder for reuse.

## Details

Ongenet embeds a Roslyn C# scripting host with a sandboxed API surface:

- **Project API** — tempo, time signature, track names, clip operations
- **Transport API** — play, stop, seek
- **Factory scripts** — rename tracks, set tempo, quantize clips (starting points)

Scripts cannot access arbitrary filesystem paths or spawn processes — see [Dev: Scripting](/dev/scripting.html) for the full API and security limits.

### Example

```csharp
// Set project tempo to 128 BPM
api.Project.Tempo = 128;
```

## Related

- [Getting started](getting-started.md)
- [Dev: Scripting API](/dev/scripting.html)
- [Keyboard shortcuts](keyboard-shortcuts.md)
