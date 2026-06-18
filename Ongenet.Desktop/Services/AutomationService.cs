using Ongenet.Core.Audio.Automation;
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

        public AutomationService(IEventAggregator events) => _events = events;

        public void CreateLane(Track owner, IAutomationTarget target)
        {
            var lane = new AutomationLane(target);
            // Default value = whatever the control is set to right now (flat curve).
            lane.AddPoint(new AutomationPoint(0, target.Read()));

            owner.AutoLanes.Add(lane);
            owner.AutomationCollapsed = false; // reveal the new row
            owner.CommitAutoLanes();
            _events.Publish(new AutomationChangedEvent(owner));
        }
    }
}
