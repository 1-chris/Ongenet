# Audio Editor

## Quick steps

1. Open **View → Audio Editor** for the standalone multitrack sample editor.
2. Or right-click an audio clip on the timeline → **Open in Audio Editor**.
3. Stack multiple clips as lanes; use cut, copy, paste, trim, normalize, and fades.
4. Double-click the waveform to audition; middle-drag to zoom.

## Details

The Audio Editor is Edison-class sample surgery — same engine as the Sample Inspector:

- **Multitrack lanes** — open several clips side by side
- **Spectral overlay**, normalize, fades
- **Shared buffers** — edits to a shared source affect every clip referencing it

Toolbar tools mirror the in-clip Sample Inspector for consistent editing workflows.

See [Dev: Audio Editor architecture](/dev/audio-editor.html) for implementation details.

## Related

- [Polyphonic pitch](polyphonic-pitch.md)
- [Timeline & clips](timeline-and-clips.md)
- [Samples & libraries](samples-and-libraries.md)
