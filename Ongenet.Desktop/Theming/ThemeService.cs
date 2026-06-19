using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Ongenet.Desktop.Theming
{
    /// <summary>Default <see cref="IThemeService"/>. See the interface for the contract.</summary>
    public sealed class ThemeService : IThemeService
    {
        private string _name = BuiltInThemes.DefaultName;
        private ThemeVariant _variant = ThemeVariant.Dark;
        private readonly Dictionary<string, Color> _tokens = new();

        public ThemeDefinition Current => new(_name, _variant, new Dictionary<string, Color>(_tokens));
        public IReadOnlyList<ThemeDefinition> BuiltIns => BuiltInThemes.All;
        public event Action? ThemeChanged;

        public void Initialize()
        {
            var app = Application.Current;
            if (app is null) return;

            // Grab the shared brush instances created in App.axaml so mutating them re-themes the whole UI.
            foreach (var token in ThemePalette.TokenNames)
            {
                if (app.TryGetResource("Catppuccin" + token, null, out var res) && res is SolidColorBrush brush)
                    ThemePalette.Register(token, brush);
            }

            Apply(BuiltInThemes.Default);
        }

        public void Apply(ThemeDefinition theme)
        {
            var app = Application.Current;
            if (app is null) return;

            foreach (var token in ThemePalette.TokenNames)
            {
                if (!theme.Tokens.TryGetValue(token, out var color)) continue;
                _tokens[token] = color;
                if (app.TryGetResource("Catppuccin" + token, null, out var res) && res is SolidColorBrush brush)
                    brush.Color = color; // mutate in place → all {StaticResource} references update live
                app.Resources["Catppuccin" + token + "Color"] = color;
            }

            _name = theme.Name;
            _variant = theme.Variant;
            app.RequestedThemeVariant = theme.Variant;

            ThemePalette.RaiseChanged();
            ThemeChanged?.Invoke();
            InvalidateAllWindows();
        }

        public void SetToken(string token, Color color)
        {
            var app = Application.Current;
            if (app is null) return;

            _tokens[token] = color;
            if (app.TryGetResource("Catppuccin" + token, null, out var res) && res is SolidColorBrush brush)
                brush.Color = color;
            app.Resources["Catppuccin" + token + "Color"] = color;

            ThemePalette.RaiseChanged();
            ThemeChanged?.Invoke();
            InvalidateAllWindows();
        }

        private static void InvalidateAllWindows()
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
            foreach (var window in desktop.Windows)
            {
                window.InvalidateVisual();
                foreach (var visual in window.GetVisualDescendants())
                    visual.InvalidateVisual();
            }
        }

        // ---- JSON ----

        private sealed class ThemeDto
        {
            [JsonPropertyName("name")] public string? Name { get; set; }
            [JsonPropertyName("variant")] public string? Variant { get; set; }
            [JsonPropertyName("colors")] public Dictionary<string, string>? Colors { get; set; }
        }

        public string ExportJson(ThemeDefinition theme)
        {
            var colors = new Dictionary<string, string>();
            foreach (var token in ThemePalette.TokenNames)
                if (theme.Tokens.TryGetValue(token, out var c))
                    colors[token] = ToHex(c);

            var dto = new ThemeDto
            {
                Name = theme.Name,
                Variant = theme.Variant == ThemeVariant.Light ? "Light" : "Dark",
                Colors = colors
            };

            return JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
        }

        public ThemeDefinition ImportJson(string json)
        {
            var dto = JsonSerializer.Deserialize<ThemeDto>(json) ?? new ThemeDto();

            var tokens = new Dictionary<string, Color>();
            if (dto.Colors is not null)
            {
                foreach (var token in ThemePalette.TokenNames)
                    if (dto.Colors.TryGetValue(token, out var hex) && TryParse(hex, out var color))
                        tokens[token] = color;
            }

            // Variant: use the declared one, else infer from the Base colour's luminance.
            ThemeVariant variant;
            if (string.Equals(dto.Variant, "Light", StringComparison.OrdinalIgnoreCase)) variant = ThemeVariant.Light;
            else if (string.Equals(dto.Variant, "Dark", StringComparison.OrdinalIgnoreCase)) variant = ThemeVariant.Dark;
            else variant = tokens.TryGetValue("Base", out var bse) && IsLight(bse) ? ThemeVariant.Light : ThemeVariant.Dark;

            return new ThemeDefinition(string.IsNullOrWhiteSpace(dto.Name) ? "Imported" : dto.Name!, variant, tokens);
        }

        private static string ToHex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

        private static bool TryParse(string? hex, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            try { color = Color.Parse(hex); return true; }
            catch (FormatException) { return false; }
        }

        /// <summary>Relative luminance test — a light background means a light theme.</summary>
        public static bool IsLight(Color c)
        {
            var luminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
            return luminance > 0.5;
        }
    }
}
