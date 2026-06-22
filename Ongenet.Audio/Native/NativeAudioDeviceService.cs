using System;
using System.Collections.Generic;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native;

/// <summary>
/// <see cref="IAudioDeviceService"/> for the native Linux backend. Aggregates devices from every
/// available subsystem driver into one list (each already tagged with its host API), and holds the
/// user's selection. The output/input streams read the current selection from here, so changing a
/// device reopens its stream.
/// </summary>
internal sealed class NativeAudioDeviceService : IAudioDeviceService
{
    private readonly object _lock = new();
    private readonly NativeDriverRegistry _drivers;
    private List<AudioDevice> _inputs = new();
    private List<AudioDevice> _outputs = new();
    private AudioDevice? _selectedInput;
    private AudioDevice? _selectedOutput;
    private AudioInputChannelMode _inputChannelMode = AudioInputChannelMode.Stereo;

    public NativeAudioDeviceService(NativeDriverRegistry drivers)
    {
        _drivers = drivers;
        try { Enumerate(); } catch { /* no usable subsystem → empty lists, app stays alive */ }
    }

    public IReadOnlyList<AudioDevice> InputDevices { get { lock (_lock) return _inputs; } }
    public IReadOnlyList<AudioDevice> OutputDevices { get { lock (_lock) return _outputs; } }

    public AudioDevice? SelectedOutput
    {
        get { lock (_lock) return _selectedOutput; }
        set
        {
            lock (_lock)
            {
                if (Equals(_selectedOutput, value)) return;
                _selectedOutput = value;
            }

            OutputChanged?.Invoke();
        }
    }

    public AudioDevice? SelectedInput
    {
        get { lock (_lock) return _selectedInput; }
        set
        {
            lock (_lock)
            {
                if (Equals(_selectedInput, value)) return;
                _selectedInput = value;
            }

            InputChanged?.Invoke();
        }
    }

    public AudioInputChannelMode InputChannelMode
    {
        get { lock (_lock) return _inputChannelMode; }
        set
        {
            lock (_lock)
            {
                if (_inputChannelMode == value) return;
                _inputChannelMode = value;
            }

            InputChanged?.Invoke();
        }
    }

    public event Action? DevicesChanged;
    public event Action? OutputChanged;
    public event Action? InputChanged;

    public void Refresh()
    {
        try { Enumerate(); } catch { /* keep prior lists */ }
        DevicesChanged?.Invoke();
    }

    private void Enumerate()
    {
        var inputs = new List<AudioDevice>();
        var outputs = new List<AudioDevice>();
        foreach (var driver in _drivers.Available)
        {
            try { driver.Enumerate(outputs, inputs); }
            catch { /* one bad driver shouldn't sink the rest */ }
        }

        lock (_lock)
        {
            _outputs = outputs;
            _inputs = inputs;
            _selectedOutput = Reconcile(_selectedOutput, outputs);
            _selectedInput = Reconcile(_selectedInput, inputs);
        }
    }

    // Keeps a prior selection if it still exists; else the default device, else the first available.
    private static AudioDevice? Reconcile(AudioDevice? current, List<AudioDevice> devices)
    {
        if (current is not null)
        {
            foreach (var d in devices)
                if (d.Id == current.Id && d.Name == current.Name) return d;
        }

        foreach (var d in devices)
            if (d.IsDefaultOutput || d.IsDefaultInput) return d;

        return devices.Count > 0 ? devices[0] : null;
    }
}
