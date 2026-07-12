using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.Localization;
using Ongenet.App.Platform;
using Ongenet.App.Services;
using Ongenet.App.ViewModels.Panels;
using Ongenet.App.Views.Windows;

namespace Ongenet.App.Views.Panels;

public partial class ScriptingPanelView : UserControl
{
    private ScriptingPopOutWindow? _popOutWindow;

    public ScriptingPanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not ScriptingPanelViewModel vm) return;
        vm.PopOutRequested -= OnPopOutRequested;
        vm.PopOutRequested += OnPopOutRequested;
        MountEditor(vm);
    }

    private void MountEditor(ScriptingPanelViewModel vm)
    {
        EditorHost.Content = null;
        var factory = App.ServiceProvider?.GetService<IScriptEditorFactory>();
        if (factory is null || !factory.IsAvailable || vm.EditorSession is null) return;
        EditorHost.Content = factory.CreateEditor(vm.EditorSession);
    }

    private void OnPopOutRequested()
    {
        if (DataContext is not ScriptingPanelViewModel vm) return;
        if (_popOutWindow is not null)
        {
            _popOutWindow.Activate();
            return;
        }

        _popOutWindow = new ScriptingPopOutWindow { DataContext = vm };
        _popOutWindow.Closed += (_, _) => _popOutWindow = null;
        _popOutWindow.Show();
    }

    private async void LoadScript_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ScriptingPanelViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Get("Scripts_Load_dialog_title"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.Get("Scripts_CSharp_files")) { Patterns = ["*.cs"] }
            ]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) vm.LoadScript(path);
    }

    private async void SaveScriptAs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ScriptingPanelViewModel vm) return;
        if (vm.SelectedScript is null) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.Get("Scripts_Save_dialog_title"),
            SuggestedFileName = vm.SelectedScript.Name + ".cs",
            DefaultExtension = "cs",
            FileTypeChoices =
            [
                new FilePickerFileType(Loc.Get("Scripts_CSharp_files")) { Patterns = ["*.cs"] }
            ]
        });
        var path = file?.TryGetLocalPath();
        if (path is null) return;
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) path += ".cs";
        var userDir = AppPaths.UserScriptsDirectory();
        if (!path.StartsWith(userDir, StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(userDir, Path.GetFileName(path));
        vm.SaveScriptToPath(path);
    }
}
