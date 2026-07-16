# DAW project import roadmap

Read-only / conversion-only import of foreign DAW projects into Ongenet’s in-memory
[`Project`](../Ongenet.Core/Models/Audio/Project.cs) model. After import, Save / Save As writes
`.ongen` only — never the source format.

**Target projects:** samples (audio clips + sampler channels) + built-in / stock effects.
Third-party plugins are detected and skipped with warnings.

## Dependency policy

- All format parsers are **written from scratch** in this repo.
- **No** NuGet packages, git submodules, vendored source trees, or project references for parsers.
- **Allowed dependencies only:** Avalonia, Microsoft / .NET BCL, SILK.NET.
- External open-source parsers and docs may be **inspected as reference** (event IDs, XML shapes);
  their code must not be copied or linked.

## Status legend

| Status | Meaning |
|--------|---------|
| Not started | No implementation yet |
| In progress | Active work |
| Partial | Works for a useful subset; gaps remain |
| Done | Meets current milestone acceptance |
| Blocked | Waiting on research / format unknowns |

## Shared pipeline

| Milestone | Status |
|-----------|--------|
| `IProjectImporter` + `ImportResult` warnings model | Done |
| `ImportDocument` intermediate representation | Done |
| `ImportMapper` → Ongenet `Project` | Done |
| `StockEffectMap` (per-DAW device → Ongenet TypeId) | Partial |
| File → Open dispatch by extension | Done |
| Conversion-only (no path / dirty until Save As `.ongen`) | Done |
| Fixture tests under `Ongenet.Core.Tests/Persistence/Import/` | Partial |

## FL Studio (`.flp`)

| Capability | Status |
|------------|--------|
| Parse `FLhd` / `FLdt` TLV events | Done |
| Tempo / time signature / PPQ | Done |
| Channel rack pre-create + sample paths | Done |
| ChanType (Sampler / Native / Layer / Instrument / Automation) | Done |
| Patterns + notes (24-byte modern layout, per rack channel) | Done |
| Playlist 32 / 60 / 80-byte items (FL20 / 21 / 25–26) | Done |
| Playlist → MIDI/audio expansion on arrangement tracks | Done |
| Mixer inserts (vol/pan/sends) + MixSliceNum routing | Partial |
| Fruity stock FX → Ongenet FX | Partial |
| Stock generators → Ongenet instruments (Sampler, 3x Osc, GMS, FL Keys, Harmless, …) | Partial |
| Channel Levels vol/pan/pitch + cutoff/resonance → sampler/synth | Partial |
| Sample path resolution (`%FLStudioFactoryData%` app-bundle + decode) | Done |
| Edison Ogg-in-WAV factory samples | Done |
| Hybrid tracks render (instrument + audio clips) | Done |
| Prune unused empty channel-rack slots | Done |
| Large-project import: pause audio + idle-skip when stopped | Done |
| Deferred sample decode (hydrate after arrangement is shown) | Done |
| FL 25/26 event-172 (3-byte) stream sync | Done |
| UI Open `.flp` | Done |
| Tests (synthetic FL20/21/25/26 fixtures) | Done |
| Save `.flp` | Not started (deferred) |

**Notes:** FL 21 playlist records are 60 bytes; FL 25/26 use 80- or **88-byte** records (`pattern_base` typically 20480). When both 80 and 88 divide the blob length, prefer **88** (FL26). Multiple arrangements each dump a playlist — import keeps the **last** arrangement only. Pattern notes use UInt16 `rack_channel`/`key`. Layer channels (event Children / 94) fan notes to child samplers. FL 26 writes event 172 with a 3-byte payload — reading it as a DWORD desyncs the file. ChanType `2` is **Native** (stock synths). Levels ints must not be reinterpreted as floats. Factory Edison `.wav` may be Ogg-in-WAV (`0x674F`).

## Ableton Live (`.als`)

| Capability | Status |
|------------|--------|
| Gunzip + XML parse | Done |
| Tempo / time signature | Partial |
| Audio / MIDI / group / return tracks | Partial |
| Arrangement clips + `SampleRef` | Partial |
| Warp markers | Partial |
| Live stock devices → Ongenet FX | Partial |
| UI Open `.als` | Done |
| Tests | Partial |
| Save `.als` | Not started (deferred) |

## Bitwig — DAWproject (`.dawproject`)

Early Bitwig path: export DAWproject from Bitwig, then Open in Ongenet.

| Capability | Status |
|------------|--------|
| ZIP + `project.xml` parse | Done |
| Tracks / channels | Partial |
| Audio + note timelines | Partial |
| Generic stock devices (EQ/comp/gate/limiter) | Partial |
| Embedded / referenced audio | Partial |
| UI Open `.dawproject` | Done |
| Tests | Partial |

## Bitwig — native (`.bwproject`)

| Capability | Status |
|------------|--------|
| Research / format probe | Partial |
| Extract sample paths | Partial |
| Extract track names | Partial |
| Arrangement / clips | Not started |
| Stock devices | Not started |
| UI Open `.bwproject` | Done (MVP) |
| Tests | Partial |

## Known limitations

- Undocumented proprietary formats; best-effort, no affiliation with Image-Line, Ableton, or Bitwig.
- Stock effect parameters are mapped heuristically, not 1:1.
- Missing samples produce empty clips / sampler slots plus warnings.
- Native `.bwproject` remains a research track; prefer `.dawproject` for reliable Bitwig interchange.
