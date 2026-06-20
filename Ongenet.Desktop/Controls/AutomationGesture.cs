using System;
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
        /// <summary>
        /// Pops the control's right-click flyout at <paramref name="anchor"/>. When <paramref name="reset"/> is
        /// given, a "Reset to default" item is shown first; "Create automation track" follows when a target
        /// track is selected.
        /// </summary>
        public static void Offer(Control anchor, IAutomationTarget target, Action? reset = null)
        {
            var sp = App.ServiceProvider;
            var automation = sp?.GetService<Services.IAutomationService>();
            var owner = sp?.GetService<ISelectionService>()?.SelectedTrack;
            ShowMenu(anchor, target, reset, automation, owner);
        }

        /// <summary>As above, but with the owning track given explicitly (e.g. the track inspector).</summary>
        public static void Offer(Control anchor, Track? owner, IAutomationTarget target, Action? reset = null)
        {
            var automation = App.ServiceProvider?.GetService<Services.IAutomationService>();
            ShowMenu(anchor, target, reset, automation, owner);
        }

        private static void ShowMenu(Control anchor, IAutomationTarget target, Action? reset,
            Services.IAutomationService? automation, Track? owner)
        {
            var flyout = new MenuFlyout();

            if (reset is not null)
            {
                var resetItem = new MenuItem { Header = "Reset to default" };
                resetItem.Click += (_, _) => reset();
                flyout.Items.Add(resetItem);
            }

            if (automation is not null && owner is not null)
            {
                var item = new MenuItem { Header = "Create automation track" };
                item.Click += (_, _) => automation.CreateLane(owner, target);
                flyout.Items.Add(item);

                AddMidiLearnItems(flyout, automation, owner, target);
            }

            if (flyout.Items.Count == 0) return;
            flyout.ShowAt(anchor);
        }

        // Adds "MIDI Learn" (or "Remove MIDI mapping" when one already exists) for the clicked parameter.
        private static void AddMidiLearnItems(MenuFlyout flyout, Services.IAutomationService automation,
            Track owner, IAutomationTarget target)
        {
            var midi = App.ServiceProvider?.GetService<IMidiMappingService>();
            if (midi is null) return;

            var binding = automation.DeriveBinding(owner, target);
            if (binding is null) return;

            var existing = midi.FindMapping(owner, binding);
            if (existing is null)
            {
                var learn = new MenuItem { Header = "MIDI learn" };
                learn.Click += (_, _) => midi.BeginLearn(owner, target);
                flyout.Items.Add(learn);
            }
            else
            {
                var remove = new MenuItem { Header = $"Remove MIDI mapping (CC {existing.Controller})" };
                remove.Click += (_, _) => midi.Remove(existing);
                flyout.Items.Add(remove);
            }
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
