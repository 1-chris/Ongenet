# Creating 3D visual effects

This guide walks you through building an effect whose UI is a **GPU-rendered, audio-modulated 3D visual** —
like Ongenet's built-in **3D Scope**, which leaves the audio untouched and draws the signal as a smoothed
waveform in 3D with a fading trail. You'll wire real audio into a hardware-rendered control that lives in
the Effects panel and can pop out into its own resizable window.

It assumes you've read **[creating-effects.md](creating-effects.md)** — we reuse the effect contract,
`Process`, and the registry from there, and only the *visual* parts are new.

> **Desktop-only.** The 3D engine is native (Vulkan / MoltenVK) and is wired into the **desktop** head only.
> The control degrades to a quiet placeholder on the web/Android heads or when no GPU is available, so it's
> always safe to put in shared UI — it just won't render a scene there.

---

## 1. The big picture

A 3D visual effect has two halves that meet in the middle:

```
audio thread                         UI thread (≈display rate)
────────────                         ─────────────────────────
Effect.Process(buffer)               Engine3DVisualHost
  └─ taps audio → ring buffer          └─ Engine3DView  (owns a GPU session)
        (IWaveformSource)                    └─ your IEngine3DVisualization
                                                  • Build(scene)   – once
   CaptureLatest(dest) ───────────────────────────• Update(scene, dt) – every frame
                                                  • ApplyTheme(scene) – on theme change
                                             └─ render on a background thread → present
```

The moving pieces, and where they live:

| Piece | Project | Role |
| --- | --- | --- |
| `IAudioEffect` + `IWaveformSource` | `Ongenet.Core` | A normal effect that also exposes its audio for the UI. |
| `Scene` / `SceneNode` / `MeshData` / `Material` / `Camera` / `Light` | `Ongenet.Engine3D.Abstractions` | The **portable scene model** you build and animate. No GPU, no Avalonia. |
| `Engine3DView` | `Ongenet.App` (`Controls/`) | An Avalonia control that renders a `Scene` on the GPU. |
| `IEngine3DVisualization` | `Ongenet.App` (`Controls/Engine3D/`) | **Your visual**: `Build` / `Update` / `ApplyTheme` over a `Scene`. |
| `Engine3DVisualHost` | `Ongenet.App` (`Controls/`) | Reusable wrapper: hosts the view, drives your visual, tracks the theme, adds the *"Open in window"* button. |
| `WaveformVisualizerEffectViewModel` + a `DataTemplate` | `Ongenet.App` | Slots the visual into the effect card. |

You write the **effect** (Core), the **visualization** (App), and a tiny bit of **view-model + XAML** glue.
Everything else is reused.

The shipped example is split across these files — open them alongside this guide:

- [`WaveformVisualizerEffect.cs`](../Ongenet.Core/Audio/Effects/WaveformVisualizerEffect.cs)
- [`WaveformTrailVisualization.cs`](../Ongenet.App/Controls/Engine3D/WaveformTrailVisualization.cs)
- [`WaveformVisualizerEffectViewModel.cs`](../Ongenet.App/ViewModels/Effects/WaveformVisualizerEffectViewModel.cs)
- [`EffectChainView.axaml`](../Ongenet.App/Views/Panels/EffectChainView.axaml) (the `3D Scope` `DataTemplate`)

---

## 2. Step 1 — the effect that taps audio

Our effect changes nothing about the sound; it only needs to expose the audio flowing through it so the
UI can draw it. Two interfaces:

- `IAudioEffect` — the usual contract (`Process` rewrites the buffer in place; ours leaves it alone).
- `IWaveformSource` — a tiny "give me the latest mono samples" seam the UI polls. Same shape as
  `ISpectrumSource`; it just means "raw waveform" rather than "for FFT".

The audio→UI hand-off is a **lock-free ring buffer**, [`SpectrumScope`](../Ongenet.Core/Audio/Dsp/SpectrumScope.cs):
the audio thread calls `Tap`, the UI thread calls `CaptureLatest`. Never lock or allocate on the audio
thread (see [creating-effects.md](creating-effects.md) §3).

```csharp
// Ongenet.Core/Audio/Effects/WaveformVisualizerEffect.cs
public sealed class WaveformVisualizerEffect : IAudioEffect, IWaveformSource
{
    public const string TypeId = "waveform-visualizer";

    private readonly SpectrumScope _scope = new();   // lock-free mono ring
    private int _channels = 2;
    private int _sampleRate = 44100;

    string IAudioEffect.TypeId => TypeId;
    public string Name => "3D Scope";
    public bool Enabled { get; set; } = true;

    // No knobs: a pure visual tap that must not alter the signal.
    public IReadOnlyList<Parameter> Parameters { get; } = Array.Empty<Parameter>();
    public int SampleRate => _sampleRate;

    public void Prepare(AudioFormat format)
    {
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100;
    }

    public void Process(Span<float> buffer)
    {
        _scope.Tap(buffer, _channels);   // pass-through: tap, don't modify
    }

    public int CaptureLatest(float[] dest) => _scope.CaptureLatest(dest);   // UI thread
    public IAudioEffect Clone() => new WaveformVisualizerEffect { Enabled = Enabled };
}
```

That's a complete, valid effect already. (If your effect *does* change the audio, write the DSP in
`Process` as usual, then `Tap` whatever signal you want to visualize.)

**Register it** so it shows in the "+ Add effect" menu — one line in
[`EffectRegistry.cs`](../Ongenet.Core/Audio/Effects/EffectRegistry.cs):

```csharp
new EffectInfo(WaveformVisualizerEffect.TypeId, "3D Scope", () => new WaveformVisualizerEffect(), "Visualizer")
```

---

## 3. The scene model (what you'll build in 3D)

Everything 3D is described with the **portable** types in `Ongenet.Engine3D.Abstractions` — plain data, no
GPU calls, safe to touch on the UI thread:

- **`Scene`** — the root of it all: a transform tree (`Root`), a `Camera`, a list of `Light`s, and a
  `ClearColor`.
- **`SceneNode`** — a node in the tree: a TRS transform (`Position` / `Rotation` / `Scale`), an optional
  `Mesh` + `Material`, `Visible`, and `Children`. `node.AddChild(new SceneNode { … })`.
- **`MeshData`** — indexed triangle geometry. Use the primitive factories (`MeshData.Box/Sphere/Plane`) or
  build your own `Vertex[]` + `uint[]`. A `Vertex` is `(Vector3 position, Vector3 normal, Vector4 color)`.
- **`Material`** — `BaseColor` (RGBA, alpha blends), `Metallic`, `Roughness`, `Emissive` (self-lit glow).
- **`Camera`** — an **orbit** camera: `Target`, `Distance`, `Yaw`, `Pitch`, `FieldOfView`. Set these to view
  your scene "at an angle"; the user can drag/scroll to orbit/zoom on top.

### Dynamic meshes (the key to animation)

A waveform changes every frame. Re-creating a `MeshData` per frame would leak GPU buffers, so the engine
supports **dynamic meshes**: create once with a fixed vertex count, then swap the vertices each frame.

```csharp
var mesh = MeshData.CreateDynamic(vertexCount, indices);   // once
…
mesh.UpdateVertices(newVertices);   // each frame: same length, bumps a Revision
```

The renderer caches one GPU buffer per mesh and re-uploads only when the `Revision` changes. **Always pass
a fresh (or unshared) array** to `UpdateVertices` and never mutate one you already handed over — the render
thread may be reading it. Publishing a new array by reference is what keeps the waveform tear-free.

---

## 4. Step 2 — the visualization

A visual implements **`IEngine3DVisualization`** (`Ongenet.App/Controls/Engine3D/`):

```csharp
public interface IEngine3DVisualization
{
    void Build(Scene scene);                 // one-time scene setup
    void Update(Scene scene, double dt);     // per-frame animation (dt = seconds)
    void ApplyTheme(Scene scene);            // (re)colour from the palette; build + on theme change
}
```

All three run on the **UI thread**, so you can freely mutate the `Scene`. Here's a minimal scope — a single
glowing ribbon that follows the smoothed waveform. (The shipped
[`WaveformTrailVisualization`](../Ongenet.App/Controls/Engine3D/WaveformTrailVisualization.cs) adds the
receding, fading "snapshot" trail behind it — the same idea, with a recycled pool of nodes.)

```csharp
public sealed class SimpleScopeVisualization : IEngine3DVisualization
{
    private const int Points = 160;
    private const int VertexCount = Points * 2;   // top + bottom of a thin ribbon per point

    private readonly IWaveformSource? _source;
    private readonly float[] _samples = new float[2048];
    private readonly float[] _display = new float[Points];
    private MeshData _mesh = null!;
    private Material _material = null!;
    private Vector3 _rgb = new(0.8f, 0.55f, 0.95f);

    public SimpleScopeVisualization(IWaveformSource? source) => _source = source;

    public void Build(Scene scene)
    {
        // View it at an angle; the user can still orbit/zoom.
        scene.Camera.Target = new Vector3(0, 0, 0);
        scene.Camera.Distance = 3.2f;
        scene.Camera.Yaw = 0.5f;
        scene.Camera.Pitch = 0.3f;

        _mesh = MeshData.CreateDynamic(VertexCount, BuildIndices());
        _mesh.UpdateVertices(BuildVertices(_display));
        _material = new Material { Roughness = 0.4f };
        scene.Root.AddChild(new SceneNode { Mesh = _mesh, Material = _material });
    }

    public void Update(Scene scene, double dt)
    {
        // 1) capture + average the latest audio into Points buckets, with temporal smoothing
        var n = _source?.CaptureLatest(_samples) ?? 0;
        var bucket = n > 0 ? (float)n / Points : 0f;
        for (var i = 0; i < Points; i++)
        {
            var target = 0f;
            if (n > 0)
            {
                int a = (int)(i * bucket), b = (int)((i + 1) * bucket);
                if (b <= a) b = a + 1;
                float sum = 0; for (var s = a; s < b && s < n; s++) sum += _samples[s];
                target = sum / Math.Max(1, b - a);
            }
            _display[i] += (target - _display[i]) * 0.4f;   // smooth frame-to-frame
        }

        // 2) rebuild the ribbon (fresh array → safe for the render thread)
        _mesh.UpdateVertices(BuildVertices(_display));
    }

    public void ApplyTheme(Scene scene)
    {
        _rgb = ToRgb(ThemePalette.Mauve);
        scene.ClearColor = new Vector4(ToRgb(ThemePalette.Crust), 1f);
        if (_material is not null)
        {
            _material.BaseColor = new Vector4(_rgb, 1f);
            _material.Emissive = _rgb * 0.7f;   // make it glow regardless of lighting
        }
    }

    private static Vertex[] BuildVertices(float[] display)
    {
        const float halfWidth = 1.5f, amp = 0.95f, thick = 0.02f;
        var v = new Vertex[VertexCount];
        var n = new Vector3(0, 0, 1);
        var white = new Vector4(1, 1, 1, 1);   // material supplies the real colour
        for (var i = 0; i < Points; i++)
        {
            var x = -halfWidth + 2f * halfWidth * i / (Points - 1);
            var y = Math.Clamp(display[i] * amp, -1.3f, 1.3f);
            v[2 * i]     = new Vertex(new Vector3(x, y + thick, 0), n, white);
            v[2 * i + 1] = new Vertex(new Vector3(x, y - thick, 0), n, white);
        }
        return v;
    }

    private static uint[] BuildIndices()
    {
        var idx = new uint[(Points - 1) * 6];
        var o = 0;
        for (var i = 0; i < Points - 1; i++)
        {
            uint tA = (uint)(2 * i), bA = (uint)(2 * i + 1), tB = (uint)(2 * (i + 1)), bB = (uint)(2 * (i + 1) + 1);
            idx[o++] = tA; idx[o++] = tB; idx[o++] = bB;
            idx[o++] = tA; idx[o++] = bB; idx[o++] = bA;
        }
        return idx;
    }

    private static Vector3 ToRgb(Avalonia.Media.Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f);
}
```

Notes:

- **Smoothing/averaging** lives here, not in the audio thread — bucket-average the captured window, then
  ease the display toward it each frame so the line glides.
- **Colours come from the theme** via `ThemePalette` (`Mauve`, `Sky`, `Crust`, … — see
  [theming.md](theming.md)). Because `ApplyTheme` is called again whenever the user changes theme, the
  visual updates live.
- **Emissive** makes geometry visible regardless of the light, which suits glowing UI accents.

---

## 5. Step 3 — host it (and get the pop-out window for free)

You don't talk to `Engine3DView` directly. The reusable
[`Engine3DVisualHost`](../Ongenet.App/Controls/Engine3DVisualHost.cs) does everything: it owns the GPU view,
builds your visualization, re-applies the theme on change, and adds the generic **"⤢ Open in window"**
button that re-hosts the same visual in a freely resizable
[`Engine3DVisualWindow`](../Ongenet.App/Views/Windows/Engine3DVisualWindow.axaml). All it needs is a
**factory** that makes a fresh visualization (one per view — the embedded card and the pop-out window each
get their own, both reading the same audio source):

```csharp
// Ongenet.App/ViewModels/Effects/WaveformVisualizerEffectViewModel.cs
public sealed class WaveformVisualizerEffectViewModel : EffectViewModel
{
    public WaveformVisualizerEffectViewModel(WaveformVisualizerEffect effect, Action<EffectViewModel> remove,
        Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
        : base(effect, remove, moveUp, moveDown)
    {
        var source = effect as IWaveformSource;
        VisualizationFactory = () => new WaveformTrailVisualization(source);
    }

    public Func<IEngine3DVisualization> VisualizationFactory { get; }
}
```

Route the effect to this view-model in
[`EffectChainViewModel.Rebuild()`](../Ongenet.App/ViewModels/Effects/EffectChainViewModel.cs) (Avalonia
picks the most specific `DataTemplate` per item — see [creating-effects.md](creating-effects.md) §9 for how
custom effect UIs are selected):

```csharp
Effects.Add(effect switch
{
    // … existing cases …
    WaveformVisualizerEffect wv => new WaveformVisualizerEffectViewModel(wv, RemoveEffect, MoveUp, MoveDown),
    _ => new EffectViewModel(effect, RemoveEffect, MoveUp, MoveDown)
});
```

Finally, the card's `DataTemplate` in
[`EffectChainView.axaml`](../Ongenet.App/Views/Panels/EffectChainView.axaml) — the shared `EffectHeaderView`
on top, then the host:

```xml
<DataTemplate DataType="fx:WaveformVisualizerEffectViewModel">
    <Border Margin="0,0,0,8" Padding="10" CornerRadius="4"
            Background="{StaticResource CatppuccinSurface0}"
            BorderBrush="{StaticResource CatppuccinSurface1}" BorderThickness="1">
        <StackPanel Spacing="6">
            <panels:EffectHeaderView/>
            <Border Height="240" CornerRadius="4" ClipToBounds="True"
                    BorderBrush="{StaticResource CatppuccinSurface1}" BorderThickness="1">
                <controls:Engine3DVisualHost VisualizationFactory="{Binding VisualizationFactory}"
                                             Title="{Binding Name}"/>
            </Border>
        </StackPanel>
    </Border>
</DataTemplate>
```

That's it — add the **3D Scope** to a track, and the waveform renders in the card and pops out on demand.

---

## 6. Threading, framerate & fallback (good to know)

- **Framerate.** `Engine3DView` ticks at display refresh (via the `FrameTicker`'s `RequestAnimationFrame`
  loop) and runs the actual GPU render + readback on a **background thread**, triple-buffering frames so the
  UI never blocks. Your `Update` runs once per UI frame.
- **Thread safety.** `Build`/`Update`/`ApplyTheme` are UI-thread; the render thread reads an immutable
  `SceneSnapshot` captured right after `Update`. Dynamic-mesh vertices are published by reference-swap, so
  the render thread always sees a complete array. Keep `Update` allocation-light but don't fear a fresh
  vertex array per frame — it's tiny.
- **Graceful fallback.** If there's no GPU engine (web/Android, or no Vulkan device) the control shows a
  placeholder and the *"Open in window"* button hides itself. Your effect's audio path is unaffected.
- **Reuse.** `Engine3DVisualHost` + `IEngine3DVisualization` aren't effect-specific — use the same pattern to
  drop an audio-reactive (or any) 3D visual into an instrument inspector, a meter, or anywhere else.

---

## 7. Recap — the checklist

| Step | File |
| --- | --- |
| 1. Pass-through effect that taps audio (`IAudioEffect` + `IWaveformSource`, `SpectrumScope`) | `Ongenet.Core/Audio/Effects/…` |
| 2. Register it | `Ongenet.Core/Audio/Effects/EffectRegistry.cs` |
| 3. The visualization (`IEngine3DVisualization`: build a scene, update from audio, theme it) | `Ongenet.App/Controls/Engine3D/…` |
| 4. A view-model exposing `Func<IEngine3DVisualization> VisualizationFactory` | `Ongenet.App/ViewModels/Effects/…` |
| 5. Switch case in `EffectChainViewModel.Rebuild()` | `Ongenet.App/ViewModels/Effects/EffectChainViewModel.cs` |
| 6. A `DataTemplate` hosting `Engine3DVisualHost` | `Ongenet.App/Views/Panels/EffectChainView.axaml` |

For the engine internals (RHI, the Vulkan backend, MoltenVK on macOS, presenters), see
[DEVELOPMENT.md §9](../DEVELOPMENT.md). For the audio side, see
[creating-effects.md](creating-effects.md); for colours, [theming.md](theming.md).
