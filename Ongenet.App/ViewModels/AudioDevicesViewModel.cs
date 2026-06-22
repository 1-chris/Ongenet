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

        private void RaiseLists()
        {
            OnPropertyChanged(nameof(OutputDevices));
            OnPropertyChanged(nameof(InputDevices));
            OnPropertyChanged(nameof(SelectedOutput));
            OnPropertyChanged(nameof(SelectedInput));
        }
    }
}
