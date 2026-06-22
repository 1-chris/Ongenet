using System.Threading.Tasks;
using System.Runtime.InteropServices.JavaScript;
using Avalonia;
using Avalonia.Browser;
using Ongenet.Web.Audio;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("browser")]

namespace Ongenet.Web;

// The shared Application type lives in the Ongenet.App namespace; alias it so the bare name `App` here
// doesn't bind to the sibling `Ongenet.App` namespace instead of the type.
using SharedApp = Ongenet.App.App;

internal sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        // Load the JS glue module that owns the WebAudio graph before any audio starts.
        // The path is resolved relative to the .NET runtime (which lives in _framework/), so go up one
        // level to reach ongen-audio.js at the app root. This holds in the dev server and at /app/ on Pages,
        // since _framework/ is always a subfolder of the app root.
        await JSHost.ImportAsync("ongen-audio", "../ongen-audio.js");

        // Plug the browser stack (Web Audio backend + browser-safe service stubs, single-view shell)
        // into the shared App.
        SharedApp.Platform = new WebPlatform();

        await BuildAvaloniaApp().StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<SharedApp>()
            .WithInterFont();
}
