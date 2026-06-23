// NOTE: the Android framework lives in the top-level `Android.*` namespaces. These usings MUST stay at
// compilation-unit scope (above the file-scoped `namespace Ongenet.Android;`): if they were inside the
// namespace body, `Android` would bind to `Ongenet.Android` and every `Android.*` reference would break.
using System;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media;

namespace Ongenet.Android;

// The shared Application type lives in the Ongenet.App namespace; alias it so the bare name `App` doesn't
// bind to the sibling `Ongenet.App` namespace instead of the type (same trick as the desktop/web heads).
using SharedApp = Ongenet.App.App;

/// <summary>
/// The Android <see cref="global::Android.App.Application"/> that bootstraps Avalonia. Under Avalonia 12
/// this is where the app is initialised (replacing the old generic <c>AvaloniaMainActivity&lt;App&gt;</c>):
/// it builds the shared <see cref="SharedApp"/>, plugs in the Android platform stack (AAudio backend +
/// Android-safe service stubs) through <see cref="AndroidPlatform"/>, and configures fonts. Avalonia then
/// runs under its single-view lifetime, hosted by <see cref="MainActivity"/>.
///
/// <para>The <c>[Application]</c> attribute registers this as the process's Application class so every
/// activity shares the one initialised Avalonia instance.</para>
/// </summary>
[Application]
public sealed class AndroidApp : AvaloniaAndroidApplication<SharedApp>
{
    // Required Java interop constructor (the runtime instantiates the Application via JNI).
    public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // Plug the Android stack into the shared App before Avalonia initialises it.
        SharedApp.Platform = new AndroidPlatform();

        return base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .With(new FontManagerOptions
            {
                // Emoji is a last-resort glyph fallback only, never a primary text family — keeps digits
                // and letters in Inter rather than rendering them as emoji keycap glyphs.
                FontFallbacks = new[]
                {
                    new FontFallback
                    {
                        FontFamily = "avares://Ongenet.App/Assets/Fonts/NotoColorEmoji.ttf#Noto Color Emoji"
                    }
                }
            });
    }
}
