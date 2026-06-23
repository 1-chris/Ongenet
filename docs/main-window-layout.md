# Main window layout & controls

A tour of Ongenet's main window — every region, what each control does, and the complete list of
keyboard shortcuts. This is a user-facing reference, but it points at the source files for each region
so developers can find their way around too.

The whole UI is the shared Avalonia library [`Ongenet.App`](../Ongenet.App). The main window itself is
[`Views/Windows/MainWindow.axaml`](../Ongenet.App/Views/Windows/MainWindow.axaml), composed by
[`ViewModels/MainViewModel.cs`](../Ongenet.App/ViewModels/MainViewModel.cs).

---

## The big picture

Ongenet uses **custom window chrome** (its own title bar, themed with Catppuccin — see
[theming.md](theming.md)) and a five-region layout:

```
┌───────────────────────────────────────────────────────────────────────┐
│  Title bar:  Ongenet  New Open Save SaveAs  Undo Redo  History Logs ⚙  │
├───────────────────────────────────────────────────────────────────────┤
│  Transport bar:  ▶ ■ ●  [ ]  tempo  bars  time-sig   master meter      │
├──────────────┬──────────────────────────────────────────┬─────────────┤
│              │                                            │  R          │
│  Track       │            Timeline / arrange              │  i  Library │
│  inspector   │       (ruler, track headers, clips)        │  g  sidebar │
│  (left)      │                                            │  h  (right) │
│              │                                            │  t          │
├──────────────┴──────────────────────────────────────────┤  tabs       │
│  Bottom panel:  [ Instrument | Piano Roll | Effects ]     │             │
└───────────────────────────────────────────────────────────────────────┘
```

Each side region can be collapsed:

- **Left** (track inspector) — toggle with the **◂ / ▸** button.
- **Right** (library) — toggle with the **▸ / ◂** button. It **starts collapsed** to just its tab
  strip; click a tab to open it.
- **Bottom** (editors) — toggle with the **▾ / ▴** button.

Drag the 4px splitters between regions to resize them. Collapse logic lives in
[`MainWindow.axaml.cs`](../Ongenet.App/Views/Windows/MainWindow.axaml.cs).

---

## 1. Title bar

The top strip (custom-drawn, so it looks the same on every OS; macOS gets native "traffic light"
buttons). Left to right:

| Control | Action |
| --- | --- |
| **Ongenet** + version | App name and current version number |
| **New** | Start a new project |
| **Open** | Open a `.ongen` project file |
| **Save** / **Save As** | Save the project |
| **↶ Undo** / **↷ Redo** | Step through edit history (greyed out when nothing to undo/redo) |
| **History** | Open the undo-stack browser ([`HistoryWindow`](../Ongenet.App/Views/Windows/HistoryWindow.axaml)) |
| **Logs** | Open the debug log viewer ([`LogWindow`](../Ongenet.App/Views/Windows/LogWindow.axaml)) |
| **Settings** | Open Settings (Audio / MIDI / Theme / Library) |
| window buttons | Minimise / maximise / close |

A busy indicator appears here during long save/load operations. **Double-click** the title bar to
maximise/restore.

---

## 2. Transport bar

The playback control strip ([`Views/Panels/TransportView.axaml`](../Ongenet.App/Views/Panels/TransportView.axaml),
[`ViewModels/TransportViewModel.cs`](../Ongenet.App/ViewModels/TransportViewModel.cs)):

| Control | What it does |
| --- | --- |
| **Render** | Export the arrangement to a WAV file (offline render) |
| **Play** ▶ | Start playback from the start marker |
| **Stop** ■ | Stop playback; if recording, stop and commit the take |
| **Record** ● | One-bar metronome count-in, then record into armed tracks |
| **Playhead time** | Current playback position (updates ~10×/sec while playing) |
| **Total time** | Length of the arrangement |
| **Slice toggle** | Switch the timeline/piano-roll into "slice" (cut) edit mode |
| **[** / **]** | Set the loop start / loop end to the current start marker |
| **Loop indicator** | A green dot lights when looping is active |
| **Tempo** | Project BPM (20–300) |
| **Bars** | Arrangement length in bars |
| **Time signature** | Numerator + denominator (denominators 1/2/4/8/16) |
| **Master meter** | Live stereo output level (read-only — there is no master *fader* here) |

> The **metronome** only clicks during the recording count-in; there is no standalone metronome toggle.
> **Audio/MIDI device pickers** live in **Settings → Audio / MIDI**, not on the transport.

---

## 3. Track inspector (left panel)

Settings for the **currently selected track**
([`Views/Panels/TrackInspectorView.axaml`](../Ongenet.App/Views/Panels/TrackInspectorView.axaml)). When
nothing is selected it shows "Select a track to edit its settings." When a track is selected:

- **Name** — rename the track.
- **Mute** / **Solo** — silence this track, or solo it.
- **Volume** — track level (0–100%, with a readout).
- **Pan** — left/right placement (−1…1).
- **Colour** — pick the track's colour (used in the timeline).

This is the main place you set per-track volume and pan. **Right-click** the volume or pan control to
**MIDI-learn** it (move a knob on your controller to bind it).

---

## 4. Timeline / arrange view (centre)

The main editing surface where you lay out clips over time
([`Views/Panels/TimelineView.axaml`](../Ongenet.App/Views/Panels/TimelineView.axaml),
[`TimelineView.axaml.cs`](../Ongenet.App/Views/Panels/TimelineView.axaml.cs)). Regions:

- **Bar ruler** (top) — bar numbers. Click it to set the **start marker** (a green triangle / grey
  line marking where playback begins).
- **Track headers** (left, per track) — track name, colour rail, a collapse chevron, **M** (mute),
  **S** (solo), **●** (record arm), and a live level meter. Right-click for a menu: group, duplicate,
  delete, add instrument/audio track.
- **Lane area** (centre) — the clips themselves on each track, plus automation curves on automation
  lanes.
- **Overlays** — the green **playhead** line, the start marker, a sky-blue **loop region** band when
  looping, and a rubber-band selection rectangle.

Clip interactions (mouse):

| Action | Result |
| --- | --- |
| Click a clip | Select it |
| Drag a clip body | Move it (snapped to the grid) |
| Drag a clip's left/right edge | Resize / trim it |
| Double-click an empty instrument lane | Create a new MIDI clip |
| Ctrl+click a clip (or use Slice mode) | Slice the clip at that point |
| Right-click a clip | Menu: Duplicate, Reverse (audio), Delete |
| Drag from the ruler / empty lane | Rubber-band select multiple clips |
| Middle-drag | Zoom (vertical) and pan (horizontal) |
| Drag audio from the library | Drop onto a lane, or create a new track |
| Drag a track header | Reorder / group tracks |
| Ctrl+click a track header | Add/remove it from a multi-selection (for grouping) |

Audio clips show a waveform with fade handles; MIDI clips show a mini note preview.

---

## 5. Piano roll (bottom panel · "Piano Roll" tab)

The MIDI note editor for the selected MIDI clip
([`Views/Panels/PianoRollView.axaml`](../Ongenet.App/Views/Panels/PianoRollView.axaml)):

- **Toolbar** — Import MIDI, Export MIDI, a melody/chord **Generator**, the **Edit / Select / Slice**
  tools, and **Arpeggio**.
- **Key gutter** (left) — a vertical piano keyboard; click a key to audition that pitch.
- **Grid** — draw and edit notes.

Tools (these share the same slice mode as the timeline):

- **Edit** — click an empty spot to draw a note; drag a note to move it; drag its edge to resize.
- **Select** — rubber-band to select multiple notes for bulk edits.
- **Slice** — drag a cut line to split notes.

Mouse extras: **right-click** a note to delete it; **middle-drag** to zoom/pan. The **Generator** and
**Arpeggio** buttons open helper windows
([`MidiGeneratorWindow`](../Ongenet.App/Views/Windows/MidiGeneratorWindow.axaml),
[`ArpeggioWindow`](../Ongenet.App/Views/Windows/ArpeggioWindow.axaml)).

---

## 6. The "mixer" — where mixing actually lives

There is **no separate mixer window** with channel strips. Mixing is spread across the places you're
already working:

| Where | What you control |
| --- | --- |
| Track inspector (left) | Volume, pan, mute, solo for the selected track |
| Timeline track headers | M / S / record-arm and a live meter per track |
| Transport bar | The master output meter (read-only) |
| Effects tab (bottom) | The track's **post** effect chain |
| Instrument slot (bottom) | Each instrument's **pre** effect chain |
| Group / bus tracks | Tracks route into group buses (and the master); set a track's parent to build the bus tree |

The actual mixing math is in [`Ongenet.Core/Audio/Mixing.cs`](../Ongenet.Core/Audio/Mixing.cs) and the
engine; the routing model (tracks → group buses → master) is explained in
[audio-engine.md](audio-engine.md).

---

## 7. Library / browser (right panel)

A collapsible sidebar with vertical tabs. Click a tab to open it; the panel starts collapsed to just
the tab strip. Tabs:

| Tab | Shows |
| --- | --- |
| **Everything** | A combined view of all library content |
| **Files** | An OS folder browser |
| **Samples** | Your sample library |
| **Soundfonts** | SF2 soundfonts |
| **Instruments** | Built-in (and plugin) instruments |
| **Effects** | Built-in (and plugin) effects |
| **Inst Presets** / **FX Presets** / **FX Chains** | Saved presets and effect-chain presets |

Below the list sits a **preview panel** (audition samples with a waveform) and, on the
sample/file tabs, **library options** (auto-stretch to project tempo, pitch correction). Library items
are **draggable** straight onto the timeline — drop an instrument to add it to a track's rack, or a
sample onto an audio lane. Configure which folders are scanned in **Settings → Library**.

---

## 8. Inspectors (bottom panel)

The bottom panel is a tabbed editor that changes with what you've selected
([`ViewModels/BottomPanelViewModel.cs`](../Ongenet.App/ViewModels/BottomPanelViewModel.cs)):

| Tab | Shows | When |
| --- | --- | --- |
| **Instrument** / **Sample** | The instrument rack, or the selected audio clip's settings | Instrument tracks (header switches to "Sample" when an audio clip is selected) |
| **Piano Roll** | The MIDI note editor | Auto-selected when you pick a MIDI clip |
| **Effects** | The track's effect chain | Always (the only tab for bus tracks) |

The panel switches tabs automatically: select a MIDI clip and it jumps to Piano Roll; an audio clip to
Sample; a bus shows only Effects.

**Instrument inspector** ([`InstrumentInspectorView`](../Ongenet.App/Views/Panels/InstrumentInspectorView.axaml)):
an **"+ Add instrument"** menu (grouped by category), a rack of instrument-slot cards, and a shared
mini keyboard. Each [slot card](../Ongenet.App/Views/Panels/InstrumentSlotView.axaml) has an
enable dot, a preset picker, sample/SFZ loaders (for samplers), a "open plugin UI" button (for
plugins), the parameter knobs, and a per-instrument **pre**-effect chain.

**Effects inspector** ([`EffectsView`](../Ongenet.App/Views/Panels/EffectsView.axaml) →
[`EffectChainView`](../Ongenet.App/Views/Panels/EffectChainView.axaml)): the track's chain of effects.
Add effects from a categorised menu or by dragging from the library; each effect has a bypass toggle, a
reorder handle, an order badge, and a "save preset" button.

**Parameter knobs** ([`Knob`](../Ongenet.App/Controls/Knob.cs)) appear throughout. **Right-click** any
knob, slider or switch to **MIDI-learn** it or **create an automation track** for it.

---

## 9. Settings & secondary windows

Opened from the title bar (or the piano roll):

| Window | Purpose |
| --- | --- |
| [Settings](../Ongenet.App/Views/Windows/SettingsWindow.axaml) | Audio device, MIDI devices/mappings, Theme editor, Library folders |
| [History](../Ongenet.App/Views/Windows/HistoryWindow.axaml) | Browse and jump around the undo stack |
| [Logs](../Ongenet.App/Views/Windows/LogWindow.axaml) | Debug log viewer |
| [MIDI Generator](../Ongenet.App/Views/Windows/MidiGeneratorWindow.axaml) | Generate chords/melodies into a clip |
| [Arpeggio](../Ongenet.App/Views/Windows/ArpeggioWindow.axaml) | Arpeggiate the selected notes |
| [Plugin](../Ongenet.App/Views/Windows/PluginWindow.axaml) | Host a native plugin's own GUI |

---

## 10. Keyboard shortcuts (complete list)

Ongenet's shortcuts are defined in C# handlers (not XAML key bindings). Below is the full set.

### Global (anywhere except while typing in a text box)

Defined in [`MainWindow.axaml.cs`](../Ongenet.App/Views/Windows/MainWindow.axaml.cs).

| Shortcut | Action |
| --- | --- |
| **Ctrl + N** | New project |
| **Ctrl + O** | Open project |
| **Ctrl + S** | Save project |
| **Ctrl + Shift + S** | Save As |
| **Ctrl + Z** | Undo |
| **Ctrl + Shift + Z** | Redo |
| **Ctrl + Y** | Redo (alternative) |
| **Space** | Play / stop toggle |
| **Shift + [** | Set loop **start** to the start marker |
| **Shift + ]** | Set loop **end** to the start marker |
| **F8** | Toggle Avalonia renderer diagnostics (FPS / render & layout graphs) |

> Set the environment variable `ONGENET_FPS=1` to auto-enable the F8 diagnostics overlay at startup.
> (Avalonia DevTools — the visual-tree inspector — is **F12** in Debug builds; see
> [DEVELOPMENT.md](../DEVELOPMENT.md).)

### Timeline (when the timeline has focus — click a lane first)

| Shortcut | Action |
| --- | --- |
| **Delete** | Delete the selected clip(s) |
| **Ctrl + D** | Duplicate the selected clip |

### Piano roll (when the piano roll has focus)

| Shortcut | Action |
| --- | --- |
| **Delete** | Delete the selected note(s) |

### Typing keyboard (play notes from your computer keys)

When the selected track is an **instrument** track (and you're not typing in a text box), unmodified
keys play notes, FL-Studio style — `NoteOn` on key down, `NoteOff` on release. The map lives in
[`Input/ComputerKeyboard.cs`](../Ongenet.App/Input/ComputerKeyboard.cs); base note is **middle C (60)**:

- **Lower row (from C4):** `Z S X D C V G B H N J M , L . ; /` — semitones 0–16
- **Upper row (from C5):** `Q 2 W 3 E R 5 T 6 Y 7 U I 9 O 0 P [ + ]` — semitones 12–31

Holding Ctrl/Shift/Alt with these keys never triggers a note, so they never clash with shortcuts like
Ctrl+D or Shift+[.

### Mouse modifiers (not keys, but handy to know)

| Input | Where | Action |
| --- | --- | --- |
| **Ctrl + click** | Timeline track header | Toggle the track in a multi-selection |
| **Ctrl + click** | Timeline clip | Slice the clip at the grid |
| **Middle-drag** | Timeline / piano roll | Zoom and pan |

### On the web build

The [WebAssembly demo](web-demo.md) supports **Space** (play/stop) and the typing keyboard, but not the
file/undo/loop/delete shortcuts.

---

## Source-file quick reference

```
Ongenet.App/Views/Windows/MainWindow.axaml(.cs)   — overall layout, panel collapse, global shortcuts
Ongenet.App/ViewModels/MainViewModel.cs           — composes all panel view models
Ongenet.App/Views/Panels/TransportView.axaml      — transport bar
Ongenet.App/Views/Panels/TimelineView.axaml(.cs)  — arrange view + timeline shortcuts
Ongenet.App/Views/Panels/PianoRollView.axaml(.cs) — piano roll + its shortcuts
Ongenet.App/Views/Panels/TrackInspectorView.axaml — left inspector
Ongenet.App/Views/Panels/InstrumentInspectorView.axaml / InstrumentSlotView.axaml
Ongenet.App/Views/Panels/EffectsView.axaml / EffectChainView.axaml
Ongenet.App/Views/Panels/LibraryListView.axaml / FileBrowserView.axaml / PreviewPanelView.axaml
Ongenet.App/ViewModels/BottomPanelViewModel.cs    — bottom-tab switching logic
Ongenet.App/Input/ComputerKeyboard.cs             — typing-keyboard note map
Ongenet.App/Controls/                             — the custom DAW controls (knob, meters, waveform, …)
```

---

## Where to go next

- [theming.md](theming.md) — how the colours you see are themed, and how to add your own theme.
- [audio-engine.md](audio-engine.md) — what happens to the audio behind this UI.
- [creating-instruments.md](creating-instruments.md) / [creating-effects.md](creating-effects.md) —
  build the instruments and effects that appear in the inspectors above.
