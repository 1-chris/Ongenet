using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Desktop.Services
{
    /// <summary>
    /// Creates parameter-automation lanes. Invoked from the "Create automation track" right-click on
    /// any automatable control (knob, slider, on/off switch).
    /// </summary>
    public interface IAutomationService
    {
        /// <summary>
        /// Adds a new automation lane for <paramref name="target"/> to <paramref name="owner"/>, seeded
        /// with one point at the control's current value (a flat curve), and announces the change.
        /// </summary>
        void CreateLane(Track owner, IAutomationTarget target);

        /// <summary>
        /// Works out a serializable <see cref="AutomationBinding"/> for <paramref name="target"/> on
        /// <paramref name="owner"/> (locating instrument/effect parameters by reference), or null if it
        /// can't be bound. Shared by automation lanes and MIDI-controller mappings.
        /// </summary>
        AutomationBinding? DeriveBinding(Track owner, IAutomationTarget target);
    }
}
