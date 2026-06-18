namespace Ongenet.Core.Models.Events;

/// <summary>
/// Published when the project's set of tracks changes (a track added or removed), so the audio
/// engine can rebuild its track snapshot.
/// </summary>
public sealed record TracksChangedEvent;
