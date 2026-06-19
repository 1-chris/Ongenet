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
                // the compositor's diagnostic renderer throw "Could not create glyphTypeface". "Noto Sans"
                // ships on our Linux dev/CI boxes but NOT on macOS or stock Windows, which crashed those
                // builds. Use the embedded Inter font (bundled by WithInterFont, so always present) as the
                // default on macOS and Windows — Linux keeps its "Noto Sans" default and renders as before.
                DefaultFamilyName = OperatingSystem.IsMacOS() || OperatingSystem.IsWindows()
                    ? "avares://Avalonia.Fonts.Inter/Assets#Inter"
                    : "Noto Sans",
                FontFallbacks = new[]
                {
                    new FontFallback { FontFamily = "Inter" },
                    new FontFallback { FontFamily = "Noto Sans" },
                    new FontFallback { FontFamily = "avares://Ongenet.Desktop/Assets/Fonts/NotoColorEmoji.ttf#Noto Color Emoji" },
                    new FontFallback { FontFamily = "Noto Color Emoji" },
                    new FontFallback { FontFamily = "Noto Emoji" },
                    new FontFallback { FontFamily = "Apple Color Emoji" },
                    new FontFallback { FontFamily = "Segoe UI Emoji" },
                    new FontFallback { FontFamily = "Twitter Color Emoji" },
                    new FontFallback { FontFamily = "EmojiOne Color" },
                    new FontFallback { FontFamily = "JoyPixels" },
                    new FontFallback { FontFamily = "Symbola" },
                    new FontFallback { FontFamily = "DejaVu Sans" }
                }
            })
            .LogToTrace();
}