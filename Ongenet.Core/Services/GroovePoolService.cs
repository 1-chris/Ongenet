using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Services;

/// <summary>Import/export user groove templates as <c>.ongenet-groove</c> JSON files.</summary>
public static class GroovePoolService
{
    public static GrooveTemplate ToTemplate(GrooveFile file)
    {
        var t = new GrooveTemplate
        {
            Name = file.Name,
            SwingAmount = file.Swing,
            Division = 16
        };
        EnsureOffsetSlots(t, 16);
        foreach (var o in file.Offsets)
        {
            var idx = o.StepIndex % t.Division;
            if (idx >= 0 && idx < t.StepOffsets.Count)
                t.StepOffsets[idx] = o.OffsetBeats;
        }
        return t;
    }

    public static GrooveFile FromTemplate(GrooveTemplate template)
    {
        var f = new GrooveFile { Name = template.Name, Swing = template.SwingAmount };
        for (var i = 0; i < template.StepOffsets.Count; i++)
        {
            if (Math.Abs(template.StepOffsets[i]) < 1e-9) continue;
            f.Offsets.Add(new GrooveTimingOffset { StepIndex = i, OffsetBeats = template.StepOffsets[i] });
        }
        return f;
    }

    public static GrooveFile Load(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        return JsonSerializer.Deserialize<GrooveFile>(json, JsonOptions)
               ?? throw new InvalidDataException("Invalid groove file.");
    }

    public static void Save(GrooveFile file, string path)
    {
        var json = JsonSerializer.Serialize(file, JsonOptions);
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    /// <summary>Extract timing offsets from MIDI note onsets in a clip (16th grid).</summary>
    public static GrooveFile ExtractFromClip(Clip clip, string name = "Extracted")
    {
        var f = new GrooveFile { Name = name };
        if (clip.Notes.Count == 0) return f;

        const double grid = 0.25;
        foreach (var note in clip.Notes.OrderBy(n => n.StartBeat))
        {
            var step = (int)Math.Round(note.StartBeat / grid) % 16;
            var ideal = step * grid;
            f.Offsets.Add(new GrooveTimingOffset { StepIndex = step, OffsetBeats = note.StartBeat - ideal });
        }

        return f;
    }

    private static void EnsureOffsetSlots(GrooveTemplate t, int count)
    {
        while (t.StepOffsets.Count < count) t.StepOffsets.Add(0);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
