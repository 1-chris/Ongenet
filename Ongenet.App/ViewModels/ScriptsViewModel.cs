using System;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.App.ViewModels;
using Ongenet.Core.Services;

namespace Ongenet.App.ViewModels;

/// <summary>Backs the Scripts window: lists loaded scripts and runs them via <see cref="IScriptingHost"/>.</summary>
public sealed class ScriptsViewModel : ViewModelBase
{
    private readonly IScriptingHost _host;
    private string? _statusMessage;
    private ScriptItemViewModel? _selectedScript;

    public ScriptsViewModel(IScriptingHost host)
    {
        _host = host;
        RunCommand = new RelayCommand(RunSelected, () => SelectedScript is not null && _host.IsEnabled);
        Refresh();
        LoadFactoryScripts();
    }

    public ObservableCollection<ScriptItemViewModel> Scripts { get; } = new();

    public ScriptItemViewModel? SelectedScript
    {
        get => _selectedScript;
        set
        {
            if (!SetField(ref _selectedScript, value)) return;
            RunCommand.RaiseCanExecuteChanged();
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public RelayCommand RunCommand { get; }

    public void LoadScript(string path)
    {
        try
        {
            _host.LoadScript(path);
            Refresh();
            StatusMessage = L("Scripts_Loaded", System.IO.Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            StatusMessage = L("Scripts_Error", ex.Message);
        }
    }

    private void RunSelected()
    {
        if (SelectedScript is null) return;
        try
        {
            _host.Invoke(SelectedScript.Name, "Run");
            StatusMessage = L("Scripts_Ran", SelectedScript.Name);
        }
        catch (Exception ex)
        {
            StatusMessage = L("Scripts_Error", ex.Message);
        }
    }

    private void LoadFactoryScripts()
    {
        if (!_host.IsEnabled) return;
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Scripts");
        if (!System.IO.Directory.Exists(dir)) return;
        foreach (var file in System.IO.Directory.GetFiles(dir, "*.cs").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try { _host.LoadScript(file); }
            catch
            {
                // Ignore broken factory scripts at startup.
            }
        }
        Refresh();
    }

    private void Refresh()
    {
        Scripts.Clear();
        foreach (var name in _host.LoadedScripts)
            Scripts.Add(new ScriptItemViewModel(name));
        SelectedScript = Scripts.FirstOrDefault();
        RunCommand.RaiseCanExecuteChanged();
    }
}

public sealed class ScriptItemViewModel : ViewModelBase
{
    public ScriptItemViewModel(string name) => Name = name;

    public string Name { get; }
}
