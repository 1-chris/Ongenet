using System.Collections.Generic;
using Avalonia.Threading;
using Ongenet.Core.Audio;
using Ongenet.Desktop.Services;

namespace Ongenet.Desktop.ViewModels
{
    /// <summary>
    /// Backs the audio device pickers in the Settings window. Surfaces the machine's input/output
    /// devices from <see cref="IAudioDeviceService"/> and round-trips the user's selection back to it —
    /// which reopens the affected stream on the chosen device — and persists the choice.
    /// </summary>
    public class AudioDevicesViewModel : ViewModelBase
    {
        private readonly IAudioDeviceService _devices;
        private readonly IAppSettingsService? _settings;

        public AudioDevicesViewModel(IAudioDeviceService devices, IAppSettingsService? settings = null)
        {
            _devices = devices;
            _settings = settings;
            _devices.DevicesChanged += () => Dispatcher.UIThread.Post(RaiseLists);
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
