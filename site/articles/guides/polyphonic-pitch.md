# Polyphonic pitch

## Quick steps

1. Select an audio clip on the timeline and open the **Pitch Editor** (bottom panel or dedicated view).
2. Click **Analyze** to detect note segments (polyphonic blobs).
3. Drag pitch handles per segment; listen with real-time crossfaded playback.
4. **Flatten** to bake corrections into a new audio clip when satisfied.

## Details

Built-in VariAudio-class editing — no third-party Melodyne required for basic polyphonic correction:

- **Analyze** — splits audio into editable pitch segments
- **Edit** — drag segments vertically (pitch) and horizontally (timing)
- **Playback** — hear changes live with crossfades between segments
- **Flatten** — renders corrected audio to a new clip

Limitations vs dedicated pitch plugins: best for monophonic lines and simple polyphony; complex dense chords may need manual cleanup.

See [Dev: Polyphonic pitch architecture](/dev/polyphonic-pitch.html) for the signal path and engine integration.

## Related

- [Audio Editor](audio-editor.md)
- [Timeline & clips](timeline-and-clips.md)
