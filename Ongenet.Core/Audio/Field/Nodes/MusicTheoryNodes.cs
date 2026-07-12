using System;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field.Nodes;

/// <summary>Quantises an incoming pitch (Hz) to the nearest note in a diatonic scale.</summary>
public sealed class ScaleQuantizeNode : FieldNode
{
    public const string Type = "music.scale_quantize";
    public override string TypeId => Type;
    public override string DisplayName => "Scale Quantize";
    public override string Category => FieldNodeCategories.Music;

    public int Root { get; set; }
    public int ScaleIndex { get; set; }

    public ScaleQuantizeNode()
    {
        AddInput("pitch", "Pitch", FieldSignalKind.Note);
        AddOutput("out", "Out", FieldSignalKind.Note);
        AddParam(new ChoiceParameter("Root", new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" },
            () => Root, i => Root = i), modulatable: false);
        AddParam(new ChoiceParameter("Scale", new[] { "Major", "Minor", "Maj Pent", "Min Pent" },
            () => ScaleIndex, i => ScaleIndex = i), modulatable: false);
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var mask = ScaleMasks[Math.Clamp(ScaleIndex, 0, ScaleMasks.Length - 1)];
        var root = Math.Clamp(Root, 0, 11);
        for (var i = 0; i < ctx.Frames; i++)
        {
            var hz = input[i];
            if (hz <= 0f) { outBuf[i] = 0f; continue; }
            var midi = 69.0 + 12.0 * Math.Log(hz / 440.0, 2.0);
            outBuf[i] = (float)MusicalMath.NoteToFrequency(QuantizeMidi(midi, root, mask));
        }
    }

    private static double QuantizeMidi(double midi, int root, ReadOnlySpan<int> mask)
    {
        var note = (int)Math.Round(midi);
        var degree = ((note - root) % 12 + 12) % 12;
        var best = degree;
        var bestDist = 12;
        for (var d = 0; d < 12; d++)
        {
            if (mask[d] == 0) continue;
            var dist = Math.Abs(degree - d);
            dist = Math.Min(dist, 12 - dist);
            if (dist < bestDist) { bestDist = dist; best = d; }
        }

        var snapped = note - degree + best;
        if (Math.Abs(snapped - midi) > 6) snapped += snapped > midi ? -12 : 12;
        return snapped;
    }

    private static readonly int[][] ScaleMasks =
    {
        new[] { 1, 0, 1, 0, 1, 1, 0, 1, 0, 1, 0, 1 }, // major
        new[] { 1, 0, 1, 1, 0, 1, 0, 1, 1, 0, 1, 0 }, // natural minor
        new[] { 1, 0, 1, 0, 1, 0, 0, 1, 0, 1, 0, 0 }, // major pentatonic
        new[] { 1, 0, 0, 1, 0, 1, 0, 1, 0, 0, 1, 0 }  // minor pentatonic
    };
}

/// <summary>Emits a constant root pitch (Hz) for tuning oscillators or quantizers.</summary>
public sealed class KeyRootNode : FieldNode
{
    public const string Type = "music.key_root";
    public override string TypeId => Type;
    public override string DisplayName => "Key Root";
    public override string Category => FieldNodeCategories.Music;

    public int Root { get; set; }
    public int Octave { get; set; } = 3;

    public KeyRootNode()
    {
        AddOutput("pitch", "Pitch", FieldSignalKind.Note);
        AddParam(new ChoiceParameter("Root", new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" },
            () => Root, i => Root = i), modulatable: false);
        AddParam(new FloatParameter("Octave", 0, 8, () => Octave, v => Octave = (int)Math.Round(v), "0"),
            modulatable: false);
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var midi = Math.Clamp(Octave, 0, 8) * 12 + Math.Clamp(Root, 0, 11) + 12;
        var hz = (float)MusicalMath.NoteToFrequency(midi);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = hz;
    }
}
