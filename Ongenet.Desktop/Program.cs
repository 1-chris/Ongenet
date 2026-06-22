using Avalonia;
using Avalonia.Media;
using System;

namespace Ongenet.Desktop;

// The shared Application type lives in the Ongenet.App namespace; alias it so the bare name `App` here
// doesn't bind to the sibling `Ongenet.App` namespace instead of the type.
using SharedApp = Ongenet.App.App;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Plug the native desktop stack (PortAudio / OS-native audio + MIDI, CLAP + LV2 hosting) into the
        // shared App, then run the classic multi-window desktop lifetime.
        SharedApp.Platform = new DesktopPlatform();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<SharedApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new FontManagerOptions
            {
                // Emoji is a last-resort glyph fallback on EVERY platform, never a primary text family.
                // The font manager only reaches it for codepoints the text fonts can't supply, so digits
                // and letters always render in Inter — keeping the emoji keycap glyphs off the digits.
                FontFallbacks = new[]
                {
                    new FontFallback { FontFamily = "avares://Ongenet.App/Assets/Fonts/NotoColorEmoji.ttf#Noto Color Emoji" }
                }
            })
            .LogToTrace();
}
