# Audio Editor

The **Audio Editor** is Ongenet's Edison-class standalone sample editor. It shares the same waveform
editing engine as the bottom-panel **Sample Inspector** via `SampleEditorCoreViewModel`.

## Opening the editor

- **View → Audio Editor** in the main menu (opens the selected audio clip if one is selected)
- Right-click an audio clip on the timeline → **Open in Audio Editor**
- Open multiple clips as **lanes** — each lane has its own editor instance

## Editing tools

All destructive edits apply to the shared PCM buffer when multiple clips reference the same source
(the yellow shared-sample warning appears in both the inspector and the editor).

| Tool | Description |
| --- | --- |
| Trim | Drag clip edges on the waveform; commits on release |
| Selection | Click-drag to select a region |
| Cut / Copy / Paste / Delete | Standard clipboard ops (`Ctrl+X/C/V`, `Delete`) |
| Move | Drag a selected region horizontally |
| Spectral | FFT magnitude overlay toggle |
| Snap | Snap trim/selection to the musical grid |
| Region gain / pan | Knobs when a stereo selection is active |
| Normalize / Fade in / Fade out / Stretch selection | Toolbar buttons below the waveform |

## Relationship to Sample Inspector

The **Sample Inspector** (bottom panel when an audio clip is selected) adds tempo/stretch/warp/key
tools on top of the shared editor core. Use the inspector for clip tempo context; use the Audio Editor
when you want a dedicated multitrack window for sample surgery.

## Architecture

| Piece | Role |
| --- | --- |
| [`SampleEditorCoreViewModel`](../Ongenet.App/ViewModels/SampleEditorCoreViewModel.cs) | Shared waveform state, cut/copy/paste, spectral overlay, audition |
| [`SampleInspectorViewModel`](../Ongenet.App/ViewModels/SampleInspectorViewModel.cs) | Thin wrapper — tempo, warp, key detect/change |
| [`AudioEditorViewModel`](../Ongenet.App/ViewModels/AudioEditorViewModel.cs) | Multitrack lanes; one core editor per open clip |
| [`SampleEditorView`](../Ongenet.App/Views/Panels/SampleEditorView.axaml) | Reusable AXAML host for the waveform toolbar |
| [`IAudioEditorService`](../Ongenet.App/Services/IAudioEditorService.cs) | Opens/activates the standalone window from menus and the timeline |

## Related

- [Main window layout](main-window-layout.md)
- [Polyphonic pitch editing](polyphonic-pitch.md) — note-segment pitch editor on the same clips
