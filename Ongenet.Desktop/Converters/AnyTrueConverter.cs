using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Ongenet.Desktop.Converters
{
    /// <summary>
    /// Multi-value converter that returns true when any bound value is <c>true</c> — used to show a control
    /// while any of several tabs is selected (e.g. the library options panel on Everything/Files/Samples).
    /// </summary>
    public sealed class AnyTrueConverter : IMultiValueConverter
    {
        /// <summary>Shared instance for use as a static resource.</summary>
        public static readonly AnyTrueConverter Instance = new();

        public object Convert(IList<object?> values, System.Type targetType, object? parameter, CultureInfo culture)
        {
            foreach (var v in values)
                if (v is true) return true;
            return false;
        }
    }
}
