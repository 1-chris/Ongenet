using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Ongenet.Audio.Interop;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native.Win;

/// <summary>ASIO device enumeration — lists drivers from the Windows ASIO registry.</summary>
[SupportedOSPlatform("windows")]
internal sealed class AsioDeviceService : IAudioDeviceService
{
    private readonly object _lock = new();
    private List<AudioDevice> _inputs = new();
    private List<AudioDevice> _outputs = new();
    private AudioDevice? _selectedInput;
    private AudioDevice? _selectedOutput;
    private AudioInputChannelMode _inputChannelMode = AudioInputChannelMode.Stereo;

    public AsioDeviceService()
    {
        try { Enumerate(); } catch { /* no ASIO → empty lists */ }
    }

    public bool DriverPresent => AsioNative.IsAvailable || _outputs.Count > 0;

    public IReadOnlyList<AudioDevice> InputDevices { get { lock (_lock) return _inputs; } }
    public IReadOnlyList<AudioDevice> OutputDevices { get { lock (_lock) return _outputs; } }

    public AudioDevice? SelectedOutput
    {
        get { lock (_lock) return _selectedOutput; }
        set { lock (_lock) { if (Equals(_selectedOutput, value)) return; _selectedOutput = value; } OutputChanged?.Invoke(); }
    }

    public AudioDevice? SelectedInput
    {
        get { lock (_lock) return _selectedInput; }
        set { lock (_lock) { if (Equals(_selectedInput, value)) return; _selectedInput = value; } InputChanged?.Invoke(); }
    }

    public AudioInputChannelMode InputChannelMode
    {
        get { lock (_lock) return _inputChannelMode; }
        set { lock (_lock) { if (_inputChannelMode == value) return; _inputChannelMode = value; } InputChanged?.Invoke(); }
    }

    public bool LowLatencyExclusive { get; set; } = true;

    public event Action? DevicesChanged;
    public event Action? OutputChanged;
    public event Action? InputChanged;

    public void Refresh()
    {
        try { Enumerate(); } catch { }
        DevicesChanged?.Invoke();
    }

    private void Enumerate()
    {
        lock (_lock)
        {
            _inputs = new List<AudioDevice>();
            _outputs = new List<AudioDevice>();
            _selectedInput = null;
            _selectedOutput = null;

            var drivers = AsioDriverEnumerator.Enumerate();
            var id = 0;
            foreach (var driver in drivers)
            {
                var device = new AudioDevice(id++, driver.Description, "ASIO", 0, 2, false, true,
                    $"asio:{driver.Name}");
                _outputs.Add(device);
                _inputs.Add(device);
            }

            if (_outputs.Count == 0 && AsioNative.IsAvailable)
            {
                var placeholder = new AudioDevice(0, "ASIO (no drivers in registry)", "ASIO", 0, 2, false, true,
                    "asio:default");
                _outputs.Add(placeholder);
            }

            _selectedOutput = _outputs.FirstOrDefault();
            _selectedInput = _inputs.FirstOrDefault();
        }
    }
}
