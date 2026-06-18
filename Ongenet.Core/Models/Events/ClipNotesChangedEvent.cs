using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Models.Events;

/// <summary>
/// Published when a MIDI clip's notes change (added/moved/resized/deleted in the piano roll), so
/// the clip's miniature view in the arrange timeline can repaint. Kept separate from
/// <see cref="ClipChangedEvent"/> so note edits don't trigger the clip's position/size relayout.
/// </summary>
public sealed record ClipNotesChangedEvent(Clip Clip);
