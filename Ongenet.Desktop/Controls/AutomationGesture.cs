using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// Shared "Create automation track" right-click behaviour for automatable controls. Builds an
    /// <see cref="IAutomationTarget"/> for the control, resolves the owning track from the current
    /// selection, and pops a one-item flyout that hands the target to the <see cref="IAutomationService"/>.
    /// </summary>
    public static class AutomationGesture
    {
        /// <summary>Pops the "Create automation track" flyout at <paramref name="anchor"/> for the given target.</summary>
        public static void Offer(Control anchor, IAutomationTarget target)
        {
            var sp = App.ServiceProvider;
            var automation = sp?.GetService<Services.IAutomationService>();
            var owner = sp?.GetService<ISelectionService>()?.SelectedTrack;
            if (automation is null || owner is null) return;

            var item = new MenuItem { Header = "Create automation track" };
            item.Click += (_, _) => automation.CreateLane(owner, target);

            var flyout = new MenuFlyout();
            flyout.Items.Add(item);
            flyout.ShowAt(anchor);
        }

        /// <summary>Pops the flyout for a target whose owner is given explicitly (e.g. the track inspector).</summary>
        public static void Offer(Control anchor, Track? owner, IAutomationTarget target)
        {
            var automation = App.ServiceProvider?.GetService<Services.IAutomationService>();
            if (automation is null || owner is null) return;

            var item = new MenuItem { Header = "Create automation track" };
            item.Click += (_, _) => automation.CreateLane(owner, target);

            var flyout = new MenuFlyout();
            flyout.Items.Add(item);
            flyout.ShowAt(anchor);
        }

        // --- target builders for the common control kinds ---

        public static IAutomationTarget ForFloat(FloatParameter p)
            => new DelegateAutomationTarget(p.Name, p.Min, p.Max, () => p.Value, v => p.Value = v)
            { BindSource = p }; // kind resolved (instrument vs effect param) when the lane is created

        public static IAutomationTarget ForBool(BoolParameter p)
            => new DelegateAutomationTarget(p.Name, 0, 1, () => p.Value ? 1 : 0, v => p.Value = v >= 0.5, stepped: true)
            { BindSource = p };

        public static IAutomationTarget ForVolume(Track t)
            => new DelegateAutomationTarget("Volume", 0, 1, () => t.Volume, v => t.Volume = v)
            { BindKind = AutomationTargetKind.TrackVolume, BindSource = t };

        public static IAutomationTarget ForPan(Track t)
            => new DelegateAutomationTarget("Pan", -1, 1, () => t.Pan, v => t.Pan = v)
            { BindKind = AutomationTargetKind.TrackPan, BindSource = t };

        public static IAutomationTarget ForEffectEnabled(IAudioEffect fx)
            => new DelegateAutomationTarget($"{fx.Name} On/Off", 0, 1,
                () => fx.Enabled ? 1 : 0, v => fx.Enabled = v >= 0.5, stepped: true)
            { BindKind = AutomationTargetKind.EffectEnabled, BindSource = fx };
    }
}
