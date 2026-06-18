using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Desktop.Controls;
using Ongenet.Desktop.ViewModels.Effects;

namespace Ongenet.Desktop.Views.Panels
{
    /// <summary>Shared header for an effect card: enable dot, chain-position badge, name, and move/remove buttons.</summary>
    public partial class EffectHeaderView : UserControl
    {
        public EffectHeaderView()
        {
            InitializeComponent();
            AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        }

        // Right-click the bypass dot → "Create automation track" for the effect's on/off state.
        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
            if (DataContext is not EffectViewModel fx) return;

            var button = (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true);
            if (button?.Name != "BypassDot") return;

            var owner = App.ServiceProvider?.GetService<ISelectionService>()?.SelectedTrack;
            if (owner is null) return;

            AutomationGesture.Offer(button, owner, AutomationGesture.ForEffectEnabled(fx.Effect));
            e.Handled = true;
        }
    }
}
