using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Desktop.ViewModels.Effects;
using Ongenet.Desktop.Views.Windows;

namespace Ongenet.Desktop.Views.Panels
{
    /// <summary>
    /// Reusable editor for one insert-effect chain (track-level or per-instrument). Hosts CLAP effect
    /// GUIs in their own windows, and accepts effects / effect presets dragged from the library. Dropping
    /// onto an existing effect card inserts above/below it or replaces it (zoned by pointer position);
    /// dropping on empty chain space appends. DataContext is an <see cref="EffectChainViewModel"/>.
    /// </summary>
    public partial class EffectChainView : UserControl
    {
        private enum Zone { Above, Replace, Below }

        public EffectChainView()
        {
            InitializeComponent();
            PluginEditorHost.EditorStateChanged += OnEditorStateChanged;

            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
            AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        }

        // Only effects / effect-presets are zoned onto a card; whole FX-chain presets always append.
        private static bool IsZoned(DragEventArgs e)
            => e.DataTransfer.Contains(DragFormats.Effect) || e.DataTransfer.Contains(DragFormats.Preset);

        private static bool Accepts(DragEventArgs e)
            => IsZoned(e) || e.DataTransfer.Contains(DragFormats.EffectChain);

        // The effect card (item container) under the pointer, plus which vertical third it's over.
        private (EffectViewModel? Card, ContentPresenter? Container, Zone Zone) HitCard(DragEventArgs e)
        {
            var presenter = (e.Source as Visual)?.GetVisualAncestors()
                .OfType<ContentPresenter>()
                .FirstOrDefault(c => c.DataContext is EffectViewModel);
            if (presenter?.DataContext is not EffectViewModel vm) return (null, null, Zone.Replace);

            var h = presenter.Bounds.Height;
            var t = h > 0 ? e.GetPosition(presenter).Y / h : 0.5;
            var zone = t < 0.30 ? Zone.Above : t > 0.70 ? Zone.Below : Zone.Replace;
            return (vm, presenter, zone);
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            if (DataContext is not EffectChainViewModel || !Accepts(e))
            {
                e.DragEffects = DragDropEffects.None;
                ClearIndicators();
                e.Handled = true;
                return;
            }

            e.DragEffects = DragDropEffects.Copy;
            if (IsZoned(e))
            {
                var (card, container, zone) = HitCard(e);
                if (card is not null && container is not null) ShowIndicator(container, zone);
                else ClearIndicators(); // over empty space → append, no indicator
            }
            else ClearIndicators();
            e.Handled = true;
        }

        private void OnDrop(object? sender, DragEventArgs e)
        {
            ClearIndicators();
            if (DataContext is not EffectChainViewModel vm) return;

            var (card, _, zone) = HitCard(e);

            if (e.DataTransfer.TryGetValue(DragFormats.Effect) is { } id)
            {
                Route(vm, card, zone, id, vm.InsertEffect, vm.ReplaceEffectAt, vm.AddEffect);
                e.Handled = true;
            }
            else if (e.DataTransfer.TryGetValue(DragFormats.Preset) is { } presetPath)
            {
                Route(vm, card, zone, presetPath, vm.InsertEffectPreset, vm.ReplaceEffectPresetAt, vm.AddEffectPreset);
                e.Handled = true;
            }
            else if (e.DataTransfer.TryGetValue(DragFormats.EffectChain) is { } chainPath)
            {
                vm.AddEffectChainPreset(chainPath); // whole chains always append
                e.Handled = true;
            }
        }

        // Maps a zoned drop onto a card to insert-above / insert-below / replace; empty space appends.
        private static void Route(EffectChainViewModel vm, EffectViewModel? card, Zone zone, string payload,
            Action<int, string> insert, Action<int, string> replace, Action<string> append)
        {
            if (card is null) { append(payload); return; }
            var i = vm.IndexOf(card);
            if (i < 0) { append(payload); return; }
            switch (zone)
            {
                case Zone.Above: insert(i, payload); break;
                case Zone.Below: insert(i + 1, payload); break;
                default: replace(i, payload); break;
            }
        }

        private void OnDragLeave(object? sender, DragEventArgs e) => ClearIndicators();

        private void ShowIndicator(ContentPresenter container, Zone zone)
        {
            var origin = container.TranslatePoint(new Point(0, 0), DropOverlay);
            if (origin is not { } p) { ClearIndicators(); return; }
            var w = container.Bounds.Width;
            var h = container.Bounds.Height;

            if (zone == Zone.Replace)
            {
                DropLine.IsVisible = false;
                Canvas.SetLeft(DropReplace, p.X);
                Canvas.SetTop(DropReplace, p.Y);
                DropReplace.Width = w;
                DropReplace.Height = h;
                DropReplace.IsVisible = true;
            }
            else
            {
                DropReplace.IsVisible = false;
                Canvas.SetLeft(DropLine, p.X);
                Canvas.SetTop(DropLine, zone == Zone.Above ? p.Y : p.Y + h);
                DropLine.Width = w;
                DropLine.IsVisible = true;
            }
        }

        private void ClearIndicators()
        {
            DropReplace.IsVisible = false;
            DropLine.IsVisible = false;
        }

        private void OnSaveChain(object? sender, RoutedEventArgs e)
        {
            (DataContext as EffectChainViewModel)?.SaveChainAsPreset();
            SaveChainButton.Flyout?.Hide();
        }

        private void OnToggleEffectPluginUi(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not EffectViewModel vm || vm.Editor is null) return;
            PluginEditorHost.Toggle(vm.Editor, vm.Name, TopLevel.GetTopLevel(this) as Window);
        }

        // Refresh the matching effect card's open/close button when its editor's state changes.
        private void OnEditorStateChanged(IPluginEditor editor)
        {
            if (DataContext is EffectChainViewModel vm) vm.RefreshEditor(editor);
        }
    }
}
