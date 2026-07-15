using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Ongenet.Core.Audio;
using Ongenet.App.Services;

namespace Ongenet.App.ViewModels
{
    /// <summary>
    /// Backs the audio device pickers in the Settings window. Surfaces the machine's input/output
    /// devices from <see cref="IAudioDeviceService"/> and round-trips the user's selection back to it —
    /// which reopens the affected stream on the chosen device — and persists the choice. Also exposes the
    /// low-level audio backend selection via <see cref="IAudioBackendManager"/>.
    /// </summary>
    public class AudioDevicesViewModel : ViewModelBase
    {
        private readonly IAudioDeviceService _devices;
        private readonly IAudioBackendManager _backend;
        private readonly IAppSettingsService? _settings;

        public AudioDevicesViewModel(IAudioDeviceService devices, IAudioBackendManager backend,
            IAppSettingsService? settings = null)
        {
            _devices = devices;
            _backend = backend;
            _settings = settings;
            _devices.DevicesChanged += () => Dispatcher.UIThread.Post(RaiseLists);
            _backend.BackendChanged += () => Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(Backends));
                OnPropertyChanged(nameof(SelectedBackend));
                OnPropertyChanged(nameof(ShowWasapiExclusive));
                RaiseLists();
            });
        }

        /// <summary>The available low-level audio backends (only supported ones are selectable).</summary>
        public IReadOnlyList<AudioBackendInfo> Backends => _backend.Backends;

        /// <summary>The active backend; setting it switches live (stops, swaps, restarts the streams).</summary>
        public AudioBackendInfo? SelectedBackend
        {
            get => _backend.Backends.FirstOrDefault(b => b.Id == _backend.ActiveId);
            set
            {
                if (value is null || value.Id == _backend.ActiveId) return;
                _backend.Switch(value.Id); // AppSettingsService persists + re-applies devices on BackendChanged
                OnPropertyChanged();
            }
        }

        public IReadOnlyList<AudioDevice> OutputDevices => _devices.OutputDevices;
        public IReadOnlyList<AudioDevice> InputDevices => _devices.InputDevices;

        public AudioDevice? SelectedOutput
        {
            get => _devices.SelectedOutput;
            set
            {
                if (Equals(_devices.SelectedOutput, value)) return;
                _devices.SelectedOutput = value;
                OnPropertyChanged();
            }
        }

        public AudioDevice? SelectedInput
        {
            get => _devices.SelectedInput;
            set
            {
                if (Equals(_devices.SelectedInput, value)) return;
                _devices.SelectedInput = value;
                OnPropertyChanged();
            }
        }

        /// <summary>The capture mode options shown in the Mono/Stereo switch.</summary>
        public AudioInputChannelMode[] InputChannelModes { get; } =
            { AudioInputChannelMode.Stereo, AudioInputChannelMode.Mono };

        /// <summary>Whether the input is captured as stereo (as-is) or mono (centered).</summary>
        public AudioInputChannelMode InputChannelMode
        {
            get => _devices.InputChannelMode;
            set
            {
                if (_devices.InputChannelMode == value) return;
                _devices.InputChannelMode = value;
                OnPropertyChanged();
                _settings?.CaptureAndSave(); // device changes persist via events; mode has no event

            }
        }

        /// <summary>When true, WASAPI uses exclusive output mode for lower latency (Windows only).</summary>
        public bool WasapiExclusiveMode
        {
            get => _settings?.Current.WasapiExclusiveMode ?? _devices.LowLatencyExclusive;
            set
            {
                if (_devices.LowLatencyExclusive == value &&
                    (_settings is null || _settings.Current.WasapiExclusiveMode == value))
                    return;
                _devices.LowLatencyExclusive = value;
                if (_settings is not null)
                    _settings.Current.WasapiExclusiveMode = value;
                OnPropertyChanged();
                _settings?.CaptureAndSave();
            }
        }

        public bool ShowWasapiExclusive => SelectedBackend?.Id is "wasapi" or "win";

        /// <summary>True on macOS where the CoreAudio producer lead is relevant.</summary>
        public bool ShowCoreAudioLead =>
            OperatingSystem.IsMacOS();

        public int[] CoreAudioLeadOptions { get; } = { 2048, 4096 };

        /// <summary>
        /// CoreAudio producer lead frames. Applied on next audio engine restart.
        /// </summary>
        public int CoreAudioLeadFrames
        {
            get => _settings?.Current.CoreAudioLeadFrames is 4096 ? 4096 : 2048;
            set
            {
                var frames = value is 4096 ? 4096 : 2048;
                if (_settings is null) return;
                if (_settings.Current.CoreAudioLeadFrames == frames
                    && AudioRuntimeOptions.CoreAudioLeadFrames == frames)
                    return;
                _settings.Current.CoreAudioLeadFrames = frames;
                AudioRuntimeOptions.CoreAudioLeadFrames = frames;
                OnPropertyChanged();
                _settings.CaptureAndSave();
            }
        }

        public bool MidiClockEnabled
        {
            get => _settings?.Current.MidiClockEnabled ?? false;
            set
            {
                if (_settings is null || _settings.Current.MidiClockEnabled == value) return;
                _settings.Current.MidiClockEnabled = value;
                OnPropertyChanged();
                _settings.CaptureAndSave();
            }
        }

        private void RaiseLists()
        {
            OnPropertyChanged(nameof(OutputDevices));
            OnPropertyChanged(nameof(InputDevices));
            OnPropertyChanged(nameof(SelectedOutput));
            OnPropertyChanged(nameof(SelectedInput));
        }
    }
}
