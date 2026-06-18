using System;
using Ongenet.Core.Audio;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Desktop.ViewModels
{
    /// <summary>
    /// Top-bar transport: Play/Stop, tempo, arrangement length (bars), time readouts and the master
    /// loudness meter. Backed by <see cref="ITransportService"/> and the <see cref="IAudioEngine"/>.
    /// </summary>
    public class TransportViewModel : ViewModelBase
    {
        private readonly ITransportService _transport;
        private readonly IAudioEngine _engine;
        private readonly IProjectService _project;
        private readonly IEventAggregator _events;
        private readonly IEditModeService _editMode;
        private readonly OfflineRenderer _renderer;
        private readonly IRecordingService _recording;
        private bool _isRendering;

        public TransportViewModel(ITransportService transport, IAudioEngine engine,
            IProjectService project, IEventAggregator events, IEditModeService editMode,
            OfflineRenderer renderer, IRecordingService recording)
        {
            _transport = transport;
            _engine = engine;
            _project = project;
            _events = events;
            _editMode = editMode;
            _renderer = renderer;
            _recording = recording;

            _transport.StateChanged += _ => OnStateChanged();
            _transport.TempoChanged += _ => OnTempoChanged();
            _editMode.ModeChanged += () => OnPropertyChanged(nameof(IsSelectMode));
            // Recording state may flip from the audio thread (count-in finishing) — marshal to UI.
            _recording.StateChanged += () =>
                Avalonia.Threading.Dispatcher.UIThread.Post(OnRecordingStateChanged);

            PlayCommand = new RelayCommand(_transport.Play);
            StopCommand = new RelayCommand(OnStop);
            RecordCommand = new RelayCommand(_recording.StartRecording);
        }

        // Stop ends a recording session (committing its clips) or just stops the transport.
        private void OnStop()
        {
            if (_recording.IsRecording) _recording.StopRecording();
            else _transport.Stop();
        }

        /// <summary>Toggles between Edit and Select (rubber-band multi-select) mode.</summary>
        public bool IsSelectMode
        {
            get => _editMode.Mode == EditMode.Select;
            set => _editMode.Mode = value ? EditMode.Select : EditMode.Edit;
        }

        /// <summary>True while an export render is in progress (disables the Render button).</summary>
        public bool IsRendering
        {
            get => _isRendering;
            private set
            {
                if (SetField(ref _isRendering, value)) OnPropertyChanged(nameof(CanRender));
            }
        }

        public bool CanRender => !IsRendering;

        /// <summary>Renders the whole arrangement to a WAV file at <paramref name="path"/> (off the UI thread).</summary>
        public async System.Threading.Tasks.Task RenderToFileAsync(string path)
        {
            if (IsRendering) return;
            IsRendering = true;
            try
            {
                var format = _engine.Format;
                var bpm = _transport.Tempo.BeatsPerMinute;
                await System.Threading.Tasks.Task.Run(() => _renderer.RenderToWav(_project.Current, format, bpm, path));
            }
            finally
            {
                IsRendering = false;
            }
        }

        public RelayCommand PlayCommand { get; }
        public RelayCommand StopCommand { get; }
        public RelayCommand RecordCommand { get; }

        public bool IsPlaying => _transport.State == TransportState.Playing;
        public bool IsRecording => _recording.IsRecording;
        public bool CanPlay => !IsPlaying && !IsRecording;
        public bool CanStop => IsPlaying || IsRecording;
        public bool CanRecord => !IsPlaying && !IsRecording;

        /// <summary>Tempo in beats per minute; two-way bound to the BPM editor.</summary>
        public double Bpm
        {
            get => _transport.Tempo.BeatsPerMinute;
            set
            {
                if (value <= 0 || _transport.Tempo.BeatsPerMinute == value) return;
                _transport.Tempo = new Tempo(value);
                _project.Current.Tempo = new Tempo(value); // keep the project model in sync
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalTime));
            }
        }

        /// <summary>Arrangement length in bars; two-way bound to the Bars editor.</summary>
        public int Bars
        {
            get => _project.Current.BarCount;
            set
            {
                var clamped = value < 1 ? 1 : value;
                if (_project.Current.BarCount == clamped) return;
                _project.Current.BarCount = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalTime));
                _events.Publish(new ArrangementLengthChangedEvent());
            }
        }

        /// <summary>Time-signature numerator (beats per bar).</summary>
        public int TimeSigNumerator
        {
            get => _project.Current.TimeSignature.Numerator;
            set
            {
                if (value < 1 || value == TimeSigNumerator) return;
                _project.Current.TimeSignature = new TimeSignature(value, TimeSigDenominator);
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalTime));
                _events.Publish(new ArrangementLengthChangedEvent());
            }
        }

        /// <summary>Available time-signature denominators.</summary>
        public int[] Denominators { get; } = { 1, 2, 4, 8, 16 };

        /// <summary>Time-signature denominator (note value).</summary>
        public int TimeSigDenominator
        {
            get => _project.Current.TimeSignature.Denominator;
            set
            {
                if (value < 1 || value == TimeSigDenominator) return;
                _project.Current.TimeSignature = new TimeSignature(TimeSigNumerator, value);
                OnPropertyChanged();
                _events.Publish(new ArrangementLengthChangedEvent());
            }
        }

        /// <summary>Total arrangement time (m:ss.mmm).</summary>
        public string TotalTime => FormatTime(Bars * BeatsPerBar / _transport.Tempo.BeatsPerMinute * 60.0);

        /// <summary>Current playhead time (m:ss.mmm).</summary>
        public string PlayheadTime => FormatTime(_transport.PlayheadBeats / _transport.Tempo.BeatsPerMinute * 60.0);

        public double MasterLevelLeft => _engine.MasterLevelLeft;
        public double MasterLevelRight => _engine.MasterLevelRight;

        /// <summary>Refreshes the polled values (master meter + playhead time) — called on the view timer.</summary>
        public void RefreshMeters()
        {
            OnPropertyChanged(nameof(MasterLevelLeft));
            OnPropertyChanged(nameof(MasterLevelRight));
            OnPropertyChanged(nameof(PlayheadTime));
        }

        private int BeatsPerBar => Math.Max(1, _project.Current.TimeSignature.Numerator);

        private static string FormatTime(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) seconds = 0;
            var minutes = (int)(seconds / 60);
            var secs = (int)(seconds % 60);
            var millis = (int)((seconds - Math.Floor(seconds)) * 1000);
            return $"{minutes}:{secs:00}.{millis:000}";
        }

        private void OnStateChanged()
        {
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(CanPlay));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(CanRecord));
            OnPropertyChanged(nameof(PlayheadTime));
        }

        private void OnRecordingStateChanged()
        {
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(CanPlay));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(CanRecord));
        }

        private void OnTempoChanged()
        {
            OnPropertyChanged(nameof(Bpm));
            OnPropertyChanged(nameof(TotalTime));
            OnPropertyChanged(nameof(PlayheadTime));
        }
    }
}
