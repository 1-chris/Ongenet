using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ongenet.Core.Audio.Instruments;
using Ongenet.App.ViewModels.Effects;
using Ongenet.App.Views.Windows;
using Ongenet.App.ViewModels;
using Ongenet.App.Localization;

namespace Ongenet.App.Views.Panels
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

        private EffectChainViewModel? _subscribedChain;

        public EffectChainView()
        {
            InitializeComponent();
            PluginEditorHost.EditorStateChanged += OnEditorStateChanged;
            DataContextChanged += OnDataContextChanged;

            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
            AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_subscribedChain is not null)
                _subscribedChain.PropertyChanged -= OnChainPropertyChanged;
            _subscribedChain = DataContext as EffectChainViewModel;
            if (_subscribedChain is not null)
                _subscribedChain.PropertyChanged += OnChainPropertyChanged;
        }

        private void OnChainPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EffectChainViewModel.HighlightedEffectIndex))
                ScrollToHighlightedEffect();
        }

        private void ScrollToHighlightedEffect()
        {
            if (DataContext is not EffectChainViewModel vm) return;
            var index = vm.HighlightedEffectIndex;
            if (index < 0 || index >= vm.Effects.Count) return;

            Dispatcher.UIThread.Post(() =>
            {
                var containers = EffectList.GetVisualDescendants()
                    .OfType<ContentPresenter>()
                    .Where(c => c.DataContext is EffectViewModel)
                    .ToList();
                if (index < containers.Count)
                    containers[index].BringIntoView();
            }, DispatcherPriority.Loaded);
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

        private async void OnLoadImpulse(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not ConvolutionEffectViewModel vm) return;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load impulse response",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("Audio") { Patterns = new[] { "*.wav", "*.wave" } }
                }
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) vm.LoadImpulseFromPath(path);
        }

        private async void OnBrowseReference(object? sender, RoutedEventArgs e)
            => await BrowseReferenceAsync(slot: 0);

        private async void OnBrowseReferenceB(object? sender, RoutedEventArgs e)
            => await BrowseReferenceAsync(slot: 1);

        private async Task BrowseReferenceAsync(int slot)
        {
            if (DataContext is not EffectChainViewModel vm) return;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = slot == 1
                    ? "Browse reference B"
                    : Loc.Get("Mastering_Reference_Browse"),
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("Audio")
                    {
                        Patterns = new[] { "*.wav", "*.wave", "*.flac", "*.aiff", "*.aif" }
                    },
                    new("WAV") { Patterns = new[] { "*.wav", "*.wave" } },
                    new("FLAC") { Patterns = new[] { "*.flac" } },
                    new("AIFF") { Patterns = new[] { "*.aiff", "*.aif" } }
                }
            });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) await vm.LoadReferenceAsync(path, slot);
        }

        private void OnReferencePressed(object? sender, PointerPressedEventArgs e)
        {
            (DataContext as EffectChainViewModel)?.StartReferenceAudition();
            e.Handled = true;
        }

        private void OnReferenceReleased(object? sender, PointerReleasedEventArgs e)
        {
            (DataContext as EffectChainViewModel)?.StopReferenceAudition();
            e.Handled = true;
        }

        private void OnOpenMasteringGuide(object? sender, RoutedEventArgs e)
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner is null) return;
            var guide = new GuideWindow();
            if (guide.DataContext is GuideViewModel vm)
                vm.Selected = vm.Sections.FirstOrDefault(s =>
                                  s.Title == Loc.Get("Guide_Mastering_Title", "Mastering")
                                  || s.Title.Contains("Mastering", StringComparison.OrdinalIgnoreCase))
                              ?? vm.Sections.FirstOrDefault(s => s.Title == Loc.Get("Guide_Mixer_Title"))
                              ?? vm.Selected;
            guide.Show(owner);
        }

        // Refresh the matching effect card's open/close button when its editor's state changes.
        private void OnEditorStateChanged(IPluginEditor editor)
        {
            if (DataContext is EffectChainViewModel vm) vm.RefreshEditor(editor);
        }
    }
}
