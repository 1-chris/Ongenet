using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Ongenet.App.Platform;
using Ongenet.App.Services;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Scripting;

namespace Ongenet.App.ViewModels.Panels;

/// <summary>Scripting IDE: script list, editor, output, and host commands.</summary>
public sealed class ScriptingPanelViewModel : ViewModelBase
{
    private const string NewScriptTemplate =
        """
        // Edit and Run against the open project. Global api is IScriptingApi.
        api.Log("Hello from script");
        """;

    private readonly IScriptingHost _host;
    private readonly IScriptingApi _api;
    private readonly IScriptEditorFactory _editorFactory;
    private readonly ISelectionService? _selection;
    private string? _statusMessage;
    private ScriptItemViewModel? _selectedScript;
    private string _outputText = string.Empty;
    private bool _isRunning;
    private string _editorText = string.Empty;
    private string? _savedSnapshot;
    private int _errorCount;
    private int _warningCount;
    private readonly Dictionary<string, string> _scriptBuffers = new(StringComparer.OrdinalIgnoreCase);

    public ScriptingPanelViewModel(IScriptingHost host, IScriptingApi api, IScriptEditorFactory editorFactory,
        ISelectionService? selection = null)
    {
        _host = host;
        _api = api;
        _editorFactory = editorFactory;
        _selection = selection;
        RunCommand = new RelayCommand(() => _ = RunSelectedAsync(), CanRunBatch);
        StartLiveCommand = new RelayCommand(StartLiveSelected, CanStartLive);
        StopLiveCommand = new RelayCommand(StopLiveSelected, () => SelectedScript is not null && _host.IsScriptLive(SelectedScript.Name));
        ReloadCommand = new RelayCommand(ReloadSelected, () => SelectedScript is not null);
        UnloadCommand = new RelayCommand(UnloadSelected, () => SelectedScript is not null);
        SaveCommand = new RelayCommand(SaveSelected, () => SelectedScript is not null && IsDirty);
        NewScriptCommand = new RelayCommand(NewScript, () => _host.IsEnabled);
        ClearOutputCommand = new RelayCommand(ClearOutput);
        PopOutCommand = new RelayCommand(() => PopOutRequested?.Invoke());
        ExportProjectCommand = new RelayCommand(ExportProjectAsScript, () => _host.IsEnabled);
        ExportPresetCommand = new RelayCommand(ExportPresetAsScript, () => _host.IsEnabled);
        _api.OutputChanged += RefreshOutput;
        if (_editorFactory.IsAvailable)
        {
            EditorSession = _editorFactory.CreateSession();
            EditorSession.TextChanged += OnEditorTextChanged;
            EditorSession.AnalysisUpdated += OnAnalysisUpdated;
        }

        Refresh();
        LoadScripts();
    }

    public event Action? PopOutRequested;

    public IScriptEditorSession? EditorSession { get; }

    public ObservableCollection<ScriptItemViewModel> Scripts { get; } = new();

    public ScriptItemViewModel? SelectedScript
    {
        get => _selectedScript;
        set
        {
            if (ReferenceEquals(_selectedScript, value)) return;
            PersistEditorBufferForCurrentScript();
            if (!SetField(ref _selectedScript, value)) return;
            LoadSelectedIntoEditor();
            RaiseCommandStates();
        }
    }

    public string EditorText
    {
        get => _editorText;
        private set => SetField(ref _editorText, value);
    }

    public bool IsDirty => SelectedScript is not null && _savedSnapshot != EditorText;

    public int ErrorCount
    {
        get => _errorCount;
        private set => SetField(ref _errorCount, value);
    }

    public int WarningCount
    {
        get => _warningCount;
        private set => SetField(ref _warningCount, value);
    }

    public string OutputText
    {
        get => _outputText;
        private set => SetField(ref _outputText, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetField(ref _isRunning, value)) return;
            RaiseCommandStates();
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string UserScriptsFolder => AppPaths.UserScriptsDirectory();

    public RelayCommand RunCommand { get; }
    public RelayCommand StartLiveCommand { get; }
    public RelayCommand StopLiveCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand UnloadCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand NewScriptCommand { get; }
    public RelayCommand ClearOutputCommand { get; }
    public RelayCommand PopOutCommand { get; }
    public RelayCommand ExportProjectCommand { get; }
    public RelayCommand ExportPresetCommand { get; }

    public void LoadScript(string path)
    {
        try
        {
            _host.LoadScript(path);
            Refresh();
            SelectedScript = Scripts.FirstOrDefault(s =>
                string.Equals(s.Name, Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase));
            StatusMessage = L("Scripts_Loaded", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            StatusMessage = L("Scripts_Error", ex.Message);
        }
    }

    public void SaveScriptToPath(string targetPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, EditorText);
            var name = Path.GetFileNameWithoutExtension(targetPath);
            _host.LoadScriptFromText(name, EditorText, targetPath);
            _savedSnapshot = EditorText;
            Refresh();
            SelectedScript = Scripts.FirstOrDefault(s => s.Name == name);
            OnPropertyChanged(nameof(IsDirty));
            StatusMessage = L("Scripts_Saved", Path.GetFileName(targetPath));
        }
        catch (Exception ex)
        {
            StatusMessage = L("Scripts_Error", ex.Message);
        }
    }

    private void ExportProjectAsScript()
    {
        try
        {
            var code = _api.ExportProjectAsScript();
            OpenGeneratedScript("Generated_Project", code);
            StatusMessage = L("Scripting_ExportProject_done", code.Split('\n').Length);
        }
        catch (Exception ex)
        {
            StatusMessage = L("Scripts_Error", ex.Message);
        }
    }

    private void ExportPresetAsScript()
    {
        try
        {
            var track = _selection?.SelectedTrack;
            if (track is null)
            {
                StatusMessage = L("Scripting_ExportPreset_no_track");
                return;
            }

            string code;
            if (track.Instruments.Count > 0)
                code = _api.ExportInstrumentSlotAsScript(track.Id, 0);
            else
                code = _api.ExportEffectChainAsScript(track.Id);

            OpenGeneratedScript("Generated_Preset", code);
            StatusMessage = L("Scripting_ExportPreset_done", track.Name);
        }
        catch (Exception ex)
        {
            StatusMessage = L("Scripts_Error", ex.Message);
        }
    }

    private void OpenGeneratedScript(string namePrefix, string code)
    {
        var name = $"{namePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}";
        _host.LoadScriptFromText(name, code, null);
        _scriptBuffers[name] = code;
        _savedSnapshot = code;
        ApplyEditorText(code, code);
        Refresh();
        SelectedScript = Scripts.FirstOrDefault(s => s.Name == name);
    }

    private void SaveSelected()
    {
        if (SelectedScript?.Path is not { } path)
        {
            StatusMessage = L("Scripting_Save_requires_path");
            return;
        }

        try
        {
            File.WriteAllText(path, EditorText);
            _host.UpdateScriptSource(SelectedScript.Name, EditorText);
            _savedSnapshot = EditorText;
            OnPropertyChanged(nameof(IsDirty));
            StatusMessage = L("Scripts_Saved", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            StatusMessage = L("Scripts_Error", ex.Message);
        }
    }

    private void NewScript()
    {
        var dir = AppPaths.UserScriptsDirectory();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"Script_{DateTime.Now:yyyyMMdd_HHmmss}.cs");
        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            File.WriteAllText(path, NewScriptTemplate);
            _host.LoadScriptFromText(name, NewScriptTemplate, path);
            _savedSnapshot = NewScriptTemplate;
            EditorText = NewScriptTemplate;
            EditorSession?.LoadText(NewScriptTemplate);
            Refresh();
            SelectedScript = Scripts.FirstOrDefault(s => s.Name == name);
            OnPropertyChanged(nameof(IsDirty));
            StatusMessage = L("Scripts_Saved", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            StatusMessage = L("Scripts_Error", ex.Message);
        }
    }

    private async Task RunSelectedAsync()
    {
        if (SelectedScript is null || IsRunning) return;
        if (!FlushEditorToHost()) return;
        IsRunning = true;
        try
        {
            await Task.Run(() => _host.Invoke(SelectedScript.Name, "Run"));
            await Dispatcher.UIThread.InvokeAsync(RefreshOutput);
            StatusMessage = L("Scripts_Ran", SelectedScript.Name);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(RefreshOutput);
            StatusMessage = L("Scripts_Error", ex.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void StartLiveSelected()
    {
        if (SelectedScript is null) return;
        if (!FlushEditorToHost()) return;
        try
        {
            _host.StartLive(SelectedScript.Name, SynchronizationContext.Current);
            Refresh();
            StatusMessage = L("Scripts_Live_started", SelectedScript.Name);
        }
        catch (Exception ex)
        {
            RefreshOutput();
            StatusMessage = L("Scripts_Error", ex.Message);
        }
    }

    private void StopLiveSelected()
    {
        if (SelectedScript is null) return;
        _host.StopLive(SelectedScript.Name);
        Refresh();
        StatusMessage = L("Scripts_Live_stopped", SelectedScript.Name);
    }

    private void ReloadSelected()
    {
        if (SelectedScript is null) return;
        _scriptBuffers.Remove(SelectedScript.Name);
        var path = _host.GetScriptPath(SelectedScript.Name);
        if (path is not null && File.Exists(path))
            LoadScript(path);
        else
            LoadSelectedIntoEditor();
    }

    private void UnloadSelected()
    {
        if (SelectedScript is null) return;
        var name = SelectedScript.Name;
        _scriptBuffers.Remove(name);
        _host.UnloadScript(name);
        Refresh();
        StatusMessage = L("Scripts_Unloaded", name);
    }

    private void ClearOutput()
    {
        _api.ClearOutput();
        RefreshOutput();
    }

    private bool FlushEditorToHost()
    {
        if (SelectedScript is null) return false;
        try
        {
            _host.UpdateScriptSource(SelectedScript.Name, EditorText);
            _scriptBuffers[SelectedScript.Name] = EditorText;
            if (SelectedScript.Path is { } path)
            {
                File.WriteAllText(path, EditorText);
                _savedSnapshot = EditorText;
                OnPropertyChanged(nameof(IsDirty));
            }

            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = L("Scripts_Error", ex.Message);
            return false;
        }
    }

    private void LoadScripts()
    {
        if (!_host.IsEnabled) return;
        var factoryDir = Path.Combine(AppContext.BaseDirectory, "Scripts");
        if (Directory.Exists(factoryDir))
        {
            foreach (var file in Directory.GetFiles(factoryDir, "*.cs").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                TryLoadScript(file);
        }

        var userDir = AppPaths.UserScriptsDirectory();
        if (Directory.Exists(userDir))
        {
            foreach (var file in Directory.GetFiles(userDir, "*.cs").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                TryLoadScript(file);
        }

        Refresh();
    }

    private void TryLoadScript(string file)
    {
        try { _host.LoadScript(file); }
        catch (Exception ex)
        {
            StatusMessage = L("Scripts_Load_failed", Path.GetFileName(file), ex.Message);
        }
    }

    private void Refresh()
    {
        Scripts.Clear();
        foreach (var name in _host.LoadedScripts)
        {
            Scripts.Add(new ScriptItemViewModel(name)
            {
                IsLive = _host.IsScriptLive(name),
                Path = _host.GetScriptPath(name),
                IsDirty = false
            });
        }

        SelectedScript = Scripts.FirstOrDefault(s => s.Name == SelectedScript?.Name) ?? Scripts.FirstOrDefault();
        RaiseCommandStates();
    }

    private void PersistEditorBufferForCurrentScript()
    {
        if (_selectedScript is null) return;
        _scriptBuffers[_selectedScript.Name] = EditorText;
    }

    private void LoadSelectedIntoEditor()
    {
        if (SelectedScript is null)
        {
            ApplyEditorText(string.Empty, string.Empty);
            return;
        }

        var name = SelectedScript.Name;
        var text = _scriptBuffers.TryGetValue(name, out var buffered)
            ? buffered
            : _host.GetScriptSource(name) ?? string.Empty;
        if (!_scriptBuffers.ContainsKey(name))
            _scriptBuffers[name] = text;

        var snapshot = _host.GetScriptSource(name) ?? text;
        ApplyEditorText(text, snapshot);
    }

    private void ApplyEditorText(string text, string savedSnapshot)
    {
        _savedSnapshot = savedSnapshot;
        EditorText = text;
        EditorSession?.LoadText(text);
        OnPropertyChanged(nameof(IsDirty));
        if (SelectedScript is not null)
            SelectedScript.IsDirty = IsDirty;
    }

    private void OnEditorTextChanged(string text)
    {
        EditorText = text;
        if (SelectedScript is not null)
        {
            _scriptBuffers[SelectedScript.Name] = text;
            SelectedScript.IsDirty = IsDirty;
        }
        OnPropertyChanged(nameof(IsDirty));
        SaveCommand.RaiseCanExecuteChanged();
    }

    private void OnAnalysisUpdated()
    {
        if (EditorSession is null) return;
        ErrorCount = EditorSession.ErrorCount;
        WarningCount = EditorSession.WarningCount;
    }

    private void RefreshOutput() => OutputText = string.Join(Environment.NewLine, _api.OutputLines);

    private bool CanRunBatch() =>
        SelectedScript is not null && _host.IsEnabled && !IsRunning && !_host.IsScriptLive(SelectedScript.Name);

    private bool CanStartLive() =>
        SelectedScript is not null && _host.IsEnabled && !IsRunning && !_host.IsScriptLive(SelectedScript.Name);

    private void RaiseCommandStates()
    {
        RunCommand.RaiseCanExecuteChanged();
        StartLiveCommand.RaiseCanExecuteChanged();
        StopLiveCommand.RaiseCanExecuteChanged();
        ReloadCommand.RaiseCanExecuteChanged();
        UnloadCommand.RaiseCanExecuteChanged();
        SaveCommand.RaiseCanExecuteChanged();
        NewScriptCommand.RaiseCanExecuteChanged();
    }
}

public sealed class ScriptItemViewModel : ViewModelBase
{
    private bool _isLive;
    private bool _isDirty;

    public ScriptItemViewModel(string name) => Name = name;

    public string Name { get; }
    public string? Path { get; init; }

    public bool IsLive
    {
        get => _isLive;
        set
        {
            if (!SetField(ref _isLive, value)) return;
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (!SetField(ref _isDirty, value)) return;
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string DisplayName => $"{Name}{(IsDirty ? " *" : "")}{(IsLive ? " ●" : "")}";
}
