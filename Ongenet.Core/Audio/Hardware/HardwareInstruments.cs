using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Hardware;

/// <summary>External hardware instrument: forwards note events when supported; silent otherwise.</summary>
public sealed class HwInstrument : PolyphonicInstrument
{
    public const string TypeId = "hw_instrument";

    protected override string GetTypeId() => TypeId;

    public override string Name => "HW Instrument";

    public int OutputChannel { get; set; } = 1;

    private IReadOnlyList<Parameter>? _parameters;

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("MIDI Channel", 1, 16, () => OutputChannel, v => OutputChannel = (int)v, "0")
    };

    public override IInstrument Clone() => new HwInstrument { OutputChannel = OutputChannel };

    protected override Voice CreateVoice() => new SilentVoice();
}

/// <summary>CV-driven external instrument: silent local output; CV note routing when supported.</summary>
public sealed class HwCvInstrument : PolyphonicInstrument
{
    public const string TypeId = "hw_cv_instrument";

    protected override string GetTypeId() => TypeId;

    public override string Name => "HW CV Instrument";

    public int CvOutput { get; set; }
    public double PitchRange { get; set; } = 5.0;

    private IReadOnlyList<Parameter>? _parameters;

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("CV Out", 0, 7, () => CvOutput, v => CvOutput = (int)v, "0"),
        new FloatParameter("Range", 1, 10, () => PitchRange, v => PitchRange = v, "0.#", "V/oct")
    };

    public override IInstrument Clone() => new HwCvInstrument
    {
        CvOutput = CvOutput, PitchRange = PitchRange
    };

    protected override Voice CreateVoice() => new SilentVoice();
}

internal sealed class SilentVoice : Voice
{
    public override void Release() => IsActive = false;
    public override void Render(Span<float> buffer) { }
}
