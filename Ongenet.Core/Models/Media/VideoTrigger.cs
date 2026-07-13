using System;

namespace Ongenet.Core.Models.Media;

/// <summary>Maps arrangement/session/MIDI events to video layer visibility.</summary>
public sealed class VideoTrigger
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TargetLayerId { get; set; }
    public VideoTriggerSource Source { get; set; } = VideoTriggerSource.ArrangementClip;
    public Guid? TrackId { get; set; }
    public Guid? ClipId { get; set; }
    public int? MidiNote { get; set; }
    public int? MidiCcChannel { get; set; }
    public int? MidiCcNumber { get; set; }
    public double MidiCcThreshold { get; set; } = 64;
    public VideoTriggerMoment Moment { get; set; } = VideoTriggerMoment.ClipStart;
    public VideoTriggerAction Action { get; set; } = VideoTriggerAction.FadeIn;
    public double FadeDurationSeconds { get; set; } = 0.5;
}
