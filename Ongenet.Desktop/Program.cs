using Avalonia;
using Avalonia.Media;
using System;

namespace Ongenet.Desktop;

class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);
    
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new FontManagerOptions
            {
                // The default family must be a font that actually exists, or text rendered directly with
                // Typeface.Default (custom-drawn controls like the piano-roll ruler / spectrum graph) and
                // the compositor's diagnostic renderer throw "Could not create glyphTypeface". Stock-font
                // availability differs per-OS (e.g. "Noto Sans" ships on Linux but not macOS/Windows), which
                // crashed those builds. Use the embedded Inter font (bundled by WithInterFont, so always
                // present on every platform) as the default everywhere — no OS branching, no missing-font crash.
                DefaultFamilyName = "avares://Avalonia.Fonts.Inter/Assets#Inter",
                // Inter (above) covers the UI text. The bundled colour-emoji fallback is Linux-only: Avalonia
                // on Linux doesn't resolve a system emoji font on its own, so emojis dotted around the UI go
                // missing without it. macOS and Windows resolve their native emoji fonts automatically, so we
                // leave their fallback list empty — keeping our emoji font out of the chain there also avoids
                // it hijacking ASCII digits (it maps 0-9/#/* as keycap bases, rendering them dark + wide-spaced).
                FontFallbacks = OperatingSystem.IsLinux()
                    ? new[]
                    {
                        new FontFallback { FontFamily = "avares://Ongenet.Desktop/Assets/Fonts/NotoColorEmoji.ttf#Noto Color Emoji" }
                    }
                    : Array.Empty<FontFallback>()
            })
            .LogToTrace();
}