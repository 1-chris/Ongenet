# Polyphonic pitch editing

Ongenet includes a built-in **VariAudio-class** pitch editor — no third-party Melodyne licence
required. Each audio clip stores a list of `PitchNoteSegment` objects with start/end sample
positions and per-note pitch offset in cents.

## Opening the Pitch Editor

Right-click an audio clip on the timeline → **Open Pitch Editor**.

## Workflow

1. **Analyze** — runs polyphonic onset/pitch detection (seeded from the same pipeline as
   **Convert to poly MIDI**) and populates note segments on the waveform
2. **Edit** — drag segment pitch (cents slider or vertical drag), adjust start/end edges, delete
   spurious segments, or add segments manually
3. **Listen** — changes apply in real time during playback via per-segment pitch ratios with
   256-sample crossfades at segment boundaries
4. **Flatten** (optional) — bake pitch-corrected audio to a new clip via **Render clip to new track**

## Fallback behaviour

- When no segments cover a frame, playback falls back to the global `AraPitchOffsetSemitones`
  (monophonic ARA offset) if set
- Monophonic clips can still use **Open ARA Editor** when an ARA-capable plugin is on the track

## Persistence

Pitch segments are saved in the project file (format v7, append-only after linked-clip metadata).
Clones sharing the same audio buffer share segment lists through the clip model.

## Limitations vs Melodyne

- Detection quality depends on source material — manual editing is the reliability backstop
- No formant preservation slider (pitch shift uses the Rubber Band R2 engine path)
- Optional ARA2 SDK integration (`ENABLE_ARA`) remains available for third-party ARA plugins but
  is not required for the built-in editor

## Architecture

| Piece | Role |
| --- | --- |
| [`PitchNoteSegment`](../Ongenet.Core/Models/Audio/PitchNoteSegment.cs) | Start/end sample, pitch cents, amplitude |
| [`Clip.PitchSegments`](../Ongenet.Core/Models/Audio/Clip.cs) | Per-clip segment list (project format v7) |
| [`PolyphonicPitchAnalyzer`](../Ongenet.Core/Audio/Files/PolyphonicPitchAnalyzer.cs) | Seeds segments from polyphonic detection |
| [`AudioClipPitch`](../Ongenet.Core/Audio/AudioClipPitch.cs) | Real-time per-segment ratios + edge crossfades |
| [`PolyphonicPitchEditorWindow`](../Ongenet.App/Views/Windows/PolyphonicPitchEditorWindow.axaml) | Analyze / edit UI with waveform overlays |

## Related

- [Audio Editor](audio-editor.md) — destructive sample surgery (separate from pitch segments)
- [Main window layout](main-window-layout.md)
