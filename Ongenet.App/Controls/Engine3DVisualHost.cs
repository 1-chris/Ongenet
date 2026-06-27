using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.Controls.Engine3D;
using Ongenet.App.Theming;
using Ongenet.App.Views.Windows;
using Ongenet.Engine3D.Abstractions;

namespace Ongenet.App.Controls
{
    /// <summary>
    /// A reusable container for a GPU 3D visualization: it hosts an <see cref="Engine3DView"/>, drives a
    /// supplied <see cref="IEngine3DVisualization"/> (build/update/theme), and offers a generic
    /// "Open visual in dedicated window" button that pops the same visualization out into a freely
    /// resizable window. Drop one of these anywhere (an effect card, an inspector) and give it a
    /// <see cref="VisualizationFactory"/>; it handles the rest, including live theme updates.
    /// </summary>
    public class Engine3DVisualHost : Grid
    {
        public static readonly StyledProperty<Func<IEngine3DVisualization>?> VisualizationFactoryProperty =
            AvaloniaProperty.Register<Engine3DVisualHost, Func<IEngine3DVisualization>?>(nameof(VisualizationFactory));

        public static readonly StyledProperty<string?> TitleProperty =
            AvaloniaProperty.Register<Engine3DVisualHost, string?>(nameof(Title), "Visualizer");

        public static readonly StyledProperty<bool> ShowPopOutProperty =
            AvaloniaProperty.Register<Engine3DVisualHost, bool>(nameof(ShowPopOut), true);

        private readonly Engine3DView _view = new();
        private readonly Button _popOut;
        private IEngine3DVisualization? _viz;
        private bool _built;

        public Engine3DVisualHost()
        {
            ClipToBounds = true;
            Children.Add(_view);

            _popOut = new Button
            {
                Content = "⤢ Open in window",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8),
                Padding = new Thickness(8, 3),
                FontSize = 11,
                Foreground = ThemePalette.BrushOf("Text"),
                Background = ThemePalette.BrushOf("Surface1")
            };
            ToolTip.SetTip(_popOut, "Open this visual in a resizable window");
            _popOut.Click += (_, _) => OpenInWindow();
            Children.Add(_popOut);
        }

        /// <summary>Factory that creates a fresh visualization instance (one per hosted view / window).</summary>
        public Func<IEngine3DVisualization>? VisualizationFactory
        {
            get => GetValue(VisualizationFactoryProperty);
            set => SetValue(VisualizationFactoryProperty, value);
        }

        /// <summary>Title shown on the pop-out window.</summary>
        public string? Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>Whether the "Open in window" button is shown (hidden inside the pop-out window itself).</summary>
        public bool ShowPopOut
        {
            get => GetValue(ShowPopOutProperty);
            set => SetValue(ShowPopOutProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == VisualizationFactoryProperty) EnsureViz();
            else if (change.Property == ShowPopOutProperty) _popOut.IsVisible = ShowPopOut && CanPopOut();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            // Only offer the pop-out where it's possible: a desktop (multi-window) lifetime with a usable
            // GPU engine. On Browser/Android (single-view, no engine) the button stays hidden.
            _popOut.IsVisible = ShowPopOut && CanPopOut();
            ThemePalette.Changed += OnThemeChanged;
            EnsureViz();
        }

        private static bool CanPopOut()
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime) return false;
            return App.ServiceProvider?.GetService<I3DEngineFactory>()?.IsAvailable == true;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            ThemePalette.Changed -= OnThemeChanged;
        }

        private void EnsureViz()
        {
            if (_built || VisualizationFactory is null) return;
            _viz = VisualizationFactory();
            _viz.Build(_view.Scene);
            _viz.ApplyTheme(_view.Scene);
            _view.OnUpdate = (scene, dt) => _viz!.Update(scene, dt);
            _built = true;
        }

        private void OnThemeChanged()
        {
            _viz?.ApplyTheme(_view.Scene);
        }

        private void OpenInWindow()
        {
            if (VisualizationFactory is null || !CanPopOut()) return;
            var window = new Engine3DVisualWindow();
            window.Configure(Title ?? "Visualizer", VisualizationFactory);
            if (TopLevel.GetTopLevel(this) is Window owner) window.Show(owner);
            else window.Show();
        }
    }
}
