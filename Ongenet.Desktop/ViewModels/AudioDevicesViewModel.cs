using System.Collections.Generic;
using Avalonia.Threading;
using Ongenet.Core.Audio;

namespace Ongenet.Desktop.ViewModels
{
    /// <summary>
    /// Backs the top-bar audio device pickers. Surfaces the machine's input/output devices from
    /// <see cref="IAudioDeviceService"/> and round-trips the user's selection back to it — which
    /// reopens the affected stream on the chosen device.
    /// </summary>
    public class AudioDevicesViewModel : ViewModelBase
    {
        private readonly IAudioDeviceService _devices;

        public AudioDevicesViewModel(IAudioDeviceService devices)
        {
            _devices = devices;
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
