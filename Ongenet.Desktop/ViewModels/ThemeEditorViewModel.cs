using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Styling;
using Ongenet.Desktop.Theming;

namespace Ongenet.Desktop.ViewModels
{
    /// <summary>
    /// Drives the theme editor window: pick a built-in flavour, flip the light/dark variant, edit any of the
    /// 26 palette tokens live (each hex edit re-themes the whole UI immediately), and import/export themes as
    /// JSON. All changes go through <see cref="IThemeService"/>, so nothing here touches resources directly.
    /// </summary>
    public sealed class ThemeEditorViewModel : ViewModelBase
    {
        private readonly IThemeService _service;
        private bool _suppress; // true while syncing rows from the service, so we don't re-apply our own values

        public ThemeEditorViewModel(IThemeService service)
        {
            _service = service;
            BuiltIns = new ObservableCollection<ThemeDefinition>(service.BuiltIns);
            Tokens = new ObservableCollection<ThemeTokenRow>();
            foreach (var name in ThemePalette.TokenNames)
                Tokens.Add(new ThemeTokenRow(name, OnTokenEdited));

            RefreshFromCurrent();
            // Pre-select the applied flavour without re-applying it (direct field, bypassing the setter).
            _selectedBuiltIn = BuiltIns.FirstOrDefault(t => t.Name == service.Current.Name);
        }

        /// <summary>The built-in Catppuccin flavours, selectable in the list.</summary>
        public ObservableCollection<ThemeDefinition> BuiltIns { get; }

        /// <summary>One editable row per palette token.</summary>
        public ObservableCollection<ThemeTokenRow> Tokens { get; }

        private ThemeDefinition? _selectedBuiltIn;
        public ThemeDefinition? SelectedBuiltIn
        {
            get => _selectedBuiltIn;
            set
            {
                if (!SetField(ref _selectedBuiltIn, value) || value is null || _suppress) return;
                _service.Apply(value);
                RefreshFromCurrent();
            }
        }

        private bool _isLight;
        /// <summary>Light/dark variant toggle — flips Fluent's built-in light/dark resources live.</summary>
        public bool IsLight
        {
            get => _isLight;
            set
            {
                if (!SetField(ref _isLight, value) || _suppress) return;
                var cur = _service.Current;
                _service.Apply(new ThemeDefinition(cur.Name,
                    value ? ThemeVariant.Light : ThemeVariant.Dark, cur.Tokens));
            }
        }

        /// <summary>Name of the theme currently applied (shown in the header).</summary>
        public string CurrentName => _service.Current.Name;

        private void OnTokenEdited(string token, Color color)
        {
            if (_suppress) return;
            _service.SetToken(token, color);
            OnPropertyChanged(nameof(CurrentName));
        }

        /// <summary>Re-syncs the rows + variant toggle from the applied theme (after select/import).</summary>
        public void RefreshFromCurrent()
        {
            _suppress = true;
            var cur = _service.Current;
            foreach (var row in Tokens)
                if (cur.Tokens.TryGetValue(row.Token, out var c))
                    row.SetColor(c);
            _isLight = cur.Variant == ThemeVariant.Light;
            OnPropertyChanged(nameof(IsLight));
            OnPropertyChanged(nameof(CurrentName));
            _suppress = false;
        }

        /// <summary>JSON for the currently-applied theme (used by the Export button).</summary>
        public string ExportCurrentJson() => _service.ExportJson(_service.Current);

        /// <summary>Imports + applies a theme from JSON, then refreshes the editor.</summary>
        public void ApplyJson(string json)
        {
            _service.Apply(_service.ImportJson(json));
            RefreshFromCurrent();
        }
    }

    /// <summary>One palette token in the editor: a name, an editable hex string, and a live preview swatch.</summary>
    public sealed class ThemeTokenRow : ViewModelBase
    {
        private readonly Action<string, Color> _onEdited;
        private bool _suppress;

        public ThemeTokenRow(string token, Action<string, Color> onEdited)
        {
            Token = token;
            _onEdited = onEdited;
            _swatch = new SolidColorBrush(Colors.Magenta);
        }

        public string Token { get; }

        private string _hex = "#000000";
        public string Hex
        {
            get => _hex;
            set
            {
                if (!SetField(ref _hex, value) || _suppress) return;
                if (!TryParseHex(value, out var color)) return;
                Swatch = new SolidColorBrush(color);
                _onEdited(Token, color);
            }
        }

        private IBrush _swatch;
        public IBrush Swatch
        {
            get => _swatch;
            private set => SetField(ref _swatch, value);
        }

        /// <summary>Sets the row's colour without re-applying it (used when syncing from the service).</summary>
        public void SetColor(Color c)
        {
            _suppress = true;
            Hex = $"#{c.R:x2}{c.G:x2}{c.B:x2}";
            _suppress = false;
            Swatch = new SolidColorBrush(c);
        }

        private static bool TryParseHex(string? hex, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            try { color = Color.Parse(hex); return true; }
            catch (FormatException) { return false; }
        }
    }
}
