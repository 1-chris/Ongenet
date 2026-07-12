using System;
using System.Threading.Tasks;
using Ongenet.App.ViewModels.Timeline;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels;

/// <summary>Guided audio-to-MIDI workflow: choose algorithm, preview stats, create a MIDI track.</summary>
public sealed class AudioToMidiViewModel : ViewModelBase
{
    public enum ConversionMode
    {
        Monophonic,
        Polyphonic
    }

    private readonly IProjectService _project;
    private readonly ITransportService _transport;
    private readonly TimelineViewModel _timeline;
    private readonly ClipViewModel _clip;
    private ConversionMode _mode = ConversionMode.Monophonic;
    private bool _isAnalyzing;
    private int _detectedNotes;
    private string _status = "Choose a detection mode and click Analyze.";

    public AudioToMidiViewModel(IProjectService project, ITransportService transport,
        TimelineViewModel timeline, ClipViewModel clip)
    {
        _project = project;
        _transport = transport;
        _timeline = timeline;
        _clip = clip;
        AnalyzeCommand = new RelayCommand(() => _ = AnalyzeAsync(), () => CanAnalyze);
        CreateCommand = new RelayCommand(() => _ = CreateAsync(), () => CanCreate);
    }

    public string ClipName => _clip.Name;
    public ConversionMode[] Modes { get; } = Enum.GetValues<ConversionMode>();

    public ConversionMode Mode
    {
        get => _mode;
        set
        {
            if (!SetField(ref _mode, value)) return;
            DetectedNotes = 0;
            Status = "Mode changed — analyze again.";
            CreateCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (SetField(ref _isAnalyzing, value))
            {
                OnPropertyChanged(nameof(CanAnalyze));
                AnalyzeCommand.RaiseCanExecuteChanged();
                CreateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanAnalyze => !IsAnalyzing && _clip.Model.Samples is { FrameCount: > 0 };

    public int DetectedNotes
    {
        get => _detectedNotes;
        private set
        {
            if (SetField(ref _detectedNotes, value))
                CreateCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanCreate => DetectedNotes > 0 && !IsAnalyzing;

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public RelayCommand AnalyzeCommand { get; }
    public RelayCommand CreateCommand { get; }

    public event Action? RequestClose;

    private async Task AnalyzeAsync()
    {
        if (_clip.Model.Samples is not { } buffer) return;
        IsAnalyzing = true;
        Status = "Analyzing…";
        try
        {
            var length = _clip.Model.LengthBeats;
            if (Mode == ConversionMode.Monophonic)
            {
                var notes = await Task.Run(() => MonophonicAudioToMidi.Convert(buffer, length));
                DetectedNotes = notes.Count;
                Status = notes.Count == 0
                    ? "No monophonic pitches detected — try polyphonic mode."
                    : $"Detected {notes.Count} monophonic notes.";
            }
            else
            {
                var events = await Task.Run(() => PolyphonicAudioToMidi.Convert(buffer, length));
                DetectedNotes = events.Count;
                Status = events.Count == 0
                    ? "No polyphonic pitches detected — try monophonic mode."
                    : $"Detected {events.Count} polyphonic note events.";
            }
        }
        catch (Exception ex)
        {
            DetectedNotes = 0;
            Status = $"Analysis failed: {ex.Message}";
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private async Task CreateAsync()
    {
        if (Mode == ConversionMode.Monophonic)
            await _timeline.ConvertAudioClipToMidiAsync(_clip);
        else
            await _timeline.ConvertAudioClipToPolyMidiAsync(_clip);
        RequestClose?.Invoke();
    }
}
