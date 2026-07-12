using System;
using System.Linq;
using System.Threading;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services.Implementation;

/// <summary>Plays arrangement marker sections in the user-defined playlist order.</summary>
public sealed class SectionPlaylistService : IDisposable
{
    private readonly IProjectService _project;
    private readonly ITransportService _transport;
    private readonly Timer _timer;
    private int _index;
    private bool _jumping;

    public SectionPlaylistService(IProjectService project, ITransportService transport)
    {
        _project = project;
        _transport = transport;
        _timer = new Timer(Poll, null, 50, 50);
        _transport.StateChanged += OnStateChanged;
    }

    public bool IsEnabled { get; set; }

    /// <summary>Zero-based index of the section currently playing in the playlist.</summary>
    public int CurrentIndex => _index;

    /// <summary>Raised when the active playlist section changes.</summary>
    public event Action? PlaylistPositionChanged;

    public void ResetIndex()
    {
        _index = 0;
        PlaylistPositionChanged?.Invoke();
    }

    private void OnStateChanged(TransportState state)
    {
        if (_jumping || !IsEnabled || state != TransportState.Playing) return;
        _index = 0;
        JumpToCurrent();
    }

    private void Poll(object? _)
    {
        if (!IsEnabled || _transport.State != TransportState.Playing) return;
        var project = _project.Current;
        if (_index < 0 || _index >= project.ArrangementSections.Count) return;
        var marker = FindMarker(project, project.ArrangementSections[_index].MarkerId);
        if (marker is null) return;
        var end = project.Markers.Where(m => m.Beat > marker.Beat).OrderBy(m => m.Beat)
            .Select(m => m.Beat).FirstOrDefault();
        if (end <= marker.Beat)
            end = project.BarCount * Math.Max(1, project.TimeSignature.Numerator);
        if (_transport.PlayheadBeats < end) return;

        _index++;
        if (_index >= project.ArrangementSections.Count)
        {
            _transport.Stop();
            PlaylistPositionChanged?.Invoke();
            return;
        }
        JumpToCurrent();
    }

    private void JumpToCurrent()
    {
        var project = _project.Current;
        if (_index >= project.ArrangementSections.Count) return;
        var marker = FindMarker(project, project.ArrangementSections[_index].MarkerId);
        if (marker is null) return;
        _jumping = true;
        try
        {
            var wasPlaying = _transport.State == TransportState.Playing;
            if (wasPlaying) _transport.Stop();
            _transport.StartBeat = marker.Beat;
            if (wasPlaying) _transport.Play();
            PlaylistPositionChanged?.Invoke();
        }
        finally
        {
            _jumping = false;
        }
    }

    private static ArrangementMarker? FindMarker(Project project, Guid id)
        => project.Markers.FirstOrDefault(m => m.Id == id);

    public void Dispose()
    {
        _transport.StateChanged -= OnStateChanged;
        _timer.Dispose();
    }
}
