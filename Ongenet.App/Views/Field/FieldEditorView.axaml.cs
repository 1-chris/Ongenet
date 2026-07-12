using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Ongenet.App.Controls;
using Ongenet.App.Controls.Field;
using Ongenet.App.ViewModels.Field;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Files;

namespace Ongenet.App.Views.Field;

/// <summary>
/// Hosts the Field node-graph editor: a toolbar (built-in patch picker + pop-out), the component palette,
/// the <see cref="Controls.Field.FieldCanvasControl"/>, and the selected-node parameter inspector. Palette
/// clicks add a node at the current view centre; the pop-out button raises <see cref="PopOutRequested"/>
/// so the embedding card/slot can open the standalone Field window.
/// </summary>
public partial class FieldEditorView : UserControl
{
    public FieldEditorView()
    {
        InitializeComponent();
        Ongenet.App.Accessibility.A11y.Landmark(this,
            Ongenet.App.Localization.Loc.Get("A11y_FieldEditor_Name"),
            Ongenet.App.Localization.Loc.Get("A11y_FieldEditor_Help"));
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Raised when the user clicks "Pop out". The host opens a standalone Field window.</summary>
    public event Action? PopOutRequested;

    private bool _showPopOut = true;

    /// <summary>Shows/hides the pop-out button (hidden inside the standalone window itself).</summary>
    public bool ShowPopOut
    {
        get => _showPopOut;
        set
        {
            _showPopOut = value;
            if (this.FindControl<Button>("PopOutButton") is { } b) b.IsVisible = value;
        }
    }

    private readonly Dictionary<Guid, Engine3DVisualHost> _visuals = new();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (this.FindControl<Button>("PopOutButton") is { } b)
        {
            b.IsVisible = _showPopOut;
            b.Click -= OnPopOut;
            b.Click += OnPopOut;
        }

        if (this.FindControl<FieldCanvasControl>("Canvas") is { } canvas)
        {
            canvas.ViewChanged -= RepositionVisuals;
            canvas.ViewChanged += RepositionVisuals;
        }

        RepositionVisuals();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ClearVisuals();
        RepositionVisuals();
    }

    private void ClearVisuals()
    {
        if (this.FindControl<Canvas>("VisualOverlay") is { } overlay) overlay.Children.Clear();
        _visuals.Clear();
    }

    // Creates/positions/removes the GPU visualization hosts so each visual node shows its live 3D view,
    // tracking the canvas transform (pan/zoom/drag/resize) and the current graph.
    private void RepositionVisuals()
    {
        if (this.FindControl<FieldCanvasControl>("Canvas") is not { } canvas) return;
        if (this.FindControl<Canvas>("VisualOverlay") is not { } overlay) return;
        if (DataContext is not FieldEditorViewModel vm)
        {
            ClearVisuals();
            return;
        }

        var live = new HashSet<Guid>();
        foreach (var node in vm.Graph.Nodes)
        {
            if (!FieldNodeVisuals.HasVisual(node)) continue;
            live.Add(node.Id);

            if (!_visuals.TryGetValue(node.Id, out var host))
            {
                host = new Engine3DVisualHost
                {
                    ShowPopOut = false,
                    IsHitTestVisible = false,
                    VisualizationFactory = FieldNodeVisuals.CreateFactory(node)
                };
                _visuals[node.Id] = host;
                overlay.Children.Add(host);
            }

            if (canvas.TryGetVisualRect(node, out var rect) && rect.Width > 6 && rect.Height > 6)
            {
                Avalonia.Controls.Canvas.SetLeft(host, rect.X);
                Avalonia.Controls.Canvas.SetTop(host, rect.Y);
                host.Width = rect.Width;
                host.Height = rect.Height;
                host.IsVisible = true;
            }
            else
            {
                host.IsVisible = false;
            }
        }

        // Remove hosts whose node is gone.
        var stale = new List<Guid>();
        foreach (var id in _visuals.Keys) if (!live.Contains(id)) stale.Add(id);
        foreach (var id in stale)
        {
            overlay.Children.Remove(_visuals[id]);
            _visuals.Remove(id);
        }
    }

    private void OnPopOut(object? sender, RoutedEventArgs e)
    {
        PopOutRequested?.Invoke();
        if (DataContext is not FieldEditorViewModel vm) return;
        var window = new Windows.FieldWindow();
        window.Configure(vm.IsInstrument ? "Field — Instrument" : "Field — Effect", vm);
        if (Avalonia.Controls.TopLevel.GetTopLevel(this) is Avalonia.Controls.Window owner) window.Show(owner);
        else window.Show();
    }

    private void OnPaletteItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FieldPaletteItem item }
            && this.FindControl<Controls.Field.FieldCanvasControl>("Canvas") is { } canvas)
        {
            canvas.AddNodeAtViewCenter(item.TypeId);
        }
    }

    private void OnExitGroup(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FieldEditorViewModel vm) vm.ExitGroup();
    }

    private async void OnSavePatch(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FieldEditorViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Field patch",
            SuggestedFileName = string.IsNullOrWhiteSpace(vm.PatchName) ? "Field Patch" : vm.PatchName.Trim(),
            DefaultExtension = "ongenpreset",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("Field patch") { Patterns = new[] { "*.ongenpreset" } }
            }
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;
        vm.CaptureHistory("Save Field patch");
        vm.SavePatchToFile(path);
    }

    private async void OnLoadPatch(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FieldEditorViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load Field patch",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Field patch") { Patterns = new[] { "*.ongenpreset" } }
            }
        });

        var path = System.Linq.Enumerable.FirstOrDefault(files)?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;
        vm.LoadPatchFromFile(path);
    }

    private async void OnLoadSample(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FieldEditorViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Load sample",
            AllowMultiple = false,
            FileTypeFilter = new List<Avalonia.Platform.Storage.FilePickerFileType>
            {
                new("Audio") { Patterns = new[] { "*.wav", "*.wave", "*.flac", "*.mp3", "*.ogg", "*.aiff" } }
            }
        });

        var path = System.Linq.Enumerable.FirstOrDefault(files)?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;
        var svc = App.ServiceProvider?.GetService(typeof(IAudioFileService)) as IAudioFileService;
        var loaded = svc?.Load(path);
        if (loaded is not null) vm.LoadSampleIntoSelected(loaded.Samples, System.IO.Path.GetFileName(path));
    }

    private async void OnLoadSoundFont(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FieldEditorViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Load soundfont",
            AllowMultiple = false,
            FileTypeFilter = new List<Avalonia.Platform.Storage.FilePickerFileType>
            {
                new("Sound fonts") { Patterns = new[] { "*.sfz", "*.sf2" } }
            }
        });

        var path = System.Linq.Enumerable.FirstOrDefault(files)?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;
        if (vm.SelectedNode is not Ongenet.Core.Audio.Field.Nodes.SoundFontNode sf) return;

        // Decode off the UI thread (SF2s can be large), then recompile on the UI thread so the sampler
        // node picks up the loaded patch via its asset inlet.
        await System.Threading.Tasks.Task.Run(() => sf.LoadFromPath(path));
        vm.NotifyStructureChanged();
        vm.RefreshSelectedResourceStatus();
    }
}
