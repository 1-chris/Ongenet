# Sampler: SFZ & SF2

Ongenet’s built-in **Sampler** plays `.sfz` and `.sf2` (SoundFont) instruments in one instrument type. You can stack several files as layers, pick SF2 bank/program presets, edit zones at runtime, and automate MIDI CCs the same way as any other rack instrument.

ARIA GUI opcodes (`gui_*`, labels) and SFZ `<effect>` plugin DSP are not supported; unsupported opcodes may show a **load warnings** dialog.

## Load an instrument

1. Put a **Sampler** on a track (empty rack slot → choose Sampler, or use a factory stack from the Preset menu).
2. Load a patch with any of:
   - **Library → Soundfonts** — drag an SFZ or SF2 onto the Sampler slot
   - Slot **Load…** — library tree or disk (`*.sfz`, `*.sf2`)
   - Field graph — **SoundFont** asset node into a **Sampler** node
3. **Load…** replaces every layer with that one file.
4. Play the MIDI track; gain / transpose / tune are on the Sampler parameters.

Factory packs and licences: [Factory content](factory-content.md). Adding your own folders: [Samples & libraries](samples-and-libraries.md).

## Layering

Layers let you stack instruments (e.g. GM piano under a kit, or two SFZ articulations).

| Control | What it does |
| --- | --- |
| **Add layer…** | Loads another SFZ/SF2 **on top** of existing layers (does not replace them) |
| **Load…** | Clears all layers and loads a single patch |
| Layer enable checkbox | Mute a layer without removing it |
| **Mask** lo/hi | Restrict that layer to a keyboard range |
| Layer colour | Tint zones in the zone map |
| **Remove** | Drop that layer |

Open **Editor…** on the Sampler slot for the layer list, keyboard coverage, zone map, and zone table.

### Factory stacks

The Sampler Preset menu includes ready-made multi-layer patches such as **GM GeneralUser**, **GM JNS**, **VSCO Strings**, and **VCSL Kit + Piano**. Those are the same layering system as **Add layer…**, pre-wired for common setups.

## SF2 presets (bank / program)

Many `.sf2` files are full GM banks. After load:

- The slot shows a preset picker labeled **Bank N · PPP  Name**
- The Editor has the same picker **per SF2 layer**
- Changing the selection reloads that layer’s regions for the new program (sample data from the same file)

SFZ files are single-instrument definitions and do not use this picker.

## Zone editor

**Editor…** opens the multi-layer sampler UI:

1. **Layers** — enable, colour, SF2 preset, key mask, add/remove  
2. **Keyboard coverage** — which keys have zones; hold a key to audition  
3. **Zone map** — key × velocity layout (colour per layer)  
4. **Zone table** — per-zone edits applied with **Apply zone edits**

Editable at runtime (in memory for this project; **does not rewrite** the `.sfz` / `.sf2` on disk):

- Key and velocity range, root key, round-robin length/position  
- Gain, pan  
- Filter on/off, cutoff, Q  
- Amp envelope attack / decay / sustain / release  

Filter EG, pitch EG, LFO depths, and CC routes from the file stay as loaded unless you change them by reloading a different patch.

## MIDI & performance

The Sampler responds to the usual rack MIDI path:

| Input | Behaviour |
| --- | --- |
| Notes | Trigger matching zones (key/vel, triggers, keyswitches) |
| Pitch bend | Region bend range from the SFZ/SF2 |
| CC (e.g. mod wheel) | Slots send CC; SFZ `*_oncc` / `*_cc` and SF2 modulators follow live |
| Sustain (CC64) | Holds notes when the region allows sustain |
| Project tempo | Tempo-gated SFZ opcodes (`lobpm` / `hibpm`, beat delays) track transport BPM |

Keyswitches and CC-gated articulations come from the SFZ/SF2 notes/opcodes — use the zone map and keyboard coverage to see what is mapped.

## Load warnings

If a patch uses opcodes or effect sends Ongenet does not implement, a **Sampler load warnings** dialog lists them after load. The instrument still plays; ignored features stay silent. Prefer SFZ v1/v2 libraries that do not depend on ARIA-only GUI or effect plugins.

## Format support matrix

| Status | Meaning |
|--------|---------|
| **Implemented** | Parsed and applied at runtime |
| **Partial** | Applied with simplified behaviour |
| **Ignored** | Recognised; load warning; no effect |

### SFZ (v1 + v2 core)

| Family | Status | Notes |
|--------|--------|-------|
| Mapping (`lokey`/`hikey`/`lovel`/`hivel`, `sample`, loops) | Implemented | Including `loop_type`, `loop_count`; `loop_crossfade` length reserved |
| `#include` / `#define` / `<control>` | Implemented | `note_offset`, `octave_offset`, `set_ccN` seeded on load |
| `<curve>` + `*_curveccN` | Implemented | |
| CC gates `loccN`/`hiccN` | Implemented | |
| Random RR `lorand`/`hirand` | Implemented | |
| Channel / bend / aftertouch / BPM / program gates | Implemented | Host tempo from transport |
| Keyswitches `sw_last`/`sw_down`/`sw_up`/`sw_previous`/`sw_vel` | Implemented | |
| `on_locc` / `start_locc` / `stop_locc` | Implemented | |
| `off_mode` | Implemented | |
| Amp/pan/pitch/cutoff/resonance `*_cc` / `*_oncc` | Implemented | Via mod routes |
| Crossfades `xfin_*` / `xfout_*` | Implemented | |
| Delay / offset random + CC + beats | Implemented | |
| EG start + `*_vel2*` | Implemented | |
| Flex `egN_*` / `lfoN_*` (volume/pitch/cutoff/pan) | Implemented | Core destinations only |
| Dual filter `cutoff2` | Implemented | |
| `effect1`–`effect4` | Ignored | Warn; no effect bus |
| `gui_*` / `label_*` / ARIA UI | Ignored | |
| Wavetable `oscillator*` / `noise_*` | Ignored | |

### SF2

| Family | Status | Notes |
|--------|--------|-------|
| Hydra flatten + 16/24-bit samples | Implemented | |
| Generators (pitch, pan, EG, filter, LFO, loops, exclusive) | Implemented | |
| Keynum → EG hold/decay | Implemented | |
| `pmod` / `imod` | Implemented | Mapped into sampler mod routes |
| Default modulators (vel→atten, CC1→vib) | Implemented | When zone mod list empty |
| Chorus / reverb sends | Ignored | Warn; no effect bus |

## Related

- [Factory content](factory-content.md)
- [Samples and libraries](samples-and-libraries.md)
