using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace Ongenet.App.Accessibility;

/// <summary>Helpers for screen-reader labels on core DAW controls.</summary>
public static class A11y
{
    public static void Name(Control control, string name) =>
        AutomationProperties.SetName(control, name);

    public static void Help(Control control, string help) =>
        AutomationProperties.SetHelpText(control, help);

    public static void Landmark(Control control, string name, string help)
    {
        AutomationProperties.SetName(control, name);
        AutomationProperties.SetHelpText(control, help);
        AutomationProperties.SetIsRequiredForForm(control, false);
    }
}
