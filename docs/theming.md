# The theming system

Ongenet is fully themeable, and themes switch **live** — pick a new theme (or drag a colour slider in
the theme editor) and the entire UI recolours instantly, with no restart and no flicker. This guide
explains how that works, and how to add new colour tokens, new themes, and theme-aware controls.

All theming code lives in the shared UI library [`Ongenet.App`](../Ongenet.App), under
[`Theming/`](../Ongenet.App/Theming) plus
[`Controls/ThemedControl.cs`](../Ongenet.App/Controls/ThemedControl.cs) and
[`App.axaml`](../Ongenet.App/App.axaml). Both the desktop and web heads get theming for free because
they share this library.

---

## 1. The big idea: one brush, shared everywhere

The trick that makes live theming work is simple: **there is exactly one `SolidColorBrush` object per
colour token, and the whole UI references that same object.** To change the theme, the code just sets
`brush.Color = newColor` on those shared objects. Because every control is pointing at the *same* brush
instance, they all repaint with the new colour automatically. Nothing is recreated.

Compare that to the naive approach (swap in a whole new set of brushes / reload styles), which would
require rebuilding the visual tree. Ongenet instead **mutates the existing brushes in place**.

There are two ways controls consume those brushes:

1. **XAML controls** use `{StaticResource CatppuccinMauve}` — they hold a reference to the shared brush,
   so they update for free.
2. **Custom-drawn controls** (things that paint themselves with `Render`) cache their own pens/brushes,
   so they need a tiny bit of help — that's what `ThemedControl` (below) provides.

---

## 2. The semantic colour tokens

Colours are referred to by **name**, not by literal hex, so the same UI works under any theme. The
palette is the [Catppuccin](https://catppuccin.com/) set — **26 tokens**, defined once as an ordered
list in [`ThemePalette.cs`](../Ongenet.App/Theming/ThemePalette.cs):

```16:23:Ongenet.App/Theming/ThemePalette.cs
        /// <summary>Token names, in palette order. The resource keys are "Catppuccin" + name.</summary>
        public static readonly IReadOnlyList<string> TokenNames = new[]
        {
            "Rosewater", "Flamingo", "Pink", "Mauve", "Red", "Maroon", "Peach", "Yellow",
            "Green", "Teal", "Sky", "Sapphire", "Blue", "Lavender",
            "Text", "Subtext1", "Subtext0", "Overlay2", "Overlay1", "Overlay0",
            "Surface2", "Surface1", "Surface0", "Base", "Mantle", "Crust"
        };
```

They fall into two groups, and using the *semantic* meaning (rather than "purple") is what keeps the UI
coherent across light and dark themes:

- **14 accent colours** — `Rosewater Flamingo Pink Mauve Red Maroon Peach Yellow Green Teal Sky Sapphire
  Blue Lavender`. `Mauve` is the app's primary accent.
- **12 surface/text colours**, from brightest text to darkest background — `Text`, `Subtext1/0`,
  `Overlay2/1/0`, `Surface2/1/0`, `Base`, `Mantle`, `Crust`. As a rough guide: **Text** for foreground,
  **Base** for the main window background, **Mantle**/**Crust** for darker panels and borders,
  **Surface\*** for cards and controls.

### How a token maps to resource keys

Each token name produces a predictable set of keys:

| Layer | Key format | Example (Mauve) |
| --- | --- | --- |
| Token name (code/JSON) | bare name | `Mauve` |
| `Color` resource | `Catppuccin{name}Color` | `CatppuccinMauveColor` |
| `SolidColorBrush` resource | `Catppuccin{name}` | `CatppuccinMauve` |

Both are declared in [`App.axaml`](../Ongenet.App/App.axaml) (the startup values are Catppuccin Mocha):

```xml
<Color x:Key="CatppuccinMauveColor">#cba6f7</Color>
<!-- ... -->
<SolidColorBrush x:Key="CatppuccinMauve" Color="{StaticResource CatppuccinMauveColor}"/>
```

### Reading tokens from C#

For code that can't use XAML resources (custom controls, code-built dialogs), `ThemePalette` exposes a
named accessor per token plus helpers:

```30:42:Ongenet.App/Theming/ThemePalette.cs
        public static IBrush BrushOf(string token) =>
            Brushes.TryGetValue(token, out var b) ? b : Avalonia.Media.Brushes.Magenta;

        public static Color ColorOf(string token) =>
            Brushes.TryGetValue(token, out var b) ? b.Color : Colors.Magenta;

        /// <summary>An opaque token colour with a custom alpha (for translucent overlays/fills).</summary>
        public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        /// <summary>Raised after a theme is applied; custom-drawn controls subscribe to repaint.</summary>
        public static event Action? Changed;
```

So you can write `ThemePalette.Mauve` (a `Color`), `ThemePalette.BrushOf("Base")` (the shared brush), or
`ThemePalette.WithAlpha(ThemePalette.Mauve, 0x40)` for a translucent fill. These accessors always return
the *current* theme's value because they read from the same mutated brushes.

---

## 3. How a theme is applied (live mutation)

A theme is just a name, a light/dark variant, and a token→colour map
([`ThemeDefinition.cs`](../Ongenet.App/Theming/ThemeDefinition.cs)). The
[`ThemeService`](../Ongenet.App/Theming/ThemeService.cs) does the work.

**Step 1 — at startup, capture the shared brushes.** `Initialize()` looks up each `Catppuccin{token}`
brush that XAML created and registers it with `ThemePalette`, so code and XAML share one instance per
token:

```24:37:Ongenet.App/Theming/ThemeService.cs
        public void Initialize()
        {
            var app = Application.Current;
            if (app is null) return;

            // Grab the shared brush instances created in App.axaml so mutating them re-themes the whole UI.
            foreach (var token in ThemePalette.TokenNames)
            {
                if (app.TryGetResource("Catppuccin" + token, null, out var res) && res is SolidColorBrush brush)
                    ThemePalette.Register(token, brush);
            }

            Apply(BuiltInThemes.Default);
        }
```

**Step 2 — applying a theme mutates those brushes in place** and nudges everything to repaint:

```39:60:Ongenet.App/Theming/ThemeService.cs
        public void Apply(ThemeDefinition theme)
        {
            var app = Application.Current;
            if (app is null) return;

            foreach (var token in ThemePalette.TokenNames)
            {
                if (!theme.Tokens.TryGetValue(token, out var color)) continue;
                _tokens[token] = color;
                if (app.TryGetResource("Catppuccin" + token, null, out var res) && res is SolidColorBrush brush)
                    brush.Color = color; // mutate in place → all {StaticResource} references update live
                app.Resources["Catppuccin" + token + "Color"] = color;
            }

            _name = theme.Name;
            _variant = theme.Variant;
            app.RequestedThemeVariant = theme.Variant;

            ThemePalette.RaiseChanged();
            ThemeChanged?.Invoke();
            InvalidateAllWindows();
        }
```

The three "tell everyone" calls at the end are:

- **`ThemePalette.RaiseChanged()`** — fires the event that `ThemedControl` (custom-drawn controls)
  listen to so they can rebuild their cached pens/brushes.
- **`ThemeChanged?.Invoke()`** — lets app services react (e.g. persist the choice).
- **`InvalidateAllWindows()`** — forces a repaint of every window and descendant so the new colours show
  immediately.

There's also `SetToken(token, color)` — the same mechanism for a *single* token, used by the theme
editor so dragging a colour slider updates the UI in real time.

> **Why `App.axaml` only defines a "Dark" FluentTheme palette:** the `RequestedThemeVariant` is still
> toggled Light/Dark by the service, and Fluent's accent/region/error colours pull from the same shared
> `Color` resources that get mutated — so a light theme like Latte still works.

---

## 4. `ThemedControl` — making a custom-drawn control theme-aware

XAML controls update for free (they reference the shared brush). But a control that paints itself in
`Render` usually caches its own `Pen`/`IBrush` objects in fields for performance — and those are
*separate* objects, so mutating the app brush won't touch them.

[`ThemedControl`](../Ongenet.App/Controls/ThemedControl.cs) bridges that gap. Inherit from it, and it
will (re)build your cached resources whenever the control attaches **and** whenever the theme changes,
then repaint:

```12:35:Ongenet.App/Controls/ThemedControl.cs
    public abstract class ThemedControl : Control
    {
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            BuildThemeResources();
            ThemePalette.Changed += OnThemeChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            ThemePalette.Changed -= OnThemeChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnThemeChanged()
        {
            BuildThemeResources();
            InvalidateVisual();
        }

        /// <summary>Rebuild cached pens/brushes from <see cref="ThemePalette"/>. Called on attach + theme change.</summary>
        protected virtual void BuildThemeResources() { }
    }
```

To use it, override `BuildThemeResources()` and assign your cached fields from `ThemePalette`. Example
from [`CurveEditorControl`](../Ongenet.App/Controls/CurveEditorControl.cs):

```csharp
protected override void BuildThemeResources()
{
    _linePen = new Pen(new SolidColorBrush(ThemePalette.Mauve), 1.8);
    _gridPen = new Pen(new SolidColorBrush(ThemePalette.Surface1), 1);
    _handleFill = new SolidColorBrush(ThemePalette.Text);
    _handleStroke = new Pen(new SolidColorBrush(ThemePalette.Base), 1);
    _fill = new SolidColorBrush(ThemePalette.Mauve, 0.12);
}
```

Then just use those fields in your `Render` override. If you subclass another themed control, call
`base.BuildThemeResources()` first. The custom controls already built this way include `Knob`,
`LevelMeterControl`, `MasterMeterControl`, `TimelineGridControl`, `PianoRollBackgroundControl`,
`AutomationLaneControl`, `EqGraphControl`, `FilterResponseControl`, and more — all good references.

> **Tip:** an even simpler option for cheap controls is to read `ThemePalette.*` directly inside
> `Render` (so you never cache anything). `LevelMeterControl` builds its gradient fresh each frame that
> way. Caching + `ThemedControl` is the better choice when building the brushes is non-trivial.

### What does *not* update live (avoid these)

- **Hard-coded hex in XAML** (e.g. `Background="#33CBA6F7"`) — a fixed value, not a token reference.
  Prefer `{StaticResource Catppuccin…}`.
- **Code that snapshots a colour once** and never subscribes to `ThemePalette.Changed` (some
  code-built dialogs do this). If you build brushes in a constructor, subscribe to `Changed` or use
  `ThemedControl`.

### Track / entity colours

Domain objects (like tracks) store a colour as a **key string** (e.g. `"CatppuccinMauve"`) and bind
through the `ColorKeyToBrush` converter
([`CoreColorToBrushConverter`](../Ongenet.App/Converters/CoreColorToBrushConverter.cs)), which resolves
the key against the live application brushes — so track colours also follow the theme.

---

## 5. JSON import / export

Themes round-trip to JSON so users can share them. The DTO is simple:

```90:95:Ongenet.App/Theming/ThemeService.cs
        private sealed class ThemeDto
        {
            [JsonPropertyName("name")] public string? Name { get; set; }
            [JsonPropertyName("variant")] public string? Variant { get; set; }
            [JsonPropertyName("colors")] public Dictionary<string, string>? Colors { get; set; }
        }
```

A theme file looks like this — `colors` keys are the **bare token names**, values are `#rrggbb`:

```json
{
  "name": "Catppuccin Mocha",
  "variant": "Dark",
  "colors": {
    "Rosewater": "#f5e0dc",
    "Mauve": "#cba6f7",
    "Text": "#cdd6f4",
    "Base": "#1e1e2e",
    "Crust": "#11111b"
  }
}
```

- **`variant`** is `"Light"` or `"Dark"` (case-insensitive). If it's missing, the service infers it from
  the `Base` colour's luminance.
- **Missing tokens** are skipped silently on import; export only writes the tokens present in the theme.

`ExportJson` / `ImportJson` handle the conversion, and the **Settings → Theme** tab
([`ThemeEditorView`](../Ongenet.App/Views/Settings/ThemeEditorView.axaml)) wires up the file pickers and
the per-token colour sliders (which call `SetToken` for live preview).

### Persistence note

Saved settings store only the **built-in theme name + light/dark flag**
([`AppSettingsService`](../Ongenet.App/Services/AppSettingsService.cs)). Custom per-token edits and
imported (non-built-in) themes apply for the session but aren't yet persisted across restarts — if you
want a tweaked theme to survive, export it to JSON.

---

## 6. The built-in themes

Four Catppuccin flavours ship, defined in
[`BuiltInThemes.cs`](../Ongenet.App/Theming/BuiltInThemes.cs) — the **only** place colour codes are
hard-coded (as theme *definitions*, not UI usage):

| Name | Variant |
| --- | --- |
| **Catppuccin Mocha** (default) | Dark |
| **Catppuccin Macchiato** | Dark |
| **Catppuccin Frappé** | Dark |
| **Catppuccin Latte** | Light |

```51:59:Ongenet.App/Theming/BuiltInThemes.cs
        public static IReadOnlyList<ThemeDefinition> All { get; } = new[]
        {
            Build("Catppuccin Mocha", ThemeVariant.Dark, Mocha),
            Build("Catppuccin Macchiato", ThemeVariant.Dark, Macchiato),
            Build("Catppuccin Frappé", ThemeVariant.Dark, Frappe),
            Build("Catppuccin Latte", ThemeVariant.Light, Latte)
        };
```

Each flavour is a `string[]` of 26 hex values, in the **same order as `TokenNames`**, zipped into a
`ThemeDefinition` by `Build`.

---

## 7. How to extend the system

### Add a new built-in theme

In [`BuiltInThemes.cs`](../Ongenet.App/Theming/BuiltInThemes.cs):

1. Add a `private static readonly string[] YourTheme = { /* 26 hex values, TokenNames order */ };`
2. Add it to the `All` array:

```csharp
Build("Your Theme", ThemeVariant.Dark, YourTheme),
```

The theme-editor dropdown binds to `IThemeService.BuiltIns` (→ `BuiltInThemes.All`), so it appears
automatically. Or, for a one-off, skip code entirely and **import a JSON theme** from the editor.

### Add a new colour token

Tokens are looped over generically, so adding one touches four places (no service changes needed):

1. **`ThemePalette.TokenNames`** — append the name (mind the ordering; it must match the theme arrays).
2. **`ThemePalette`** — add an accessor: `public static Color NewToken => ColorOf("NewToken");`
3. **`App.axaml`** — add a `<Color x:Key="CatppuccinNewTokenColor">…</Color>` and a matching
   `<SolidColorBrush x:Key="CatppuccinNewToken" Color="{StaticResource CatppuccinNewTokenColor}"/>`.
4. **`BuiltInThemes.cs`** — append one hex value to **each** of the four flavour arrays, in the same
   position as the new token in `TokenNames`.

### Make a new control theme-aware

- **XAML view:** just bind to `{StaticResource CatppuccinTokenName}`. Live updates are free; nothing
  else to do.
- **Custom-drawn control:** inherit `ThemedControl`, cache `Pen`/`IBrush` fields, override
  `BuildThemeResources()` to assign them from `ThemePalette.*`, and use them in `Render()`. (Call
  `base.BuildThemeResources()` first if you derive from another themed control.)
- **Entity colour stored in a model:** store a key like `"CatppuccinMauve"` (or a `#rrggbb`) and bind
  through the `ColorKeyToBrush` converter.
- **Avoid** hard-coded hex in XAML if you want it to follow the theme.

---

## Startup order (for reference)

[`App.axaml.cs`](../Ongenet.App/App.axaml.cs) loads the XAML resources, registers `IThemeService` in DI,
calls `Initialize()` (which captures the brushes and applies Mocha), then `TryApplySettings()` re-applies
a saved built-in theme + variant if one exists.

---

## Key files

| File | Role |
| --- | --- |
| [`Theming/ThemePalette.cs`](../Ongenet.App/Theming/ThemePalette.cs) | The 26 tokens, shared-brush registry, `Changed` event, colour accessors |
| [`Theming/ThemeService.cs`](../Ongenet.App/Theming/ThemeService.cs) | Apply/SetToken (live mutation), JSON import/export, invalidation |
| [`Theming/ThemeDefinition.cs`](../Ongenet.App/Theming/ThemeDefinition.cs) | A theme = name + variant + token map |
| [`Theming/BuiltInThemes.cs`](../Ongenet.App/Theming/BuiltInThemes.cs) | The four Catppuccin flavours |
| [`Controls/ThemedControl.cs`](../Ongenet.App/Controls/ThemedControl.cs) | Base class for theme-aware custom-drawn controls |
| [`App.axaml`](../Ongenet.App/App.axaml) | The `Color` + `SolidColorBrush` resource definitions and global styles |
| [`Views/Settings/ThemeEditorView.axaml`](../Ongenet.App/Views/Settings/ThemeEditorView.axaml) | The Settings → Theme UI |
| [`Converters/CoreColorToBrushConverter.cs`](../Ongenet.App/Converters/CoreColorToBrushConverter.cs) | Resolves entity colour keys → live brushes |

---

## Where to go next

- [main-window-layout.md](main-window-layout.md) — the UI these colours paint.
- [creating-instruments.md](creating-instruments.md) / [creating-effects.md](creating-effects.md) — if
  you build a custom inspector control, theme it with `ThemedControl`.
