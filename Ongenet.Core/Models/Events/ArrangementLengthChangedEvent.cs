namespace Ongenet.Core.Models.Events;

/// <summary>
/// Published when the arrangement length (<see cref="Audio.Project.BarCount"/>) changes, so the
/// timeline can resize its ruler/arrange area.
/// </summary>
public sealed record ArrangementLengthChangedEvent;
