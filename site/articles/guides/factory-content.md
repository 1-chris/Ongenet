# Factory content

Desktop builds of Ongenet can ship a bundled **Content/Core** pack. It appears in the Library under **Factory Samples** and **Factory Soundfonts** with no Settings step required. The Ongenet application remains **MIT**; factory audio is licensed separately.

## What’s included

Rough category overview of the current Core pack (~1.4 GB uncompressed):

### Electronic / acoustic drums

| Item | Location | Licence |
| --- | --- | --- |
| Stargate electronic drums | `Samples/Drums/Stargate` | CC0 |
| Oramics LM-2 | `Samples/Drums/Oramics/LM-2` | Public Domain |
| Oramics TR-909 Detroit | `Samples/Drums/Oramics/TR-909_Detroit` | Public Domain |
| OngenetKit one-shots + SFZ | `Samples/Drums/OngenetKit`, `Soundfonts/OngenetDrumKit` | CC0 |
| VCSL acoustic kit + `VcslAcousticKit.sfz` | `Soundfonts/VCSL/` | CC0 |
| 909 Drum SoundFont | `Soundfonts/Sf2/Drums/909_drum_sf/` | **CC BY 3.0** |

### Acoustic instruments

| Item | Location | Licence |
| --- | --- | --- |
| VCSL Kawai grand (`Piano.sfz`) | `Soundfonts/VCSL/` | CC0 |
| VCSL Strumstick | `Soundfonts/VCSL/` | CC0 |
| VSCO 2 CE orchestra (violin/cello sections, clarinet, horn, harp, timpani) | `Soundfonts/VSCO2CE/` | CC0 |

### GM / SF2 banks

| Bank | Location | Licence |
| --- | --- | --- |
| Jnsgm2 | `Soundfonts/Sf2/GM/Jnsgm2/` | CC0 |
| GeneralUser GS | `Soundfonts/Sf2/GM/GeneralUser/` | Author licence (software OK) |
| ChaosBank | `Soundfonts/Sf2/GM/ChaosBank/` | CC0 |
| Masterpiece | `Soundfonts/Sf2/GM/Masterpiece/` | CC0 |
| Unison | `Soundfonts/Sf2/GM/Unison/` | CC0 |
| eawpats | `Soundfonts/Sf2/GM/eawpats/` | Public Domain |

Not shipped: TimGM (GPLv2), WeedsGM3 (personal-use-only wording), FluidR3 GM.

### ToneJs instruments (CC BY 3.0)

Bass-electric, organ, and xylophone remain under `Soundfonts/ToneJs/` (ogg + SFZ). Piano was replaced by the VCSL Kawai grand.

## Licences & commercial use

| Licence | In your music releases |
| --- | --- |
| CC0 / Public Domain | Freely; attribution not required |
| **CC BY 3.0** (ToneJs remaining instruments, 909 SF2) | Allowed; **credit required** |
| GeneralUser GS | Allowed in software and recordings; credit S. Christian Collins |

In-app credits: **Settings → General → Factory content attribution** (Legal subsection). On disk: `Content/Core/ATTRIBUTION.md`.

## Sources / credits

Summarised from onboard attribution (see that file for full notes):

| Name | Upstream | Licence |
| --- | --- | --- |
| Stargate Sample Pack | https://github.com/stargatedaw/stargate-sample-pack | CC0-1.0 |
| Oramics LM-2 / TR-909 Detroit | https://oramics.github.io/sampled/ | Public Domain |
| tonejs-instruments (bass / organ / xylophone) | https://github.com/nbrosowsky/tonejs-instruments | CC-BY-3.0 |
| VCSL (curated) | https://github.com/sgossner/VCSL | CC0-1.0 |
| VSCO 2 CE (curated) | https://github.com/sgossner/VSCO-2-CE | CC0-1.0 |
| ChaosBank / Masterpiece / Unison / Jnsgm2 / eawpats | https://github.com/bratpeki/soundfonts | CC0 / PD |
| GeneralUser GS | https://schristiancollins.com/generaluser.php | LicenseRef-GeneralUser |
| 909 Drum Soundfont | https://musical-artifacts.com/artifacts/1971 | CC-BY-3.0 |

## How to use

1. Open **Library → Samples** or **Soundfonts** and expand the Factory groups (nested by folder).
2. Drag an SFZ/SF2 onto a track that hosts a **Sampler**, or use the slot **Load…** / **Add…** buttons.
3. **Load…** replaces all layers; **Add…** stacks another instrument. **Editor…** opens layers, keyboard coverage, and the zone map.
4. Sampler factory stacks (Preset menu): e.g. **GM GeneralUser**, **GM JNS**, **VSCO Strings**, **VCSL Kit + Piano**.

More drag-and-drop detail: [Samples & libraries](samples-and-libraries.md). Sampler how-to (load, layers, editor): [Sampler: SFZ & SF2](sfz-sf2-opcode-support.md).

## Maintainers

Approved sources and how to expand the pack: [`Content/README.md`](../../../Content/README.md).
