// NOTE: the Android framework lives in the top-level `Android.*` namespaces. These usings MUST stay at
// compilation-unit scope (above the file-scoped `namespace Ongenet.Android;`): if they were inside the
// namespace body, `Android` would bind to `Ongenet.Android` and every `Android.*` reference would break.
using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace Ongenet.Android;

/// <summary>
/// The single launcher activity. Under Avalonia 12 the activity is a thin, non-generic
/// <see cref="AvaloniaMainActivity"/>: all app initialisation (which <c>App</c> to run, fonts, the
/// platform stack) lives in <see cref="AndroidApp"/> instead. This class only carries the Android
/// activity metadata.
///
/// <para>Tablet-first: a sensor-driven landscape orientation, no action bar, and it handles
/// orientation/size config changes itself so a rotation doesn't tear down and recreate the activity.</para>
/// </summary>
[Activity(
    Label = "Ongenet",
    Theme = "@android:style/Theme.Material.NoActionBar",
    MainLauncher = true,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : AvaloniaMainActivity
{
}
