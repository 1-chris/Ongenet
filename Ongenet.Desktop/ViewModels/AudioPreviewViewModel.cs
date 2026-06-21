using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Desktop.Services;

namespace Ongenet.Desktop.ViewModels;

/// <summary>
/// Shared audio preview shown at the bottom of the library sidebar (used by the Files and Samples tabs):
/// renders the selected file's waveform and BPM / length / key, and auditions it through the output
/// device. Selecting a new file immediately replaces the sounding one; a stop button and an auto-play
/// toggle (persisted) control playback. Loading + key analysis run off the UI thread.
/// </summary>
public sealed class AudioPreviewViewModel : ViewModelBase
{
    private readonly IAudioFileService _audioFiles;
    private readonly IAuditionPlayer _audition;
    private readonly IAppSettingsService _settings;

    private string? _path;
    private AudioSampleBuffer? _buffer;
    private bool _isPlaying;

    public AudioPreviewViewModel(IAudioFileService audioFiles, IAuditionPlayer audition,
        IAppSettingsService settings, IPlaybackClock clock)
    {
        _audioFiles = audioFiles;
        _audition = audition;
        _settings = settings;

        PlayCommand = new RelayCommand(PlayCurrent);
        StopCommand = new RelayCommand(() => _audition.Stop());

        // Reflect the audition player's state on the UI heartbeat (cheap; only flips on change).
        clock.Tick += () =>
        {
            if (_isPlaying == _audition.IsPlaying) return;
            _isPlaying = _audition.IsPlaying;
            OnPropertyChanged(nameof(IsPlaying));
        };
    }

    public RelayCommand PlayCommand { get; }
    public RelayCommand StopCommand { get; }

    public bool HasSelection => _path is not null;
    public string FileName => _path is null ? string.Empty : Path.GetFileName(_path);

    public AudioWaveform? Waveform { get; private set; }
    public int WaveRevision { get; private set; }

    public string Bpm { get; private set; } = "—";
    public string Length { get; private set; } = "—";
    public string Key { get; private set; } = "—";

    public bool IsPlaying => _isPlaying;

    public bool AutoPlay
    {
        get => _settings.Current.LibraryAutoPlay;
        set
        {
            if (_settings.Current.LibraryAutoPlay == value) return;
            _settings.Current.LibraryAutoPlay = value;
            _settings.SaveLibrary();
            OnPropertyChanged();
        }
    }

    /// <summary>Selects a file to preview (no-op for the file already shown). Auditions it if auto-play is on.</summary>
    public void Select(string path)
    {
        if (string.IsNullOrEmpty(path) || string.Equals(path, _path, StringComparison.Ordinal)) return;
        if (!_audioFiles.IsAudioFile(path)) return;

        _path = path;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(FileName));
        SetStats("…", "…", "…");
        _ = LoadAsync(path);
    }

    private async Task LoadAsync(string path)
    {
        LoadedAudio? loaded = null;
        string key = "—";
        try
        {
            await Task.Run(() =>
            {
                loaded = _audioFiles.Load(path);
                if (loaded is not null) key = MusicalKeyDetector.Detect(loaded.Samples) is { Length: > 0 } k ? k : "—";
            });
        }
        catch { /* ignore decode errors — show no stats */ }

        if (!string.Equals(path, _path, StringComparison.Ordinal)) return; // selection moved on

        if (loaded is null)
        {
            _buffer = null;
            Waveform = null; WaveRevision++;
            OnPropertyChanged(nameof(Waveform)); OnPropertyChanged(nameof(WaveRevision));
            SetStats("—", "—", "—");
            return;
        }

        _buffer = loaded.Samples;
        Waveform = loaded.Waveform;
        WaveRevision++;
        OnPropertyChanged(nameof(Waveform));
        OnPropertyChanged(nameof(WaveRevision));

        var seconds = loaded.Samples.SampleRate > 0 ? loaded.Samples.FrameCount / (double)loaded.Samples.SampleRate : 0;
        SetStats(loaded.Tempo is { } bpm ? $"{bpm:0.#} BPM" : "—", FormatLength(seconds), key);

        if (AutoPlay) _audition.Play(loaded.Samples);
    }

    private void PlayCurrent()
    {
        if (_buffer is { } buf) _audition.Play(buf);
    }

    private void SetStats(string bpm, string length, string key)
    {
        Bpm = bpm; Length = length; Key = key;
        OnPropertyChanged(nameof(Bpm));
        OnPropertyChanged(nameof(Length));
        OnPropertyChanged(nameof(Key));
    }

    private static string FormatLength(double seconds)
    {
        if (seconds <= 0) return "—";
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalMinutes >= 1 ? $"{(int)ts.TotalMinutes}:{ts.Seconds:00}" : $"{seconds:0.00}s";
    }
}
