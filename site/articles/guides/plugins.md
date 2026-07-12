# Plugins

## Quick steps

1. Install plugins into your OS standard folders (see paths below) or set environment variables.
2. Launch Ongenet — plugins are scanned in the background at startup.
3. Find them in **Library → Instruments** / **FX**, or add from a track's instrument/effect menu under **Plugins**.

## Supported formats

Ongenet hosts **CLAP**, **LV2**, **VST2/VST3**, and **Audio Units** (macOS). Rescans pick up new files on the next launch — no manual refresh needed.

### Install paths

| Platform | Location |
| --- | --- |
| macOS CLAP/VST3/AU | `~/Library/Audio/Plug-Ins/…` |
| Linux LV2 | `~/.lv2` or `/usr/lib/lv2` |
| Windows VST3 | `%CommonProgramFiles%\VST3` |

Optional environment overrides: `CLAP_PATH`, `VST_PATH`, `VST3_PATH`, `LV2_PATH`.

## Suggested free starters

**Synths** — Surge XT, Odin 2, Helm, Vital (free tier), TAL-NoiseMaker, u-he Tyrell N6.

**Effects** — ChowDSP suite, Dragonfly Reverb, LSP Plugins (LV2), Melda free bundle, TAL-Reverb-4.

**Samplers** — Decent Sampler, PianoBook SFZ packs.

## Useful links

- [ChowDSP](https://chowdsp.com/)
- [Dragonfly Reverb](https://github.com/michaelwillis/dragonfly-reverb)
- [LSP Plugins](https://lsp-plug.in/)
- [Surge XT](https://surge-synthesizer.github.io/)
- [Vital](https://vital.audio/)
- [KVR Audio — free plugins](https://www.kvraudio.com/plugins/free)
- [Plugins4Free](https://plugins4free.com/)

## Related

- [Samples & libraries](samples-and-libraries.md)
- [Sidechain & dynamics](sidechain-and-dynamics.md)
- [Dev: Plugin isolation](/dev/plugin-isolation.html)
