namespace Ongenet.Core.Audio.Automation;

/// <summary>
/// One breakpoint on an automation curve. <see cref="Curve"/> is the tension of the segment to the
/// NEXT point: 0 = linear, &gt;0 bends toward the later value sooner (ease-out), &lt;0 later (ease-in),
/// in the range −1..1.
/// </summary>
public sealed class AutomationPoint
{
    public double Beat { get; set; }
    public double Value { get; set; }
    public double Curve { get; set; }

    public AutomationPoint() { }

    public AutomationPoint(double beat, double value, double curve = 0)
    {
        Beat = beat;
        Value = value;
        Curve = curve;
    }
}
