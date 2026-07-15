# Ongenet factory content

Bundled desktop factory audio lives in [`Core/`](Core/). The app finds it via `AppPaths.FactoryContentDirectory()` and shows **Factory Samples** / **Factory Soundfonts** in the Library without Settings changes. Packaging copies `Content/Core` beside binaries (`scripts/packaging-common.sh`).

Software (Ongenet) stays **MIT**. Audio here is licensed **separately** (CC0 / Public Domain / CC-BY only). See [`Core/ATTRIBUTION.md`](Core/ATTRIBUTION.md) and [`provenance.jsonl`](provenance.jsonl).

---

## What’s in Core today

| Path | Source | License |
|------|--------|---------|
| `Samples/Drums/OngenetKit` | Procedural Ongenet one-shots | CC0 |
| `Samples/Drums/Stargate` | [stargate-sample-pack](https://github.com/stargatedaw/stargate-sample-pack) electronic drums | CC0 |
| `Samples/Drums/Oramics/LM-2` | [Oramics LM-2](https://oramics.github.io/sampled/DM/LM-2/) | Public Domain |
| `Samples/Drums/Oramics/TR-909_Detroit` | [Oramics TR-909 Detroit](https://oramics.github.io/sampled/DM/TR-909/Detroit/) | Public Domain |
| `Soundfonts/OngenetDrumKit`, `OramicsLM2`, `Oramics909` | SFZ maps of the above | CC0 / PD |
| `Soundfonts/ToneJs/{bass-electric,organ,xylophone}` | [tonejs-instruments](https://github.com/nbrosowsky/tonejs-instruments) (ogg + SFZ) | **CC BY 3.0** (credit required) |
| `Soundfonts/VCSL/` | [VCSL](https://github.com/sgossner/VCSL) curated: acoustic kit, Strumstick, Kawai grand (`Piano.sfz`) | CC0 |
| `Soundfonts/VSCO2CE/` | [VSCO 2 CE](https://github.com/sgossner/VSCO-2-CE) curated simple orchestra (SFZ branch) | CC0 |
| `Soundfonts/Sf2/GM/*` | [bratpeki/soundfonts](https://github.com/bratpeki/soundfonts): ChaosBank, Jnsgm2, Masterpiece, Unison, eawpats, GeneralUser | CC0 / PD / GeneralUser |
| `Soundfonts/Sf2/Drums/909_drum_sf` | 909 drum SF2 (bratpeki / Musical Artifacts) | **CC BY 3.0** |

Credits for CC-BY material (and GeneralUser) are required in product credits (`Settings → General → Factory content attribution`).

**Size:** Core is ~1.4 GB uncompressed (packaging asserts ≤ 1600 MB). VCSL has no traditional strings/brass/guitar — orchestra pieces come from sibling **VSCO 2 CE**; fretted tone is **Strumstick**; piano is VCSL **Grand Piano, Kawai**. GPLv2 banks (e.g. TimGM) and personal-use-only banks (WeedsGM3) are **not** shipped.

### VCSL / VSCO2CE curated instruments

| Role | Instruments |
|------|-------------|
| Drums | Bass drums, modern snares, toms, hi-hat, clash cymbals, tambourine, cowbells, claps, woodblock, shaker + GM-ish `VcslAcousticKit.sfz` |
| Guitar-like | Strumstick |
| Piano | Grand Piano, Kawai (`Piano.sfz`) |
| Orchestra | Violin section, cello section, clarinet, French horn, harp, timpani |

---

## Approved sources for future additions

Use these when expanding Core or a future optional **Extra** pack. Prefer **CC0 / Public Domain**, then **CC-BY**. Avoid commercial “royalty-free” packs (almost always forbid redistributing loose files) and **CC-BY-SA** (share-alike grey area for a host DAW).

### Electronic & acoustic drums

| Source | License notes | Notes |
|--------|---------------|-------|
| [Stargate sample pack](https://github.com/stargatedaw/stargate-sample-pack) | CC0 | Already partially integrated (drums). Safe full-repo redistrib. |
| [Oramics Sampled](https://oramics.github.io/sampled/DM/) | Public Domain | LM-2 + TR-909 Detroit included; also CR-78, MRK-2, TR-505, etc. |
| [Hydrogen drumkits](https://sourceforge.net/projects/hydrogen/files/Sound%20Libraries/) | **Per kit** — check `drumkit.xml` `<license>` | Many AVL kits are **CC BY-SA** — do **not** ship those. Prefer kits marked CC0 / CC-BY. |
| [VCSL](https://github.com/sgossner/VCSL) | CC0 | Acoustic kit subset already in Core; more perc/mallets available. |

### Orchestral / acoustic

| Source | License notes | Notes |
|--------|---------------|-------|
| [VSCO 2 CE](https://github.com/sgossner/VSCO-2-CE) | CC0 | Simple orchestra subset already in Core (SFZ branch). Expand with viola/flute/trumpet as budget allows. |
| [Sonatina Symphonic Orchestra (SSO)](https://github.com/peastman/sso) | Sampling Plus / community forks | Large shared sample pool; verify license before bundling. Prefer Extra pack. |
| [VCSL](https://versilian-studios.com/vcsl/) | CC0 | Excellent quality; no full traditional orchestra — use with VSCO 2 CE. |

### Synths / keys / multisamples

| Source | License notes | Notes |
|--------|---------------|-------|
| [tonejs-instruments](https://github.com/nbrosowsky/tonejs-instruments) | Code MIT; **samples CC BY 3.0** | Piano / bass / organ / xylophone already in Core (ogg + SFZ). Read `sample-source-info.txt` for author credits. |
| [Freesound](https://freesound.org/) CC0 filter | CC0 / CC-BY as tagged | Only queue individual packs with clear license URLs; do not scrape. Prefer packs already aggregated under Stargate when possible. |

---

## How to add content (maintainer)

No fetch scripts are checked in — keep the tree simple.

1. **Confirm license** allows redistribution *inside a DAW installer* (CC0 / PD / CC-BY). Reject NC, ND, SA, and commercial RF packs.
2. **Download** the pack yourself (browser/git/curl) into a temporary folder (not necessarily in-repo).
3. **Curate** a size-conscious subset (Core should stay under ~1400 MB uncompressed; packaging asserts this).
4. **Copy** files into `Content/Core/Samples/...` or `Content/Core/Soundfonts/...`.
5. **Add** `Content/Core/licenses/<pack-id>/LICENSE` (and NOTICE if CC-BY authors must be listed).
6. **Append** one JSON line per file (or per pack summary) to [`provenance.jsonl`](provenance.jsonl):

```json
{"id":"pack/file.wav","localPath":"Core/Samples/.../file.wav","kind":"sample","sourceUrl":"...","licenseUrl":"...","licenseId":"CC0-1.0","author":"...","title":"file.wav","retrievedUtc":"2026-07-14T00:00:00Z","sha256":"...","attributionText":"...","notes":"..."}
```

7. **Update** [`Core/ATTRIBUTION.md`](Core/ATTRIBUTION.md) so every distinct author/source appears (used by Settings → Factory content attribution).
8. **Optional SFZ**: if shipping a playable kit/instrument, add a `.sfz` next to the samples using opcodes supported by Ongenet’s Sampler.
9. **Size check**: `source scripts/packaging-common.sh && assert_content_size_mb "$(pwd)" 1400`

### License placement (keeps MIT core clean)

| Resource type | Preferred license | Practice |
|---------------|-------------------|----------|
| CC0 / Public Domain | No attribution required | Ship under `Content/Core/`; still credit upstream in ATTRIBUTION as courtesy |
| CC-BY 3.0 / 4.0 | Attribution required | Keep author credit in `licenses/<id>/` + ATTRIBUTION + in-app credits link |
| CC-BY-SA | **Avoid** | Do not bundle |

For multi-GB libraries (full SSO / full VCSL), prefer a separate optional Extra download — do **not** inflate every installer beyond the asserted ceiling.

---

## Layout

```text
Content/
  README.md                 # this file
  provenance.jsonl          # ledger of shipped files
  .gitignore                # staging zips / leftovers
  Core/                     # shipped with installers
    ATTRIBUTION.md
    Samples/
    Soundfonts/
      VCSL/                 # curated acoustic kit + Strumstick
      VSCO2CE/              # curated simple orchestra
    licenses/<pack-id>/
    manifest.json
```
