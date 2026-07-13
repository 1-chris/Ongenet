# Video & composition

Make a simple music video inside Ongenet: a background image (or video) with live **audio visualisers** — moving bars or waveforms — for each part of your song (drums, bass, leads, and so on).

## Before you start

Turn on video in two places:

1. **Settings → General → Enable video features** — shows the **Video** tab in the main window.
2. Open the **Video** tab and click **Enable video** — turns on video for the current project.

## Tutorial: visualiser video with a background

This walkthrough builds a video with a full-screen background and three coloured visualiser bands (drums, bass, leads).

### 1. Group your tracks in Arrangement

In the **Arrangement** tab, create **group tracks** for the main sections of your song — for example **Drums**, **Bass**, and **Leads**. Route your existing tracks into the matching group so each group plays the full mix for that section.

You do not need to finish the whole song first; you just need groups with audio routed into them.

### 2. Add a background layer

1. Open the **Video** tab.
2. Click **+ Layer** (no file dialog appears).
3. In the timeline inspector below, set **Content** to **Media**.
4. Click **Browse** and choose a background **image** (PNG or JPEG works well).
5. On the program monitor, drag the image to fill the canvas if needed.

Put the background on the **bottom** of the stack (see step 5 below if it covers the visualisers).

### 3. Add a visualiser for each group

For each group (Drums, Bass, Leads):

1. Click **+ Layer** again.
2. Set **Content** to **Audio visualiser**.
3. Under **Audio source**, pick the matching group (e.g. `Drums (Group)`).
4. Choose a **Visualiser type**:
   - **Volume bars** — classic level meters
   - **Waveform** — oscilloscope-style shape
   - **Spectrum** — frequency curve (you can set min/max Hz and line thickness)
5. Pick **Color mode** (solid or gradient) and colours that read clearly on your background.
6. Adjust **Bounds** (X, Y, W, H) so each band sits in its own strip — for example stack three bands vertically with `Y` around `0.70`, `0.82`, and `0.94`, and `H` around `0.12`.

Press **Play** to check that each visualiser moves with its group’s audio.

### 4. Layer order

Layers drawn **later** appear **on top**. To change order:

- **Drag the layer name** on the left of the video timeline up or down, or
- Right-click the layer name → **Move up** / **Move down**.

Keep the background at the **bottom** and visualisers above it.

### 5. When each visualiser appears (optional)

By default a layer is visible for the whole song. To show a visualiser only during part of the track:

- Double-click or drag on a layer row to add a **visibility region**, or
- Click **+ Region** with a layer selected.

The layer is visible only while the playhead is inside its region(s).

### 6. Export your video

1. Choose **Export ▾ → Export video…** in the title bar (shown when video is enabled for the app and project).
2. Confirm **Export composited video** is checked.
3. Click **Export**, pick where to save, and wait for the MP4.

If export fails, install **ffmpeg** on your computer and try again.

## Quick reference

| I want to… | Do this |
| --- | --- |
| Add an empty layer | **+ Layer** (Resources, program monitor, or video timeline) |
| Add a background image | Layer → **Media** → **Browse** |
| Add a live visualiser | Layer → **Audio visualiser** → pick track/group |
| Move or resize on screen | Drag on the program monitor |
| Reorder layers | Drag layer names on the timeline left |
| Limit when a layer shows | Visibility regions on the layer row |
| Export MP4 | **Export ▾ → Export video…** |

## Related

- [Getting started](getting-started.md)
- [Mixer & export](mixer-and-export.md)
