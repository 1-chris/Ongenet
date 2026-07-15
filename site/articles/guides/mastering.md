# Mastering

## Quick steps

1. Select the pinned **Master** track (timeline row 0 or the Mixer master strip).
2. Open the bottom **Effects** tab — use the **chain picker** for a factory recipe, or drag from **FX Chains** in the Library.
3. Set the **delivery target** (Spotify, YouTube, Apple Music, Club, Podcast, or Custom) — shared with the title-bar meter and Export dialog.
4. Watch **LUFS** and **true peak** on the title-bar meter; choose **Pre Limiter**, **Post Chain**, or **Post Fader** tap.
5. Use **Compare** (clipper/limiter bypass with loudness makeup), **A/B** snapshots, and WAV **reference** slots (A/B) with optional **Match EQ**.
6. **Export** with **Analyse loudness** and optional **Normalize to platform LUFS**.

## Details

Ongenet masters like a DAW: insert effects on the pinned **Master** track, not a separate mastering page. The engine only hard-clamps overs at ±1.0 after the Master FX chain; delivery loudness and ISP headroom come from your limiter and clipper choices.

### Master track Effects tab workflow

1. Select the **Master** track.
2. Open the **Effects** tab at the bottom of the window.
3. Pick a recipe from the **chain picker**, drag an **FX Chains** preset from the Library, or build manually.
4. Set **Delivery target** on the toolbar — the same platform, integrated LUFS, and true-peak ceiling drive the title-bar meter peach target line, Compare makeup, reference matching, and Export dialog defaults.
5. Choose a **meter tap** (**Pre Limiter**, **Post Chain**, or **Post Fader**) to decide what the title-bar LUFS / dBTP readout reflects.
6. Use mastering toolbar actions: **Bypass chain**, **Compare**, **A/B**, and **Reference** (hold to audition).
7. Tune individual inserts in the chain list; Peak Limiter presets and GR meter are on the limiter card.

Suggested manual order when not using a preset: corrective EQ → Mid/Side EQ → glue compressor → stereo width → clipper → **Peak Limiter** → Spectrum analyser. (**Full Master** omits Multiband OTT on Master — put OTT on a bus, or use **Full Master+** / **Techno Master**.)

### Chain recipes

`MasteringChains.Create(name)` and the FX Chains library share **eight** core recipes plus **Audiophile Master** and **Reference Master** (**ten** named recipes total):

| Chain | Signal path |
| --- | --- |
| **Full Master** | Corrective EQ → mid/side → glue → width → clipper → Peak Limiter (Streaming) → Spectrum |
| **Full Master+** | Full Master with Multiband OTT before width |
| **Streaming Master** | EQ → glue → Peak Limiter (Streaming) — no clipper |
| **Pre-Master** | DC blocker → EQ → mid/side → glue only (no limiter) |
| **Club Loud** | Multiband → width → soft Over → clipper → Peak Limiter (Master) |
| **Podcast / Speech** | HPF → de-esser → glue → Peak Limiter (Safety) |
| **Master Glue** | Glue compressor → width → Peak Limiter (Loud) |
| **Techno Master** | HPF → multiband → width → Exciter → Peak Limiter (Master) |
| **Audiophile Master** | Full Master with **Linear-Phase EQ** replacing the minimum-phase corrective EQ (higher latency; `Create("audiophile")` / `Create("linear")`) |
| **Reference Master** | Corrective EQ → Match EQ → glue → Peak Limiter (Streaming) → Spectrum (`Create("reference")` / `Create("match")`; capture reference in UI first) |

**Peak Limiter presets** (Streaming −1.0 dBTP, Master −0.3 dBTP with spectral mode, Loud, Transparent, Safety) and **Multiband OTT** macros have tooltip descriptions in the UI. Prefer **2×/4× Oversample** on clipper and limiter for inter-sample peak control.

### Meter tap, delivery targets, Compare, A/B, reference, Match EQ

- **Meter tap** — title-bar stereo L/R bars, short-term/integrated **LUFS**, **LRA**, and **dBTP** can read **Pre Limiter** (before the first Peak Limiter), **Post Chain** (after all inserts, pre-fader), or **Post Fader**. True peak uses 4× FIR BS.1770-style reconstruction. A peach line marks the active delivery true-peak target. Use **Reset loudness** to clear integrated measurements.
- **Delivery targets** — Spotify (−14 LUFS / −1.0 dBTP), YouTube (−14 / −1.0), Apple Music (−16 / −1.0), Club (−9 / −0.3), Podcast (−16 / −1.5), or **Custom** LUFS/TP. The Master Effects tab and Export dialog share one app-wide target.
- **Compare** — bypasses the **clipper and limiter(s)** (`ClipperEffect`, `PeakLimiterEffect`, `LimiterEffect`) — or the full Master chain in full-bypass mode — and applies loudness makeup via a metering **Tool** so you hear un-limited audio at matched level. Inserts a Tool automatically if needed. Analysers after the limiter (Spectrum, etc.) stay enabled.
- **A/B** — two complete ordered-chain snapshots; swap without losing either version.
- **Reference** — load a WAV; integrated LUFS is matched to the delivery target for level-matched audition while you hold the button. Overlay spectrum shows live (sapphire) vs reference (peach).
- **Match EQ** — **Capture reference to Match EQ** seeds a **Match EQ** insert from the reference spectrum; blend and smoothness tame the correction.

The **Spectrum** analyser panel adds peak bars, correlation, and a **Scope** goniometer; **Tool** supplies peak/LUFS/correlation meters (no goniometer). Load a second WAV with **Browse B** for a dual reference slot.

### Export loudness analyse / normalize

From **Export** (title bar or transport):

- **Analyse loudness** — after bounce, writes integrated LUFS, LRA, momentary/short-term peaks, and true peak to `.loudness.txt` / `.loudness.json` sidecars; WAV embeds RIFF INFO; FLAC/MP3/OGG get ReplayGain/R128 tags.
- **Normalize to platform LUFS** — two-pass normalize to the selected delivery target (custom LUFS supported). When boost is needed, the exporter re-limits at the delivery ceiling instead of only attenuating.
- **Pre-master bounce** — bypass Master inserts for an un-mastered WAV.
- **Comparison pair** — also writes `{name}-comparison-unmastered.wav` and `{name}-comparison-mastered.wav` beside the deliverable.
- **Match album loudness** — stem sets can share one offset while preserving relative track loudness; scripts call `MatchAlbumLoudness`.

Default archival bounce is **24-bit** WAV; enable **TPDF** or **noise-shaped** dither for 16-bit CD. **Sample rate** SRC uses windowed-sinc resampling (e.g. 44.1 kHz for CD).

### Peak Limiter vs Limiter (Dynamics)

| | **Peak Limiter** (Mastering) | **Limiter** (Dynamics) |
| --- | --- | --- |
| Role | Delivery brickwall on Master | Bus/drum safety limiter |
| Features | Presets, GR meter, spectral mode, oversampling | Simple threshold/ceiling |
| Typical placement | Last processing on Master (before analysers) | Drum bus, instrument groups |

Use **Peak Limiter** on Master for streaming/club delivery. Keep the simpler **Limiter** on buses that need headroom below the master ceiling (Ascension puts a bus limiter on Drums above the master Peak Limiter ceiling).

### Built-in project genre chains

Factory songs call the same recipes as the chain picker:

| Project | Master chain | Notes |
| --- | --- | --- |
| **Ascension** | Full Master | Multiband OTT on **Leads** bus, not on Master |
| **First Light**, **House Starter** | Full Master | |
| **Undertow**, **Trap Beat** | Club Loud | Plus Spectrum / waveform analysers |
| **Dust & Vinyl**, **Field Modular**, **Static Bloom** | Streaming Master | Field Modular / Static Bloom add reverb before the limiter |
| **Web Demo** | Streaming Master without Spectrum | EQ → glue → Peak Limiter only (WASM/Android DSP budget) |
| **Techno Starter** | Techno Master | |

**Startup:** desktop opens **Ascension**; WASM/Android open **Web Demo** (lightweight Streaming Master without Spectrum). **First Light** is library-only.

Portable project scripts store mastering FX as inline parameter values so chains round-trip through **Export project** / `ApplyMasteringChain`.

**Portable export vs chain recipes:** `ApplyMasteringChain(masterId, name)` replaces the entire Master insert list with a factory recipe in one undo step — use this in generated project scripts and batch automation. `AddEffect(masterId, typeId)` appends a single insert and is better for custom one-off tweaks; exported scripts prefer inline parameter literals on `*WithId` tracks rather than re-deriving recipes by hand.

### Scripting limitations

Desktop Roslyn scripting exposes mastering helpers on `IScriptingApi`. Web Demo and Android register `NullScriptingApi` — the Scripting tab shows a status hint; mastering/export **mutations** throw (meter-tap / delivery-target **gets** return stubs).

| Available from scripts | Not available from scripts |
| --- | --- |
| `ApplyMasteringChain`, `Get/SetDeliveryTarget`, `Get/SetMasterMeterTap` | Compare / A/B / reference audition UI |
| `ExportAudio` (normalize, dither, SRC, comparison pair, bypass master FX) | Match EQ reference capture (use UI first, then export) |
| `MatchAlbumLoudness` on existing WAV files | Bypass chain / Restore toolbar actions |

Album alignment uses `MatchAlbumLoudness`, not the internal `ExportService.ComputeAlbumOffsets` helper.

### Web Demo and Android

WASM and Android builds use a reduced mastering path to stay within mobile/browser DSP budgets:

- **Web Demo** ships **Streaming Master without Spectrum** (EQ → glue → Peak Limiter only).
- Analyser-only inserts (Spectrum, Loudness Meter, oscilloscope, 3D scope) are skipped during offline export everywhere; live analyser panels are further reduced on WASM/Android.
- Scripting, offline `ExportAudio`, and album loudness APIs are desktop-only.

**Oversampling is real DSP:** 2×/4× modes FIR-upsample, process nonlinear DSP at the higher rate, then FIR-downsample. Only 1× is sample-peak based.

## Related

- [Mixer & export](mixer-and-export.md)
- [Sidechain & dynamics](sidechain-and-dynamics.md)
- [Getting started](getting-started.md)
- [Scripting](scripting.md) — `ApplyMasteringChain`, `ExportAudio`
