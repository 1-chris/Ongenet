namespace Ongenet.Core.Audio.Parameters;

/// <summary>
/// Base of the shared parameter framework used by instruments and effects to expose their
/// editable settings generically. A parameter wraps the owner's live value via delegates, so
/// there's a single source of truth and the UI can render any parameter list uniformly.
/// </summary>
public abstract class Parameter
{
    protected Parameter(string name) => Name = name;

    /// <summary>Display name.</summary>
    public string Name { get; }

    /// <summary>
    /// Optional UI grouping label (e.g. "Grain", "Amp Envelope"). The instrument inspector lays out
    /// parameters that share a group inside one titled fieldset, in first-seen order. Null/empty means
    /// ungrouped. Purely cosmetic — flat consumers (automation, plugins) ignore it.
    /// </summary>
    public string? Group { get; init; }
}
