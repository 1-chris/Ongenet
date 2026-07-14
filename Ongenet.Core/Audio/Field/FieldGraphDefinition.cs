using System;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// Library metadata for a user-authored Field instrument or effect. The graph and surface live alongside
/// this in a <c>.ongenfielddef</c> package; project instances embed a snapshot so edits to the library
/// definition do not affect existing songs.
/// </summary>
public sealed class FieldGraphDefinition
{
    public const string InstrumentTypePrefix = "field.user.inst.";
    public const string EffectTypePrefix = "field.user.fx.";
    public const string UserInstrumentCategory = "User Instruments";
    public const string UserEffectCategory = "User Effects";

    public Guid DefinitionId { get; set; } = Guid.NewGuid();
    public FieldGraphRole Role { get; set; }
    public string DisplayName { get; set; } = "Untitled";
    public string Category { get; set; } = "";
    public string Author { get; set; } = "";
    public long CreatedTicks { get; set; } = DateTime.UtcNow.Ticks;
    public long ModifiedTicks { get; set; } = DateTime.UtcNow.Ticks;
    public FieldSurfaceDefinition Surface { get; set; } = new();

    /// <summary>Stable registry type id derived from <see cref="DefinitionId"/> and <see cref="Role"/>.</summary>
    public string TypeId => Role == FieldGraphRole.Instrument
        ? InstrumentTypePrefix + DefinitionId.ToString("N")
        : EffectTypePrefix + DefinitionId.ToString("N");

    public string LibraryCategory => string.IsNullOrWhiteSpace(Category)
        ? (Role == FieldGraphRole.Instrument ? UserInstrumentCategory : UserEffectCategory)
        : Category.Trim();

    public static bool IsUserInstrumentType(string typeId)
        => typeId.StartsWith(InstrumentTypePrefix, StringComparison.Ordinal);

    public static bool IsUserEffectType(string typeId)
        => typeId.StartsWith(EffectTypePrefix, StringComparison.Ordinal);

    public static bool IsUserFieldType(string typeId)
        => IsUserInstrumentType(typeId) || IsUserEffectType(typeId);

    public static Guid? TryParseDefinitionId(string typeId)
    {
        string? hex = null;
        if (IsUserInstrumentType(typeId)) hex = typeId[InstrumentTypePrefix.Length..];
        else if (IsUserEffectType(typeId)) hex = typeId[EffectTypePrefix.Length..];
        if (hex is null || hex.Length != 32) return null;
        return Guid.TryParseExact(hex, "N", out var id) ? id : null;
    }

    public FieldGraphDefinition CloneMetadata()
        => new()
        {
            DefinitionId = DefinitionId,
            Role = Role,
            DisplayName = DisplayName,
            Category = Category,
            Author = Author,
            CreatedTicks = CreatedTicks,
            ModifiedTicks = ModifiedTicks,
            Surface = Surface.Clone()
        };
}
