using System;

namespace Ongenet.Core.Services;

/// <summary>Mutable shared delivery target, initialized to the Spotify preset.</summary>
public sealed class MasteringDeliveryTarget : IMasteringDeliveryTarget
{
    private string _platformName = "Spotify";
    private double _targetIntegratedLufs = -14.0;
    private double _targetTruePeakDbTp = -1.0;

    public string PlatformName
    {
        get => _platformName;
        set
        {
            var name = string.IsNullOrWhiteSpace(value) ? "Custom" : value;
            if (string.Equals(_platformName, name, StringComparison.Ordinal)) return;
            _platformName = name;
            Changed?.Invoke();
        }
    }

    public double TargetIntegratedLufs
    {
        get => _targetIntegratedLufs;
        set
        {
            if (Math.Abs(_targetIntegratedLufs - value) < 1e-9) return;
            _targetIntegratedLufs = value;
            Changed?.Invoke();
        }
    }

    public double TargetTruePeakDbTp
    {
        get => _targetTruePeakDbTp;
        set
        {
            if (Math.Abs(_targetTruePeakDbTp - value) < 1e-9) return;
            _targetTruePeakDbTp = value;
            Changed?.Invoke();
        }
    }

    public event Action? Changed;

    public void ApplyPlatform(string? name)
    {
        var platform = string.IsNullOrWhiteSpace(name) ? "Custom" : name;
        var preset = DeliveryPlatformPresets.TryGet(platform);
        var changed = !string.Equals(_platformName, platform, StringComparison.Ordinal)
                      || preset is { } p
                      && (Math.Abs(_targetIntegratedLufs - p.Lufs) >= 1e-9
                          || Math.Abs(_targetTruePeakDbTp - p.DbTp) >= 1e-9);
        if (!changed) return;

        _platformName = platform;
        if (preset is { } target)
        {
            _targetIntegratedLufs = target.Lufs;
            _targetTruePeakDbTp = target.DbTp;
        }
        Changed?.Invoke();
    }
}
