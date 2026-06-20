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
                FontFallbacks = OperatingSystem.IsLinux()
                    ? new[]
                    {
                        new FontFallback { FontFamily = "avares://Ongenet.Desktop/Assets/Fonts/NotoColorEmoji.ttf#Noto Color Emoji" }
                    }
                    : Array.Empty<FontFallback>()
            })
            .LogToTrace();
}