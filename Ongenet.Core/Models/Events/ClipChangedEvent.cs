using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Models.Events;

/// <summary>
/// Published when a clip's properties are edited (e.g. from the clip inspector), so the
/// timeline lane can re-lay-out the clip.
/// </summary>
public sealed record ClipChangedEvent(Clip Clip);
