using Avalonia.Controls;
using Avalonia.Interactivity;
using Ongenet.App.Services;
using Ongenet.App.ViewModels;

namespace Ongenet.App.Views.Windows;

public partial class SamplerZoneEditorWindow : Window
{
    private SamplerZoneEditorViewModel? _vm;
    private int _lastPreviewKey = -1;

    public SamplerZoneEditorWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WirePreview();
        Closed += (_, _) => ReleasePreview();
        Opened += (_, _) => WirePreview();
    }

    private void WirePreview()
    {
        if (PianoCoverage is null) return;
        PianoCoverage.NotePreviewOn -= OnNotePreviewOn;
        PianoCoverage.NotePreviewOff -= OnNotePreviewOff;
        _vm = DataContext as SamplerZoneEditorViewModel;
        if (_vm is null) return;
        PianoCoverage.NotePreviewOn += OnNotePreviewOn;
        PianoCoverage.NotePreviewOff += OnNotePreviewOff;
    }

    private void OnNotePreviewOn(int key)
    {
        _lastPreviewKey = key;
        _vm?.PreviewNoteOn(key);
    }

    private void OnNotePreviewOff(int key)
    {
        if (_lastPreviewKey == key) _lastPreviewKey = -1;
        _vm?.PreviewNoteOff(key);
    }

    private void ReleasePreview()
    {
        if (_lastPreviewKey >= 0)
        {
            _vm?.PreviewNoteOff(_lastPreviewKey);
            _lastPreviewKey = -1;
        }
        if (PianoCoverage is not null)
        {
            PianoCoverage.NotePreviewOn -= OnNotePreviewOn;
            PianoCoverage.NotePreviewOff -= OnNotePreviewOff;
        }
    }

    private void OnAddLayer(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SamplerZoneEditorViewModel vm || sender is not Control anchor) return;
        SoundFontPickFlyout.Show(anchor, path => _ = vm.AddLayerFromPathAsync(path), "Add sound-font layer");
    }
}
