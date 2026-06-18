using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Models.Events;

/// <summary>
/// Published when a clip is added to a track outside the timeline view model (e.g. by the
/// recording service), so the timeline can create the clip's view model on the matching lane.
/// </summary>
public sealed record ClipAddedEvent(Track Track, Clip Clip);
