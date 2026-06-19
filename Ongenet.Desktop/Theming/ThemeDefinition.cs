using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Styling;

namespace Ongenet.Desktop.Theming
{
    /// <summary>
    /// A complete theme: a display name, a light/dark variant, and a colour for each of the 26 palette tokens
    /// (keyed by token name without the "Catppuccin" prefix, e.g. "Mauve", "Base"). Built-in flavours and
    /// imported JSON themes are both represented this way.
    /// </summary>
    public sealed class ThemeDefinition
    {
        public ThemeDefinition(string name, ThemeVariant variant, IReadOnlyDictionary<string, Color> tokens)
        {
            Name = name;
            Variant = variant;
            Tokens = tokens;
        }

        public string Name { get; }
        public ThemeVariant Variant { get; }
        public IReadOnlyDictionary<string, Color> Tokens { get; }
    }
}
