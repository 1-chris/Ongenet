using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Ongenet.App.Views.Panels;

/// <summary>Shared TreeView helpers: keep deep nested branches horizontally in view when expanding.</summary>
public static class TreeBrowseHelper
{
    public static void EnableHorizontalBrowsing(TreeView tree)
    {
        tree.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
    }

    /// <summary>After a <see cref="TreeViewItem"/> expands, scroll so its indent is visible.</summary>
    public static void ScrollExpandedIntoView(TreeView tree, Control? source)
    {
        if (source is null) return;
        Dispatcher.UIThread.Post(() =>
        {
            var item = source as TreeViewItem ?? source.FindAncestorOfType<TreeViewItem>();
            if (item is null) return;

            item.BringIntoView();

            var sv = tree.FindDescendantOfType<ScrollViewer>()
                     ?? tree.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (sv is null) return;

            var bounds = item.Bounds;
            var transform = item.TransformToVisual(sv);
            if (transform is null) return;
            var topLeft = transform.Value.Transform(new Point(0, 0));
            var right = topLeft.X + bounds.Width;
            if (right > sv.Viewport.Width + sv.Offset.X - 8)
            {
                var targetX = Math.Max(0, right - sv.Viewport.Width + 24);
                sv.Offset = new Vector(targetX, sv.Offset.Y);
            }
        }, DispatcherPriority.Loaded);
    }
}
