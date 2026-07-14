using System.Collections.Generic;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Modulation;

/// <summary>Track/device modulation source evaluated at schedule time (output 0..1).</summary>
public interface IModulator
{
    string Name { get; }
    string TypeId { get; }
    bool Enabled { get; set; }
    IReadOnlyList<Parameter> Parameters { get; }
    IModulator Clone();
    double Evaluate(ModulatorContext ctx);
}
