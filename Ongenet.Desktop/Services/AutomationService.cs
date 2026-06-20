using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Desktop.Services
{
    /// <summary>
    /// Default <see cref="IAutomationService"/>. Builds the lane, seeds it with the control's current
    /// value, commits the lane snapshot for the audio thread, and publishes an
    /// <see cref="AutomationChangedEvent"/> so the timeline shows the new (indented) automation row.
    /// </summary>
    public sealed class AutomationService : IAutomationService
    {
        private readonly IEventAggregator _events;
        private readonly IHistoryService _history;

        public AutomationService(IEventAggregator events, IHistoryService history)
        {
            _events = events;
            _history = history;
        }

        public void CreateLane(Track owner, IAutomationTarget target)
        {
            _history.Capture("Create automation");
            var lane = new AutomationLane(target) { Binding = DeriveBinding(owner, target) };
            // Default value = whatever the control is set to right now (flat curve).
            lane.AddPoint(new AutomationPoint(0, target.Read()));

            owner.AutoLanes.Add(lane);
            owner.AutomationCollapsed = false; // reveal the new row
            owner.CommitAutoLanes();
            _events.Publish(new AutomationChangedEvent(owner));
        }

        // Works out a serializable binding from the target's creation hints, locating instrument/effect
        // parameters by reference so duplicate parameter names (e.g. 3x Osc's three "Wave"s) stay distinct.
        public AutomationBinding? DeriveBinding(Track owner, IAutomationTarget target)
        {
            if (target is not DelegateAutomationTarget d) return null;

            switch (d.BindKind)
            {
                case AutomationTargetKind.TrackVolume: return new(AutomationTargetKind.TrackVolume, -1, -1);
                case AutomationTargetKind.TrackPan: return new(AutomationTargetKind.TrackPan, -1, -1);
                case AutomationTargetKind.EffectEnabled:
                    var fxIndex = d.BindSource is IAudioEffect fx ? owner.Effects.IndexOf(fx) : -1;
                    return fxIndex >= 0 ? new(AutomationTargetKind.EffectEnabled, fxIndex, -1) : null;
            }

            // No kind set → an instrument/effect parameter; find it by reference.
            if (d.BindSource is not Parameter param) return null;

            // Instrument-rack parameter: the slot index is carried in the binding's EffectIndex field so
            // the right instrument is found again on load (and after re-ordering the rack).
            for (var s = 0; s < owner.Instruments.Count; s++)
            {
                var pi = IndexOf(owner.Instruments[s].Instrument.Parameters, param);
                if (pi >= 0) return new(AutomationTargetKind.InstrumentParam, s, pi);
            }

            for (var i = 0; i < owner.Effects.Count; i++)
            {
                var pi = IndexOf(owner.Effects[i].Parameters, param);
                if (pi >= 0) return new(AutomationTargetKind.EffectParam, i, pi);
            }

            return null;
        }

        private static int IndexOf(System.Collections.Generic.IReadOnlyList<Parameter> list, Parameter p)
        {
            for (var i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], p)) return i;
            return -1;
        }
    }
}
