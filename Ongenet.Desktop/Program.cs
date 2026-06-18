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
                DefaultFamilyName = "Noto Sans",
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