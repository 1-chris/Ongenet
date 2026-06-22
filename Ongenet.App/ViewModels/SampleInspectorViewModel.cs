using System;
using System.Globalization;
using Ongenet.Core.Audio;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels
{
    /// <summary>
    /// Bottom-panel inspector shown when an audio sample clip is selected. Surfaces the sample's
    /// natural tempo, the tempo-stretch toggle, and how much it is currently being stretched, and
    /// lets the user edit them — changes re-fit the clip to the grid and notify the timeline/engine.
    /// </summary>
    public class SampleInspectorViewModel : ViewModelBase
    {
        private readonly ISelectionService _selection;
        private readonly ITransportService _transport;
        private readonly IEventAggregator _events;

        public SampleInspectorViewModel(ISelectionService selection, ITransportService transport,
            IEventAggregator events)
        {
            _selection = selection;
            _transport = transport;
            _events = events;

            _selection.SelectionChanged += RaiseAll;
            _transport.TempoChanged += _ => RaiseAll();
            _events.Subscribe<ClipChangedEvent>(e =>
            {
                if (ReferenceEquals(e.Clip, Clip)) RaiseAll();
            });
        }

        // The currently selected clip, if it's an audio sample.
        private Clip? Clip => _selection.SelectedClip is { IsAudio: true } clip ? clip : null;

        /// <summary>True when an audio sample clip is selected (drives the Sample tab visibility).</summary>
        public bool HasSample => Clip is not null;

        /// <summary>The sample/clip name shown as the inspector header.</summary>
        public string SampleName => Clip?.Name ?? "No sample";

        /// <summary>The sample's natural tempo in BPM (0 = unknown). Editing it re-fits the clip if syncing.</summary>
        public double NaturalBpm
        {
            get => Clip?.SourceTempo ?? 0;
            set
            {
                if (Clip is not { } clip) return;
                clip.SourceTempo = value > 0 ? value : null;
                if (clip.StretchToTempo) Refit(clip);
                Publish(clip);
            }
        }

        /// <summary>Whether the sample is time-stretched to lock to the project tempo.</summary>
        public bool StretchEnabled
        {
            get => Clip?.StretchToTempo ?? false;
            set
            {
                if (Clip is not { } clip) return;
                if (value)
                {
                    // Need a natural tempo to stretch against; assume it's already in-tempo if unknown.
                    if (clip.SourceTempo is not { } t || t <= 0) clip.SourceTempo = _transport.Tempo.BeatsPerMinute;
                    clip.StretchToTempo = true;
                    Refit(clip);
                }
                else
                {
                    clip.StretchToTempo = false;
                    var duration = DurationSeconds(clip);
                    if (duration > 0) clip.LengthBeats = duration * _transport.Tempo.BeatsPerMinute / 60.0;
                }

                Publish(clip);
            }
        }

        /// <summary>Whether this stretched clip preserves pitch (time-stretch) instead of resampling.</summary>
        public bool PitchCorrected
        {
            get => Clip?.PitchCorrected ?? false;
            set
            {
                if (Clip is not { } clip) return;
                clip.PitchCorrected = value;
                Publish(clip); // engine rebuilds the clip's pitch shifters on the next play
            }
        }

        /// <summary>Whether the BPM / length readouts apply (only when a natural tempo is known).</summary>
        public bool HasTempo => Clip is { SourceTempo: > 0 };

        /// <summary>Human-readable stretch amount, e.g. "0.93× (93% speed)" or "Native (not stretched)".</summary>
        public string StretchInfo
        {
            get
            {
                if (Clip is not { } clip) return string.Empty;
                if (!clip.StretchToTempo) return "Native (not stretched)";
                var duration = DurationSeconds(clip);
                var factor = TempoSync.Stretch(duration, _transport.Tempo.BeatsPerMinute, clip.LengthBeats);
                var pct = factor * 100.0;
                var dir = factor > 1.0001 ? " — faster" : factor < 0.9999 ? " — slower" : "";
                return string.Create(CultureInfo.InvariantCulture, $"{factor:0.###}× ({pct:0}% speed){dir}");
            }
        }

        /// <summary>Clip length on the grid, e.g. "8 beats".</summary>
        public string LengthInfo
        {
            get
            {
                if (Clip is not { } clip) return string.Empty;
                return string.Create(CultureInfo.InvariantCulture, $"{clip.LengthBeats:0.##} beats");
            }
        }

        /// <summary>Source PCM info, e.g. "3.20 s · 44.1 kHz · stereo".</summary>
        public string SourceInfo
        {
            get
            {
                if (Clip?.Samples is not { } s) return string.Empty;
                var seconds = s.FrameCount / (double)s.SampleRate;
                var channels = s.Channels == 1 ? "mono" : s.Channels == 2 ? "stereo" : $"{s.Channels} ch";
                return string.Create(CultureInfo.InvariantCulture,
                    $"{seconds:0.00} s · {s.SampleRate / 1000.0:0.0} kHz · {channels}");
            }
        }

        private static double DurationSeconds(Clip clip)
            => clip.Samples is { } s ? s.FrameCount / (double)s.SampleRate : 0;

        // Recomputes the clip's grid length from its natural tempo at the current project tempo.
        private void Refit(Clip clip)
        {
            var duration = DurationSeconds(clip);
            if (duration <= 0 || clip.SourceTempo is not { } source || source <= 0) return;
            var beats = TempoSync.MusicalBeats(duration, source, _transport.Tempo.BeatsPerMinute);
            if (beats > 0) clip.LengthBeats = beats;
        }

        private void Publish(Clip clip)
        {
            _events.Publish(new ClipChangedEvent(clip)); // timeline resizes the clip; engine re-stretches on next play
            RaiseAll();
        }

        private void RaiseAll()
        {
            OnPropertyChanged(nameof(HasSample));
            OnPropertyChanged(nameof(SampleName));
            OnPropertyChanged(nameof(NaturalBpm));
            OnPropertyChanged(nameof(StretchEnabled));
            OnPropertyChanged(nameof(PitchCorrected));
            OnPropertyChanged(nameof(HasTempo));
            OnPropertyChanged(nameof(StretchInfo));
            OnPropertyChanged(nameof(LengthInfo));
            OnPropertyChanged(nameof(SourceInfo));
        }
    }
}
