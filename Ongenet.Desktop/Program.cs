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
                // The default family must be a font that actually exists, or the very first text-render
                // (the compositor's diagnostic renderer) throws "Could not create glyphTypeface" before
                // the app's styles apply. "Noto Sans" ships on our Linux dev/CI boxes but NOT on macOS,
                // which crashed the Mac build at startup. Use the embedded Inter font (bundled by
                // WithInterFont, so always present) as the default on macOS only — Linux/Windows keep
                // their existing "Noto Sans" default and render exactly as before.
                DefaultFamilyName = OperatingSystem.IsMacOS()
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