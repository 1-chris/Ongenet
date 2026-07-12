using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Services;

namespace Ongenet.App.ViewModels.Panels;

/// <summary>Video track panel — syncs video frames to the transport playhead via ffmpeg.</summary>
public sealed class VideoTrackViewModel : ViewModelBase
{
    private readonly IProjectService _project;
    private readonly ITransportService _transport;
    private VideoTrack? _selected;
    private Bitmap? _frame;
    private string _status = string.Empty;
    private double _lastFrameTime = double.NaN;

    public VideoTrackViewModel(IProjectService project, ITransportService transport, IPlaybackClock clock)
    {
        _project = project;
        _transport = transport;
        AddTrackCommand = new RelayCommand(AddTrack);
        BrowseCommand = new RelayCommand(() => _ = BrowseAsync(), () => Selected is not null);
        _project.ProjectChanged += Rebuild;
        _transport.StartBeatChanged += OnTransportScrub;
        _transport.StateChanged += _ => OnTransportScrub();
        clock.Tick += OnTick;
        Rebuild();
    }

    public ObservableCollection<VideoTrack> Tracks { get; } = new();

    public bool IsFfmpegAvailable => FfmpegVideoFrameExtractor.IsAvailable;

    public VideoTrack? Selected
    {
        get => _selected;
        set
        {
            if (!SetField(ref _selected, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(OffsetSeconds));
            OnPropertyChanged(nameof(InPointSeconds));
            OnPropertyChanged(nameof(OutPointSeconds));
            OnPropertyChanged(nameof(Fps));
            OnPropertyChanged(nameof(IsMuted));
            _lastFrameTime = double.NaN;
            BrowseCommand.RaiseCanExecuteChanged();
            RefreshFrame(force: true);
        }
    }

    public bool HasSelection => Selected is not null;

    public string FilePath
    {
        get => Selected?.FilePath ?? string.Empty;
        set
        {
            if (Selected is null || Selected.FilePath == value) return;
            Selected.FilePath = value;
            OnPropertyChanged();
            _lastFrameTime = double.NaN;
            RefreshFrame(force: true);
        }
    }

    public double OffsetSeconds
    {
        get => Selected?.OffsetSeconds ?? 0;
        set
        {
            if (Selected is null || Selected.OffsetSeconds == value) return;
            Selected.OffsetSeconds = value;
            OnPropertyChanged();
            _lastFrameTime = double.NaN;
            RefreshFrame(force: true);
        }
    }

    public double InPointSeconds
    {
        get => Selected?.InPointSeconds ?? 0;
        set
        {
            if (Selected is null || Math.Abs(Selected.InPointSeconds - value) < 1e-6) return;
            Selected.InPointSeconds = Math.Max(0, value);
            OnPropertyChanged();
            _lastFrameTime = double.NaN;
            RefreshFrame(force: true);
        }
    }

    public double OutPointSeconds
    {
        get => Selected?.OutPointSeconds ?? 0;
        set
        {
            if (Selected is null || Math.Abs(Selected.OutPointSeconds - value) < 1e-6) return;
            Selected.OutPointSeconds = Math.Max(0, value);
            OnPropertyChanged();
            _lastFrameTime = double.NaN;
            RefreshFrame(force: true);
        }
    }

    public double Fps
    {
        get => Selected?.Fps ?? 24;
        set
        {
            if (Selected is null || Selected.Fps == value) return;
            Selected.Fps = Math.Max(1, value);
            OnPropertyChanged();
        }
    }

    public bool IsMuted
    {
        get => Selected?.Muted ?? false;
        set
        {
            if (Selected is null || Selected.Muted == value) return;
            Selected.Muted = value;
            OnPropertyChanged();
        }
    }

    public Bitmap? Frame
    {
        get => _frame;
        private set => SetField(ref _frame, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public double SyncTimeSeconds
    {
        get
        {
            if (Selected is null) return 0;
            var bpm = _transport.Tempo.BeatsPerMinute;
            var beats = _transport.PlayheadBeats;
            var raw = Selected.OffsetSeconds + beats * 60.0 / Math.Max(bpm, 1);
            var inPt = Selected.InPointSeconds;
            var outPt = Selected.OutPointSeconds;
            if (outPt > inPt && raw > outPt)
                raw = outPt;
            return Math.Max(inPt, raw);
        }
    }

    public RelayCommand AddTrackCommand { get; }
    public RelayCommand BrowseCommand { get; }

    public Func<Task<string?>>? PickVideoPathAsync { get; set; }

    private void Rebuild()
    {
        Tracks.Clear();
        foreach (var vt in _project.Current.VideoTracks)
            Tracks.Add(vt);
        Selected = Tracks.FirstOrDefault();
        OnPropertyChanged(nameof(IsFfmpegAvailable));
    }

    private void AddTrack()
    {
        var vt = new VideoTrack();
        _project.Current.VideoTracks.Add(vt);
        Tracks.Add(vt);
        Selected = vt;
    }

    private async Task BrowseAsync()
    {
        if (Selected is null || PickVideoPathAsync is null) return;
        var path = await PickVideoPathAsync();
        if (!string.IsNullOrWhiteSpace(path))
            FilePath = path;
    }

    private void OnTransportScrub() => RefreshFrame(force: true);

    private void OnTick() => RefreshFrame(force: false);

    private void RefreshFrame(bool force)
    {
        if (Selected is null || Selected.Muted || string.IsNullOrWhiteSpace(Selected.FilePath))
        {
            Frame = null;
            Status = Selected is null ? "Add a video track." : "No video file loaded.";
            return;
        }

        if (!File.Exists(Selected.FilePath))
        {
            Frame = null;
            Status = "Video file not found.";
            return;
        }

        var t = SyncTimeSeconds;

        if (!IsFfmpegAvailable)
        {
            Frame = null;
            var name = Path.GetFileName(Selected.FilePath);
            Status = _transport.State == TransportState.Playing
                ? $"ffmpeg not installed — tracking {name} @ {t:F2}s (no preview)."
                : $"ffmpeg not installed — scrub to {t:F2}s in {name} (no preview). Install ffmpeg to see frames.";
            OnPropertyChanged(nameof(SyncTimeSeconds));
            return;
        }

        if (!force && Math.Abs(t - _lastFrameTime) < 1.0 / Math.Max(Selected.Fps, 1))
            return;

        _lastFrameTime = t;
        var png = FfmpegVideoFrameExtractor.ExtractFramePng(Selected.FilePath, Math.Max(0, t));
        if (png is null)
        {
            Status = "Failed to extract frame.";
            return;
        }

        using var ms = new MemoryStream(png);
        Frame = new Bitmap(ms);
        Status = _transport.State == TransportState.Playing
            ? $"Synced @ {t:F2}s"
            : $"Scrub preview @ {t:F2}s";
        OnPropertyChanged(nameof(SyncTimeSeconds));
    }
}
