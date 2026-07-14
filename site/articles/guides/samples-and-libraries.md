# Samples & libraries

## Quick steps

1. Open the **Library** sidebar (right). **Samples** and **Soundfonts** include a **Factory** group on desktop when the Content/Core pack is installed — no Settings step required for those.
2. Optionally open **Settings** → **Library** → **Sample folders** → **Add…** for your own packs.
3. Drag a file onto an **audio track**. Enable **Auto-stretch** to sync loops to project tempo on import.
4. Browse **Library → Soundfonts** for SFZ / SF2 instruments (including factory kits).

## Factory content

Desktop installers ship a **Content/Core** pack (~1.4 GB): drums (Stargate, Oramics, VCSL kit, 909 SF2), acoustic instruments (VCSL piano/Strumstick, VSCO 2 CE orchestra), GM SF2 banks (Jnsgm2, GeneralUser, ChaosBank, …), and tonejs **CC BY 3.0** bass / organ / xylophone.

Full catalogue, licences, and attribution: **[Factory content](factory-content.md)**. In-app: **Settings → Legal → Factory content attribution** (`Content/Core/ATTRIBUTION.md`).

Maintainers expanding the pack: see [`Content/README.md`](../../../Content/README.md).

## Your own packs

Always check each pack's licence — CC0 needs no attribution; Creative Commons may require credit in album notes.

Good sources for royalty-free material:

- [99Sounds](https://99sounds.org/)
- [Freesound](https://freesound.org/)
- [Looperman](https://www.looperman.com/)
- [PianoBook](https://www.pianobook.co.uk/)
- [SampleFocus](https://samplefocus.com/)
- [BBC Sound Effects](https://sound-effects.bbcrewind.co.uk/)

### SFZ and soundfonts

Place SFZ instruments and soundfont folders in paths registered under **Settings → Library → Soundfonts**. They appear under **Library → Soundfonts** once scanned.

### Tempo sync

When **Auto-stretch** is enabled, Ongenet detects loop BPM from filename/folder or onset analysis and time-stretches to the project tempo. One-shots and recordings stay at native length unless overridden in the Sample Inspector.

## Related

- [Sampler: SFZ & SF2](sfz-sf2-opcode-support.md) — load, layering, zone editor, format support
- [Getting started](getting-started.md)
- [Timeline & clips](timeline-and-clips.md)
- [Plugins](plugins.md)
