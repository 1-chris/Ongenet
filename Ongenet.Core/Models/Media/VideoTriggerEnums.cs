namespace Ongenet.Core.Models.Media;

public enum VideoTriggerSource
{
    ArrangementClip,
    SessionClip,
    MidiNote,
    MidiCc
}

public enum VideoTriggerMoment
{
    ClipStart,
    ClipEnd,
    NoteOn,
    NoteOff
}

public enum VideoTriggerAction
{
    Show,
    Hide,
    Toggle,
    FadeIn,
    FadeOut
}
