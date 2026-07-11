using Ongenet.Core.Music;

namespace Ongenet.App.ViewModels;

/// <summary>One entry in the sample target-key picker, with a musical-fit hint for pitch-shift.</summary>
public sealed class TargetKeyOption
{
    public required int RootIndex { get; init; }
    public required bool IsMinor { get; init; }
    public required string Label { get; init; }
    public required string Hint { get; init; }
    public SampleKeyCompatibility.Fit Fit { get; init; }

    public override string ToString() => Label;
}
