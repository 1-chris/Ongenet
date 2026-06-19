using Avalonia;
using Avalonia.Controls;
using Ongenet.Desktop.Theming;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// Base for custom-drawn controls that paint with theme colours. It rebuilds the control's cached
    /// pens/brushes from <see cref="ThemePalette"/> when the control attaches and whenever the theme changes,
    /// then repaints — so a live theme switch recolours these controls without any per-control plumbing.
    /// </summary>
    public abstract class ThemedControl : Control
    {
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            BuildThemeResources();
            ThemePalette.Changed += OnThemeChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            ThemePalette.Changed -= OnThemeChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnThemeChanged()
        {
            BuildThemeResources();
            InvalidateVisual();
        }

        /// <summary>Rebuild cached pens/brushes from <see cref="ThemePalette"/>. Called on attach + theme change.</summary>
        protected virtual void BuildThemeResources() { }
    }
}
