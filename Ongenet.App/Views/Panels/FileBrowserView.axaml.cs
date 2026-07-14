using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Ongenet.App.Localization;
using Ongenet.App.ViewModels;
using Ongenet.App.ViewModels.FileSystem;
using Ongenet.App.Views.Windows;

namespace Ongenet.App.Views.Panels
{
    /// <summary>
    /// Right-hand file browser. Starts a drag-and-drop operation when an audio file is dragged
    /// out, carrying the file path for the timeline to consume.
    /// </summary>
    public partial class FileBrowserView : UserControl
    {
        private const double DragThreshold = 4.0;

        private Point _pressPoint;
        private FileNodeViewModel? _pressedNode;
        private PointerPressedEventArgs? _pressArgs;

        public FileBrowserView()
        {
            InitializeComponent();

            FileTree.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            FileTree.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
            FileTree.AddHandler(TreeViewItem.ExpandedEvent, OnItemExpanded, RoutingStrategies.Bubble);
            FileTree.SelectionChanged += OnSelectionChanged;
        }

        private void OnItemExpanded(object? sender, RoutedEventArgs e)
            => TreeBrowseHelper.ScrollExpandedIntoView(FileTree, e.Source as Control);

        private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (FileTree.SelectedItem is not FileNodeViewModel { IsDirectory: false, IsSynthetic: false } node) return;
            if (App.ServiceProvider?.GetService(typeof(AudioPreviewViewModel)) is AudioPreviewViewModel preview)
                preview.Select(node.FullPath);
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Source is Control c && c.Classes.Contains("library-star")) return;

            _pressedNode = null;
            _pressArgs = null;
            if ((e.Source as Control)?.DataContext is FileNodeViewModel { IsDirectory: false, IsSynthetic: false } node)
            {
                _pressPoint = e.GetPosition(this);
                _pressedNode = node;
                _pressArgs = e;
            }
        }

        private async void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_pressedNode is null) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _pressedNode = null;
                return;
            }

            var delta = e.GetPosition(this) - _pressPoint;
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold) return;

            if (DataContext is not FileBrowserViewModel vm || _pressArgs is null || !vm.IsAudioFile(_pressedNode.FullPath))
            {
                _pressedNode = null;
                return;
            }

            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(DragFormats.AudioFile, _pressedNode.FullPath));
            var pressArgs = _pressArgs;
            _pressedNode = null;
            _pressArgs = null;

            try { await DragDrop.DoDragDropAsync(pressArgs, data, DragDropEffects.Copy); }
            catch (Exception) { /* cancelled */ }
        }

        private void OnToggleFavourite(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is FileNodeViewModel node)
                node.ToggleFavourite();
            e.Handled = true;
        }

        private async void OnNewCategory(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not FileBrowserViewModel vm) return;
            if ((sender as Control)?.DataContext is not FileNodeViewModel { CanFavourite: true } node) return;
            var owner = OwnerWindow();
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
            if (DataContext is not FileBrowserViewModel vm) return;
            if ((sender as Control)?.DataContext is not FileNodeViewModel { CanFavourite: true } node) return;
            var cats = vm.Organization.Categories;
            if (cats.Count == 0)
            {
                OnNewCategory(sender, e);
                return;
            }

            var owner = OwnerWindow();
            if (owner is null) return;
            var names = string.Join(", ", cats.Select(c => c.Name));
            var chosen = await InputDialog.Prompt(owner,
                Loc.Get("LibraryOrg_AddToCategory_Title", "Add to category"),
                Loc.Get("LibraryOrg_AddToCategory_Label", "Category name ({0}):", names),
                cats[0].Name, Loc.Get("LibraryOrg_Add", "Add"));
            if (string.IsNullOrWhiteSpace(chosen)) return;
            var cat = cats.FirstOrDefault(c =>
                string.Equals(c.Name, chosen.Trim(), StringComparison.OrdinalIgnoreCase));
            cat ??= vm.Organization.CreateCategory(chosen);
            vm.Organization.AddToCategory(cat.Id, node.ItemKey);
        }

        private void OnRemoveFromCategories(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not FileBrowserViewModel vm) return;
            if ((sender as Control)?.DataContext is not FileNodeViewModel { CanFavourite: true } node) return;
            foreach (var cat in vm.Organization.Categories.ToList())
                vm.Organization.RemoveFromCategory(cat.Id, node.ItemKey);
        }

        private Window? OwnerWindow()
            => this.FindAncestorOfType<Window>()
               ?? (Avalonia.Application.Current?.ApplicationLifetime
                   as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    }
}
