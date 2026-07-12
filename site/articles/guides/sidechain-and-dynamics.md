# Sidechain & dynamics

## Quick steps

1. Add the built-in **Sidechain** effect to a track's FX chain (e.g. bass or pad).
2. **Tempo mode** — leave source empty for a tempo-synced pump; set Rate and Amount.
3. **Track mode** — pick a source track (e.g. kick) for envelope-follower ducking; tweak Attack and Release.
4. Or add **Compressor** and pick a **Sidechain source** for classic kick-driven bass ducking.

## Details

Sidechaining ducks one sound when another is loud — the pumping heard in EDM when the kick hits.

### Sidechain effect

| Mode | Behaviour |
| --- | --- |
| Tempo | Ghost-kick pump synced to project tempo |
| Track | Ducks when the chosen source track is loud |

### Compressor sidechain

Add **Compressor** to the target chain; select the kick (or any track/group) as **Sidechain source**.

**Track order matters** — the sidechain source track is processed before tracks that read from it.

### Alternative

**Volume LFO** on the track inspector gives rhythmic pump/tremolo without an envelope follower.

## Related

- [Mixer & export](mixer-and-export.md)
- [Plugins](plugins.md)
- [Timeline & clips](timeline-and-clips.md)
