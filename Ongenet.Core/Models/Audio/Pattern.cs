using System;
using System.Collections.Generic;
using System.Linq;

namespace Ongenet.Core.Models.Audio;

/// <summary>FL-style pattern definition (step/channel data lives in linked clips).</summary>
public sealed class Pattern
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Pattern";
    public double LengthBeats { get; set; } = 4;
    public int ColorIndex { get; set; }
    public List<PatternChannel> Channels { get; } = new();
    public List<StepSequence> StepSequences { get; } = new();

    /// <summary>Channels sorted by <see cref="PatternChannel.Order"/> (stable row order in the editor).</summary>
    public IEnumerable<PatternChannel> OrderedChannels =>
        Channels.OrderBy(c => c.Order).ThenBy(c => Channels.IndexOf(c));

    public StepSequence GetOrCreateSequence(PatternChannel channel, int stepCount = 16)
    {
        var seq = StepSequences.FirstOrDefault(s => s.PatternChannelId == channel.Id);
        if (seq is not null) return seq;
        seq = new StepSequence { PatternChannelId = channel.Id, StepCount = stepCount };
        for (var i = 0; i < stepCount; i++)
            seq.Steps.Add(new StepData());
        StepSequences.Add(seq);
        return seq;
    }

    public void ReorderChannel(Guid channelId, int newIndex)
    {
        var ordered = OrderedChannels.ToList();
        var current = ordered.FindIndex(c => c.Id == channelId);
        if (current < 0) return;
        newIndex = Math.Clamp(newIndex, 0, ordered.Count - 1);
        if (current == newIndex) return;
        var ch = ordered[current];
        ordered.RemoveAt(current);
        ordered.Insert(newIndex, ch);
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].Order = i;
    }
}

/// <summary>How a pattern row triggers sound during playback.</summary>
public enum PatternRowSourceKind
{
    /// <summary>MIDI routed to an existing instrument track.</summary>
    InstrumentTrack,

    /// <summary>One-shot sample via a sampler-backed instrument track.</summary>
    AudioSample
}

/// <summary>One row in the pattern editor (instrument track or sample source).</summary>
public sealed class PatternChannel
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Display order in the pattern grid (0 = top).</summary>
    public int Order { get; set; }

    public PatternRowSourceKind SourceKind { get; set; } = PatternRowSourceKind.InstrumentTrack;

    /// <summary>Target instrument track for MIDI/sample playback.</summary>
    public Guid TrackId { get; set; }

    /// <summary>When <see cref="SourceKind"/> is <see cref="PatternRowSourceKind.AudioSample"/>, the source audio clip.</summary>
    public Guid? SampleClipId { get; set; }

    public string Name { get; set; } = "Channel";
    public bool Muted { get; set; }
    public double Volume { get; set; } = 0.8;
    public double Pan { get; set; }
}

/// <summary>A pattern block placed on the playlist timeline.</summary>
public sealed class PatternClip
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PatternId { get; set; }
    public Guid TrackId { get; set; }
    public double StartBeat { get; set; }
    public double LengthBeats { get; set; } = 4;
}
