# Mixer & export

## Quick steps

1. Switch to the **Mixer** tab for per-track volume, pan, sends, and routing.
2. Click **Export** in the title bar (or the transport bar **Export** button) for offline bounce.
3. Choose master mix, stems, regions, or surround; pick WAV, FLAC, MP3, or OGG.
4. Use **Export → Collaboration folder sync** to share projects via folder-based sync.

## Details

### Mixer

- Per-track volume, constant-power pan, mute, solo, peak metering on **audio, instrument, hybrid, group, return, and master** tracks
- **MIDI** and **Pattern** tracks are timeline-only — they do not appear as mixer strips (no audio mix path)
- **Aux sends** — pre- or post-fader taps into return tracks; pick the return destination, adjust level, or remove sends from the mixer strip or Track Inspector
- **Send automation** — right-click a send level slider to create an automation lane
- **Surround pan** — 5.1 / 7.1 in the **Track Inspector** when output device has enough channels
- **Routing matrix** — edit track output destinations, all aux sends per track, and plugin multi-out routes (hardware audio-interface inputs are configured in Settings → Audio)
- **Control surfaces** — MCU/HUI mute and solo buttons; Push2/APC40 learnable volume, pan, mute, solo, and first-send level (CC 91)

### Export

- **Master mix**, **per-track stems**, or **beat regions** — faster than real time
- **Surround** offline bounce for 5.1 / 7.1
- **Timeline XML** handoff for post pipelines (not binary AAF)
- **MIDI export** from the transport area
- Honours automation, aux sends, PDC, sidechain, and effect tails
- **Video mux** — when the project has video tracks and **ffmpeg** is installed, enable **Mux master audio with video track (MP4)** to deliver a single `.mp4` with the bounced master
- **Composited export** — **Export composited video** (or title bar **Export ▾ → Export video…**) bakes all video layers into an MP4; see [Video & composition](video-and-composition.md) for a step-by-step visualiser tutorial.

### Mastering on the Master track

Ongenet masters like a DAW: insert effects on the pinned **Master** track. See **[Mastering](mastering.md)** for the full workflow — chain recipes, meter tap, Compare/A/B/reference, Match EQ, export loudness, and Peak Limiter vs Dynamics **Limiter**.

**Startup & built-ins:** desktop opens **Ascension** (Full Master, OTT on the Leads bus); WASM/Android open **Web Demo** (Streaming Master). Library demos such as **First Light** are not the desktop startup song. Built-in projects pick genre chains automatically — Full Master (Ascension, First Light, House Starter), Club Loud (Undertow, Trap Beat), Streaming Master (Dust & Vinyl, Field Modular, Static Bloom, Web Demo), or Techno Master (Techno Starter).

Export honours the same delivery targets: **Analyse loudness**, **Normalize to platform LUFS**, **Pre-master bounce**, stem **Include master FX**, and **Match album loudness**. Surround export loudness analysis uses every channel with full BS.1770 weighting; the on-screen meter stays stereo-focused for 5.1/7.1 monitoring.

## Related

- [Mastering](mastering.md)
- [Timeline & clips](timeline-and-clips.md)
- [Sidechain & dynamics](sidechain-and-dynamics.md)
- [Video & composition](video-and-composition.md)
- [Getting started](getting-started.md)
