using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Models.Events;

/// <summary>
/// Published when a track's properties are edited (e.g. from the track inspector), so other
/// views showing the same track — such as the timeline lane header — can refresh.
/// </summary>
public sealed record TrackChangedEvent(Track Track);
