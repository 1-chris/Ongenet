using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Ongenet.Desktop.Controls;
using Ongenet.Desktop.ViewModels;

namespace Ongenet.Desktop.Views.Panels
{
    /// <summary>Renders a list of parameter view models as sliders/combos (shared by instrument + effect editors).</summary>
    public partial class ParametersView : UserControl
    {
        public ParametersView()
        {
            InitializeComponent();
            // Float knobs handle their own right-click; here we cover the on/off (bool) checkboxes.
            AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
            if ((e.Source as StyledElement)?.DataContext is not BoolParameterViewModel bp) return;

            var anchor = e.Source as Control ?? this;
            AutomationGesture.Offer(anchor, AutomationGesture.ForBool(bp.Parameter));
            e.Handled = true;
        }
    }
}
