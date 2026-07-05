using Ongenet.Core.Audio.Field.Nodes;

namespace Ongenet.Core.Audio.Field.Patches;

/// <summary>
/// Factory for the built-in Field starter graphs. The beginner instrument patch is a sine oscillator shaped
/// by an ADSR into a scope and the output — the "hello world" for learning the grid. The full-decomposition
/// patches that reproduce every built-in instrument/effect live in <see cref="FieldBuiltInPatches"/>.
/// </summary>
public static class FieldPatches
{
    /// <summary>Builds the beginner instrument: Note → Sine Osc → (× ADSR) → Scope → Out.</summary>
    public static void BuildBeginnerInstrument(FieldGraph graph)
    {
        graph.Clear();

        var note = new NoteInNode { X = 40, Y = 160 };
        var osc = new WaveOscNode { X = 240, Y = 120, WaveIndex = 0, Level = 0.6 };
        var adsr = new AdsrNode { X = 240, Y = 300, Attack = 0.005, Decay = 0.1, Sustain = 0.7, Release = 0.3 };
        var vca = new GainNode { X = 460, Y = 160, Amount = 0.6 };
        var scope = new ScopeNode { X = 660, Y = 160 };
        var outNode = new AudioOutNode { X = 860, Y = 160 };

        graph.AddNode(note);
        graph.AddNode(osc);
        graph.AddNode(adsr);
        graph.AddNode(vca);
        graph.AddNode(scope);
        graph.AddNode(outNode);

        graph.Connect(note.Id, "pitch", osc.Id, "pitch");
        graph.Connect(note.Id, "gate", adsr.Id, "gate");
        graph.Connect(osc.Id, "out", vca.Id, "in");
        graph.Connect(adsr.Id, "out", vca.Id, "cv");
        graph.Connect(vca.Id, "out", scope.Id, "in");
        graph.Connect(scope.Id, "thru", outNode.Id, "l");
        graph.Connect(scope.Id, "thru", outNode.Id, "r");
    }

    /// <summary>Builds the beginner effect: Audio In → Out (a transparent pass-through to start editing from).</summary>
    public static void BuildBeginnerEffect(FieldGraph graph)
    {
        graph.Clear();

        var input = new AudioInNode { X = 60, Y = 160 };
        var scope = new ScopeNode { X = 300, Y = 160 };
        var outNode = new AudioOutNode { X = 540, Y = 160 };

        graph.AddNode(input);
        graph.AddNode(scope);
        graph.AddNode(outNode);

        // Pass-through via the scope tap (L), and dry R.
        graph.Connect(input.Id, "l", scope.Id, "in");
        graph.Connect(scope.Id, "thru", outNode.Id, "l");
        graph.Connect(input.Id, "r", outNode.Id, "r");
    }
}
