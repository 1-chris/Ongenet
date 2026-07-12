using Avalonia.Controls;
using Avalonia.Input;
using Ongenet.App.ViewModels.Panels;

namespace Ongenet.App.Controls;

/// <summary>Session clip slot button — supports gate mode (play while held).</summary>
public sealed class SessionSlotButton : Button
{
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (DataContext is not SessionSlotViewModel slot) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (slot.IsEmpty)
            slot.AssignFromSelection();
        else if (slot.IsGateMode)
            slot.PressGate();
        else
            slot.LaunchImmediate();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (DataContext is SessionSlotViewModel slot)
            slot.ReleaseGate();
    }
}
