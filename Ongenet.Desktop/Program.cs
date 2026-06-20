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
                // Emoji is a last-resort glyph fallback on EVERY platform, never a primary text family.
                // The font manager only reaches it for codepoints the text fonts can't supply, so digits
                // and letters always render in Inter — keeping the emoji keycap glyphs off the digits.
                FontFallbacks = new[]
                {
                    new FontFallback { FontFamily = "avares://Ongenet.Desktop/Assets/Fonts/NotoColorEmoji.ttf#Noto Color Emoji" }
                }
            })
            .LogToTrace();
}