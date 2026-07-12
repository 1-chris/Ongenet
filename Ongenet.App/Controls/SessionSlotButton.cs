using Avalonia.Controls;
using Avalonia.Input;
using Ongenet.App.ViewModels.Panels;

namespace Ongenet.App.Controls;

/// <summary>Session clip slot button — supports gate mode (play while held).</summary>
public sealed class SessionSlotButton : Button
{
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (DataContext is not SessionSlotViewModel slot) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            slot.SelectForInspector();
            e.Handled = true;
            return;
        }

        if (slot.IsEmpty)
            slot.AssignFromSelection();
        else if (slot.IsGateMode)
            slot.PressGate();
        else
            slot.LaunchImmediate();

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (DataContext is SessionSlotViewModel slot)
            slot.ReleaseGate();
    }
}
