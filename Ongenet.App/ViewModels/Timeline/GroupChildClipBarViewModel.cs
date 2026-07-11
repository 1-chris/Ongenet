using System.Collections.Generic;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;

namespace Ongenet.App.ViewModels.Timeline
{
    /// <summary>
    /// One child clip rendered as a coloured mini bar inside a <see cref="GroupClipSummaryViewModel"/>.
    /// </summary>
    public sealed class GroupChildClipBarViewModel : ViewModelBase
    {
        private readonly TimelineMetrics _metrics;
        private readonly double _envelopeStartBeat;
        private readonly double _rowStride;
        private readonly double _barHeight;

        public GroupChildClipBarViewModel(
            ClipViewModel source,
            string colorKey,
            int stackIndex,
            double envelopeStartBeat,
            double rowStride,
            double barHeight,
            TimelineMetrics metrics)
        {
            SourceClip = source;
            ColorKey = colorKey;
            StackIndex = stackIndex;
            _envelopeStartBeat = envelopeStartBeat;
            _rowStride = rowStride;
            _barHeight = barHeight;
            _metrics = metrics;
            _metrics.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(TimelineMetrics.PixelsPerBeat))
                {
                    OnPropertyChanged(nameof(Left));
                    OnPropertyChanged(nameof(Width));
                }
            };
        }

        public ClipViewModel SourceClip { get; }

        public string ColorKey { get; }

        /// <summary>Vertical stack slot (0 = top), one per descendant track lane.</summary>
        public int StackIndex { get; }

        /// <summary>Position inside the summary envelope, in pixels.</summary>
        public double Left => _metrics.BeatsToPixels(SourceClip.Model.StartBeat - _envelopeStartBeat);

        public double Width => _metrics.BeatsToPixels(SourceClip.Model.LengthBeats);

        /// <summary>Vertical offset inside the summary clip (px).</summary>
        public double Top => 4 + StackIndex * _rowStride;

        public double BarHeight => _barHeight;

        public bool IsAudio => SourceClip.IsAudio;
        public bool IsMidi => SourceClip.IsMidi;
        public AudioWaveform? Waveform => SourceClip.Waveform;
        public int WaveformRevision => SourceClip.WaveformRevision;
        public double WaveStartFraction => SourceClip.WaveStartFraction;
        public double WaveEndFraction => SourceClip.WaveEndFraction;
        public IReadOnlyList<MidiNote> Notes => SourceClip.Notes;
        public double ClipLengthBeats => SourceClip.ClipLengthBeats;
        public int NotesRevision => SourceClip.NotesRevision;
    }
}
