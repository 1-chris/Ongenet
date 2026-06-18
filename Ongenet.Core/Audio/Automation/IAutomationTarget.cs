namespace Ongenet.Core.Audio.Automation;

/// <summary>
/// A parameter an automation lane can read and drive: a named value with a min/max range. The
/// Desktop builds one per control (a knob's FloatParameter, a track's Volume/Pan, an effect's
/// on/off, etc.). <see cref="Stepped"/> marks discrete targets (on/off, choice).
/// </summary>
public interface IAutomationTarget
{
    string Name { get; }
    double Minimum { get; }
    double Maximum { get; }
    bool Stepped { get; }

    /// <summary>The control's current value.</summary>
    double Read();

    /// <summary>Sets the control's value (clamping is the target's responsibility).</summary>
    void Write(double value);
}
