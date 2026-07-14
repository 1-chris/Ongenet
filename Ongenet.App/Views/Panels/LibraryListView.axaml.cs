using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ongenet.App.Localization;
using Ongenet.App.Services;
using Ongenet.App.ViewModels.Library;
using Ongenet.App.Views.Windows;

namespace Ongenet.App.Views.Panels
{
    /// <summary>
    /// Renders any <see cref="LibraryListViewModel"/> (Everything / Samples / Soundfonts / Instruments /
    /// Effects / presets) as a searchable tree of folders and draggable leaves. Each leaf drags its node's
    /// payload (instrument/effect id, file path or preset path) and double-clicking runs its optional
    /// activate action (e.g. preview a sample). Folder rows are not draggable.
    /// </summary>
    public partial class LibraryListView : UserControl
    {
        private const double DragThreshold = 4;
        private LibraryNode? _pressed;
        private PointerPressedEventArgs? _pressArgs;
        private Point _pressPoint;

        public LibraryListView()
        {
            InitializeComponent();
            Ongenet.App.Accessibility.A11y.Landmark(this,
                Ongenet.App.Localization.Loc.Get("A11y_LibraryBrowser_Name"),
                Ongenet.App.Localization.Loc.Get("A11y_LibraryBrowser_Help"));
            NodeTree.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            NodeTree.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
            NodeTree.AddHandler(DoubleTappedEvent, OnDoubleTapped);
            NodeTree.SelectionChanged += OnSelectionChanged;
            NodeTree.AddHandler(TreeViewItem.ExpandedEvent, OnItemExpanded, RoutingStrategies.Bubble);
        }

        private static LibraryNode? DraggableOf(object? source)
            => (source as Control)?.DataContext is LibraryNode { DragFormat: not null } n ? n : null;

        private static LibraryNode? NodeOf(object? source) => (source as Control)?.DataContext as LibraryNode;

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Star clicks toggle favourite — don't start a drag from them.
            if (e.Source is Control c && c.Classes.Contains("library-star")) return;

            _pressed = DraggableOf(e.Source);
            _pressArgs = _pressed is not null ? e : null;
            if (_pressed is not null) _pressPoint = e.GetPosition(this);
        }

        private async void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_pressed is null || _pressArgs is null) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _pressed = null; return; }
            var delta = e.GetPosition(this) - _pressPoint;
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold) return;

            var node = _pressed;
            var args = _pressArgs;
            _pressed = null;
            _pressArgs = null;

            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(node.DragFormat!, node.DragPayload!));
            try { await DragDrop.DoDragDropAsync(args, data, DragDropEffects.Copy); }
            catch (Exception) { /* drag cancelled */ }
        }

        private void OnDoubleTapped(object? sender, TappedEventArgs e)
        {
            var node = NodeOf(e.Source);
            if (node is null) return;
            if (DataContext is InstrumentPresetLibraryViewModel instPresets &&
                node.DragFormat == DragFormats.Preset && node.DragPayload is { Length: > 0 } path)
            {
                instPresets.LoadSelectedPreset(path);
                return;
            }

            node.Activate?.Invoke();
        }

        private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not LibraryListViewModel { PreviewOnSelect: true } vm) return;
            if (NodeTree.SelectedItem is not LibraryNode { IsFolder: false } node) return;
            node.Activate?.Invoke();
        }

        private void OnItemExpanded(object? sender, RoutedEventArgs e)
            => TreeBrowseHelper.ScrollExpandedIntoView(NodeTree, e.Source as Control);

        private void OnExpandRecursive(object? sender, RoutedEventArgs e) => SetExpanded(sender, true);

        private void OnCollapseRecursive(object? sender, RoutedEventArgs e) => SetExpanded(sender, false);

        private static void SetExpanded(object? sender, bool expanded)
        {
            if ((sender as Control)?.DataContext is LibraryNode node) SetExpandedRecursive(node, expanded);
        }

        private static void SetExpandedRecursive(LibraryNode node, bool expanded)
        {
            if (!node.IsFolder) return;
            node.IsExpanded = expanded;
            foreach (var c in node.Children) SetExpandedRecursive(c, expanded);
        }

        private void OnToggleFavourite(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is LibraryNode node)
                node.ToggleFavourite();
            e.Handled = true;
        }

        private async void OnNewCategory(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not LibraryListViewModel vm || vm.Organization is null) return;
            if ((sender as Control)?.DataContext is not LibraryNode { CanFavourite: true } node) return;
            var owner = this.FindAncestorOfType<Window>()
                ?? (Avalonia.Application.Current?.ApplicationLifetime
                    as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner is null) return;

            var name = await InputDialog.Prompt(owner,
                Loc.Get("LibraryOrg_NewCategory_Title", "New category"),
                Loc.Get("LibraryOrg_NewCategory_Label", "Category name:"),
                "", Loc.Get("LibraryOrg_Create", "Create"));
            if (string.IsNullOrWhiteSpace(name)) return;
            var cat = vm.Organization.CreateCategory(name);
            vm.Organization.AddToCategory(cat.Id, node.ItemKey);
        }

        private async void OnAddToCategory(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not LibraryListViewModel vm || vm.Organization is null) return;
            if ((sender as Control)?.DataContext is not LibraryNode { CanFavourite: true } node) return;
            var cats = vm.Organization.Categories;
            if (cats.Count == 0)
            {
                OnNewCategory(sender, e);
                return;
            }

            var owner = this.FindAncestorOfType<Window>()
                ?? (Avalonia.Application.Current?.ApplicationLifetime
                    as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner is null) return;

            // Simple picker: offer existing names as a single-line prompt with suggestion in label.
            var names = string.Join(", ", cats.Select(c => c.Name));
            var chosen = await InputDialog.Prompt(owner,
                Loc.Get("LibraryOrg_AddToCategory_Title", "Add to category"),
                Loc.Get("LibraryOrg_AddToCategory_Label", "Category name ({0}):", names),
                cats[0].Name, Loc.Get("LibraryOrg_Add", "Add"));
            if (string.IsNullOrWhiteSpace(chosen)) return;
            var cat = cats.FirstOrDefault(c =>
                string.Equals(c.Name, chosen.Trim(), StringComparison.OrdinalIgnoreCase));
            if (cat is null)
            {
                cat = vm.Organization.CreateCategory(chosen);
            }
            vm.Organization.AddToCategory(cat.Id, node.ItemKey);
        }

        private void OnRemoveFromCategories(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not LibraryListViewModel vm || vm.Organization is null) return;
            if ((sender as Control)?.DataContext is not LibraryNode { CanFavourite: true } node) return;
            foreach (var cat in vm.Organization.Categories.ToList())
                vm.Organization.RemoveFromCategory(cat.Id, node.ItemKey);
        }
    }
}
