# Main window layout & controls

A tour of Ongenet's main window — every region, what each control does, and the complete list of
keyboard shortcuts. This is a user-facing reference, but it points at the source files for each region
so developers can find their way around too.

The whole UI is the shared Avalonia library [`Ongenet.App`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App). The main window itself is
[`Views/Windows/MainWindow.axaml`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Windows/MainWindow.axaml), composed by
[`ViewModels/MainViewModel.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/ViewModels/MainViewModel.cs).

### Accessibility

Core regions expose Avalonia `AutomationProperties` names and help text for screen readers:

- **Transport** — Play, Stop, Record, tempo, export, metronome, master volume
- **Timeline** — arrangement landmark and track lanes
- **Mixer** — channel strips and routing controls
- **Settings** — tabbed preferences (Audio, MIDI, Control Surface, Theme, Library)

Use keyboard focus and the shortcuts below for mouse-free operation.

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
[`MainWindow.axaml.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Windows/MainWindow.axaml.cs).

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
| **History** | Open the undo-stack browser ([`HistoryWindow`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Windows/HistoryWindow.axaml)) |
| **Logs** | Open the debug log viewer ([`LogWindow`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Windows/LogWindow.axaml)) |
| **Settings** | Open Settings (Audio / MIDI / Theme / Library) |
| window buttons | Minimise / maximise / close |

A busy indicator appears here during long save/load operations. **Double-click** the title bar to
maximise/restore.

---

## 2. Transport bar

The playback control strip ([`Views/Panels/TransportView.axaml`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Panels/TransportView.axaml),
[`ViewModels/TransportViewModel.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/ViewModels/TransportViewModel.cs)):

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
| **Master meter + fader** | Live stereo output level meter and master volume slider |
| **Metronome (Click)** | Toggle audible click during playback (independent of record count-in) |
| **MIDI export** | Export all instrument-track MIDI from the arrangement to a Standard MIDI File |

> During **record**, a one-bar metronome count-in still runs before capture begins. The **Click** toggle is for everyday playback practice.
> **Audio/MIDI device pickers** live in **Settings → Audio / MIDI**, not on the transport.

---

## 3. Track inspector (left panel)

Settings for the **currently selected track**
([`Views/Panels/TrackInspectorView.axaml`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Panels/TrackInspectorView.axaml)). When
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
([`Views/Panels/TimelineView.axaml`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Panels/TimelineView.axaml),
[`TimelineView.axaml.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Panels/TimelineView.axaml.cs)). Regions:

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
| Right-click a clip | Menu: Rename, Duplicate, Make unique (when shared), Reverse (audio), Delete |
| Drag from the ruler / empty lane | Rubber-band select multiple clips |
| Middle-drag | Zoom (vertical) and pan (horizontal) |
| Drag audio from the library | Drop onto a lane, or create a new track |
| Drag a track header | Reorder / group tracks |
| Ctrl+click a track header | Add/remove it from a multi-selection (for grouping) |

Audio clips show a waveform with fade handles; MIDI clips show a mini note preview.

---

## 5. Piano roll (bottom panel · "Piano Roll" tab)

The MIDI note editor for the selected MIDI clip
([`Views/Panels/PianoRollView.axaml`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Panels/PianoRollView.axaml)):

- **Toolbar** — Import MIDI, Export MIDI, a melody/chord **Generator**, **Quantize** and **Groove**,
  an **Expression** lane toggle (MPE), the **Edit / Select / Slice**
  tools, and **Arpeggio**.
- **Key gutter** (left) — a vertical piano keyboard; click a key to audition that pitch.
- **Grid** — draw and edit notes.

Tools (these share the same slice mode as the timeline):

- **Edit** — click an empty spot to draw a note; drag a note to move it; drag its edge to resize.
- **Select** — rubber-band to select multiple notes for bulk edits.
- **Slice** — drag a cut line to split notes.

Mouse extras: **right-click** a note to delete it; **middle-drag** to zoom/pan. The **Generator** and
**Arpeggio** buttons open helper windows
([`MidiGeneratorWindow`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Windows/MidiGeneratorWindow.axaml),
[`ArpeggioWindow`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Windows/ArpeggioWindow.axaml)).

---

## 6. The "mixer" — where mixing actually lives

There is a dedicated **Mixer** centre tab with channel strips, sends, meters, per-track **input monitoring**
(Off/Auto/On on audio tracks), and an editable **Routing Matrix**
window for bus and multi-out routing. Mixing is also available from:

| Where | What you control |
| --- | --- |
| Track inspector (left) | Volume, pan, mute, solo for the selected track |
| Timeline track headers | M / S / record-arm and a live meter per track |
| Transport bar | Master output meter and master volume fader |
| Effects tab (bottom) | The track's **post** effect chain |
| Instrument slot (bottom) | Each instrument's **pre** effect chain |
| Group / bus tracks | Tracks route into group buses (and the master); set a track's parent to build the bus tree |

The actual mixing math is in [`Ongenet.Core/Audio/Mixing.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.Core/Audio/Mixing.cs) and the
engine; the routing model (tracks → group buses → master) is explained in
[audio-engine.md](audio-engine.md).

### Instrument rack (track inspector)

On **instrument** tracks, the left **Track Controls** inspector includes an **Instrument rack** section:
eight macro knobs and an optional **drum pad grid** (16 pads with MIDI note triggers). Switch between
standard rack and drum grid from the rack kind dropdown.

### Hybrid tracks

**Add Hybrid Track** (timeline context menu) creates a lane that accepts **both** audio and MIDI
clips — Ableton/Bitwig-style hybrid workflows without duplicating tracks.

### Specialist windows (View menu)

| Window | Purpose |
| --- | --- |
| **Tempo Map** | Master tempo automation |
| **Section Playlist** | Ordered arrangement-marker playback with reorder/duplicate |
| **Chord Track** | Global harmony regions applied to MIDI clips |
| **Expression Maps** | VST expression / keyswitch articulation maps |
| **Audio Editor** | Edison-class multitrack sample editor — clip list + shared waveform tools ([audio-editor.md](audio-editor.md)) |
| **Pitch Editor** | Polyphonic note-segment pitch correction on audio clips (timeline → **Open Pitch Editor**) |
| **Scripting** | **Scripting** centre tab — in-app C# IDE with syntax highlighting and IntelliSense ([scripting.md](scripting.md)) |

**Settings → General → Plugins → Isolate plugins in separate process** enables optional VST3 effect
crash isolation via `Ongenet.PluginHost` ([plugin-isolation.md](plugin-isolation.md)).

**Export → ADM BWF** (when surround is 5.1/7.1) exports ITU-R BS.2076 immersive handoff. **AAF/OMF
handoff** exports structured XML for post houses.

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
([`ViewModels/BottomPanelViewModel.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/ViewModels/BottomPanelViewModel.cs)):

| Tab | Shows | When |
| --- | --- | --- |
| **Instrument** / **Sample** | The instrument rack, or the selected audio clip's settings | Instrument tracks (header switches to "Sample" when an audio clip is selected) |
| **Piano Roll** | The MIDI note editor | Auto-selected when you pick a MIDI clip |
| **Effects** | The track's effect chain | Always (the only tab for bus tracks) |

The panel switches tabs automatically: select a MIDI clip and it jumps to Piano Roll; an audio clip to
Sample; a bus shows only Effects.

**Instrument inspector** ([`InstrumentInspectorView`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Panels/InstrumentInspectorView.axaml)):
an **"+ Add instrument"** menu (grouped by category), a rack of instrument-slot cards, and a shared
mini keyboard. Each [slot card](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Panels/InstrumentSlotView.axaml) has an
enable dot, a preset picker, sample/SFZ loaders (for samplers), a "open plugin UI" button (for
plugins), the parameter knobs, and a per-instrument **pre**-effect chain.

**Effects inspector** ([`EffectsView`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Panels/EffectsView.axaml) →
[`EffectChainView`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Panels/EffectChainView.axaml)): the track's chain of effects.
Add effects from a categorised menu or by dragging from the library; each effect has a bypass toggle, a
reorder handle, an order badge, and a "save preset" button.

**Sample inspector** ([`SampleInspectorView`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Panels/SampleInspectorView.axaml)):
shown when an audio clip is selected. Includes a tempo-aware waveform editor with bar/beat grid and ruler
labels, a hover cursor line (with bar/time readout in the toolbar), and snap-to-beat for pointer edits
(toggle **Snap** in the toolbar to disable). **Middle-mouse drag** zooms vertically and pans horizontally
(same gesture as the timeline); a horizontal scrollbar appears when zoomed in. A **Play / Stop** button
auditions the sample through the output device without engaging the transport (plays the selection when one
exists, otherwise the whole sample); a red **playhead** line tracks audition progress in the waveform. Draggable trim handles
(destructive crop on release), click-drag selection, segment move (drag inside a selection), and Cut / Copy /
Paste / Delete toolbar buttons. When a time range is selected, a **region edit** strip offers gain
(−24…+24 dB), pan (stereo), **Swap L/R**, and **Reverse** controls that apply only to the selected frames
(gain/pan commit when you **release** the knob after dragging; buttons apply immediately).
When multiple timeline clips share the same PCM buffer, a yellow banner warns that edits affect all instances
— use **Make unique** on a clip (timeline context menu) to detach one instance. Tempo controls: natural BPM,
stretch-to-project-tempo, and preserve-pitch. **Detect key** analyses the sample chromagram; **Re-detect tempo**
runs filename/onset tempo detection into the natural BPM field. **Change key** pitch-shifts the selected sample
to a user-chosen target key; entries marked **★** are musically related or low-shift options that tend to
sound smoother. Transposition moves by semitones only — it does not convert major ↔ minor harmony, so
re-detect may report a different mode than the label. Keyboard shortcuts (when the Sample panel has focus):
**Ctrl+X/C/V** cut/copy/paste, **Delete** removes the selection.

**Parameter knobs** ([`Knob`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Controls/Knob.cs)) appear throughout. **Right-click** any
knob, slider or switch to **MIDI-learn** it or **create an automation track** for it.

---

## 9. Settings & secondary windows

Opened from the title bar (or the piano roll):

| Window | Purpose |
| --- | --- |
| [Guide](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Windows/GuideWindow.axaml) | Built-in help topics (title bar **Guide** button) |
| [Settings](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Windows/SettingsWindow.axaml) | Audio device, MIDI devices/mappings, Theme editor, Library folders |
| [History](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Windows/HistoryWindow.axaml) | Browse and jump around the undo stack |
| [Logs](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Windows/LogWindow.axaml) | Debug log viewer |
| [MIDI Generator](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Windows/MidiGeneratorWindow.axaml) | Generate chords/melodies into a clip |
| [Arpeggio](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Windows/ArpeggioWindow.axaml) | Arpeggiate the selected notes |
| [Plugin](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Windows/PluginWindow.axaml) | Host a native plugin's own GUI |

---

## 10. Keyboard shortcuts (complete list)

Ongenet's shortcuts are defined in C# handlers (not XAML key bindings). Below is the full set.

### Global (anywhere except while typing in a text box)

Defined in [`MainWindow.axaml.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Views/Windows/MainWindow.axaml.cs).

| Shortcut | Action |
| --- | --- |
| **Ctrl + N** | New project |
| **Ctrl + O** | Open project |
| **Ctrl + S** | Save project |
| **Ctrl + Shift + S** | Save As |
| **Ctrl + Z** | Undo |
| **Ctrl + Shift + Z** | Redo |
| **Ctrl + Y** | Redo (alternative) |
| **Ctrl + Shift + I** | Ripple insert time at playhead (one bar) |
| **Ctrl + Shift + Backspace** | Ripple delete time at playhead (one bar) |
| **Ctrl + Alt + T** | Open tempo map window |
| **Ctrl + Alt + L** | Open section playlist window |
| **Space** | Play / stop toggle |
| **Shift + [** | Set loop **start** to the start marker |
| **Shift + ]** | Set loop **end** to the start marker |
| **F8** | Toggle Avalonia renderer diagnostics (FPS / render & layout graphs) |
| **View → Tempo Map** | Edit master tempo automation (menu; no default shortcut) |
| **View → Section Playlist** | Ordered section playback from arrangement markers (menu) |
| **Edit → Ripple edit** | Toggle ripple mode for clip edits (when enabled in edit menu) |
| **File → Collaboration → Pull latest** | Import the project copy from the sync folder |
| **Transport → Capture MIDI** | Retrospective capture of buffered notes into a new clip |

> Custom transport/key mappings can be configured in **Settings → MIDI** (`TransportMappings` in app settings).

> Set the environment variable `ONGENET_FPS=1` to auto-enable the F8 diagnostics overlay at startup.
> (Avalonia DevTools — the visual-tree inspector — is **F12** in Debug builds; see
> [DEVELOPMENT.md](https://github.com/1-chris/Ongenet/blob/main/DEVELOPMENT.md).)

Custom bindings for ripple edit, tempo map, and section playlist can be reset to defaults in
**Settings → MIDI → Keyboard shortcuts**. Overrides are stored in the app settings file.

### Collaboration (Export menu)

| Action | Description |
| --- | --- |
| **Sync to collaboration folder** | Export the saved project + share manifest to the configured sync folder |
| **Pull from collaboration folder** | Load the shared project from the sync folder; prompts if you have unsaved local changes |

### Arrangement tools (View menu)

| Menu item | Shortcut | Description |
| --- | --- | --- |
| **Tempo Map** | Ctrl + Alt + T | Edit master tempo automation |
| **Section Playlist** | Ctrl + Alt + L | Arrange and play back song sections |

Ripple insert/delete shift all clips at/after the playhead by one bar. Use **Ctrl + Shift + I** /
**Ctrl + Shift + Backspace**, or the timeline context commands.

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
[`Input/ComputerKeyboard.cs`](https://github.com/1-chris/Ongenet/blob/main/Ongenet.App/Input/ComputerKeyboard.cs); base note is **middle C (60)**:

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
