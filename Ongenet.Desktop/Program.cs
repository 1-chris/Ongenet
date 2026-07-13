using Avalonia;
using Avalonia.Media;
using Avalonia.Native;
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
        // Local crash dialog + log for managed unhandled exceptions (no telemetry).
        CrashReporter.Install();

        try
        {
            // Plug the native desktop stack (OS-native audio + MIDI, CLAP + LV2 hosting) into the
            // shared App, then run the classic multi-window desktop lifetime.
            SharedApp.Platform = new DesktopPlatform();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            CrashReporter.ReportFatal(ex);
            Environment.Exit(1);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<SharedApp>()
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
            });

        // Avalonia 12 defaults to Metal on macOS. Its async presentation can drop frames under load,
        // which reads as a subtle whole-window brightness flicker (see Avalonia #4500 / #21204).
        // OpenGL was the stable default in v11; prefer it unless explicitly overridden.
        if (OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable("ONGENET_METAL") != "1")
        {
            builder = builder.With(new AvaloniaNativePlatformOptions
            {
                RenderingMode =
                [
                    AvaloniaNativeRenderingMode.OpenGl,
                    AvaloniaNativeRenderingMode.Metal,
                    AvaloniaNativeRenderingMode.Software
                ]
            });
        }

        return builder.LogToTrace();
    }
}
