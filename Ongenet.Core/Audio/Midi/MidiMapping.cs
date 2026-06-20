using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Midi;

/// <summary>
/// Binds a MIDI Continuous Controller to an automatable target (a track/instrument/effect parameter),
/// so turning a hardware knob drives that parameter — "MIDI learn". The <see cref="Binding"/> is the
/// same serializable descriptor automation lanes use; <see cref="Target"/> is the resolved runtime
/// target, rebuilt from the binding on project load (not serialized).
/// </summary>
public sealed class MidiMapping
{
    /// <summary>The track whose instrument/effect/parameter this mapping drives.</summary>
    public required Track Owner { get; init; }

    /// <summary>MIDI channel to match (0..15), or -1 to match any channel.</summary>
    public int Channel { get; init; } = -1;

    /// <summary>Controller number (0..127).</summary>
    public required int Controller { get; init; }

    /// <summary>Serializable descriptor of the bound target (kind + effect/param indices).</summary>
    public required AutomationBinding Binding { get; init; }

    /// <summary>Resolved runtime target. Rebuilt from <see cref="Binding"/> on load; not serialized.</summary>
    public IAutomationTarget? Target { get; set; }
}
