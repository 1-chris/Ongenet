using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace Ongenet.Desktop.Theming
{
    /// <summary>
    /// Applies themes live by recolouring the shared palette brushes + switching the app's light/dark variant.
    /// No restart and (for now) no persistence.
    /// </summary>
    public interface IThemeService
    {
        /// <summary>The theme currently applied (reflects any per-token edits).</summary>
        ThemeDefinition Current { get; }

        /// <summary>The built-in Catppuccin flavours.</summary>
        IReadOnlyList<ThemeDefinition> BuiltIns { get; }

        /// <summary>Raised after a theme (or a single token) is applied, so UI can refresh.</summary>
        event Action? ThemeChanged;

        /// <summary>Captures the shared brush instances and applies the default theme. Call once at startup.</summary>
        void Initialize();

        /// <summary>Applies a full theme live.</summary>
        void Apply(ThemeDefinition theme);

        /// <summary>Recolours a single token live (used by the per-element colour editor).</summary>
        void SetToken(string token, Color color);

        /// <summary>Serialises a theme to JSON (name, variant, 26 colour codes).</summary>
        string ExportJson(ThemeDefinition theme);

        /// <summary>Parses a theme from JSON; tolerates missing tokens and infers the variant if absent.</summary>
        ThemeDefinition ImportJson(string json);
    }
}
