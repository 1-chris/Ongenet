using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Models.Events;

/// <summary>
/// Published when a track's automation lanes change (added, removed, or collapsed), so the audio
/// engine re-snapshots the lanes and the timeline rebuilds its rows.
/// </summary>
public sealed record AutomationChangedEvent(Track Track);
