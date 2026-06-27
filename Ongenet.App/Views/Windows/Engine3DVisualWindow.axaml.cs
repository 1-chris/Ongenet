using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Ongenet.App.Controls;
using Ongenet.App.Controls.Engine3D;

namespace Ongenet.App.Views.Windows
{
    /// <summary>
    /// A generic, freely-resizable window that hosts a single GPU 3D visualization. Reused by any control
    /// via <see cref="Engine3DVisualHost"/>'s "Open in window" button; it builds its own engine view from
    /// the supplied factory so it renders independently of the embedded one.
    /// </summary>
    public partial class Engine3DVisualWindow : ChromedWindow
    {
        public Engine3DVisualWindow()
        {
            InitializeComponent();
        }

        /// <summary>Sets the window title and the visualization to host.</summary>
        public void Configure(string title, Func<IEngine3DVisualization> factory)
        {
            Title = title;
            TitleText.Text = title;
            VisualHost.Title = title;
            VisualHost.VisualizationFactory = factory;
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Source is not Control c || c is Button || c.Parent is Button) return;
            BeginMoveDrag(e);
        }

        private void OnResizeHandlePressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border border || border.Tag is not string tag) return;
            var edge = tag switch
            {
                "Left" => WindowEdge.West,
                "Right" => WindowEdge.East,
                "Top" => WindowEdge.North,
                "Bottom" => WindowEdge.South,
                "TopLeft" => WindowEdge.NorthWest,
                "TopRight" => WindowEdge.NorthEast,
                "BottomLeft" => WindowEdge.SouthWest,
                "BottomRight" => WindowEdge.SouthEast,
                _ => WindowEdge.North
            };
            BeginResizeDrag(edge, e);
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
    }
}
