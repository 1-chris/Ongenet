using System;

namespace Ongenet.Core.Services;

/// <summary>Application-wide mastering delivery target shared by meters, effects, and export.</summary>
public interface IMasteringDeliveryTarget
{
    string PlatformName { get; set; }
    double TargetIntegratedLufs { get; set; }
    double TargetTruePeakDbTp { get; set; }
    event Action? Changed;
    void ApplyPlatform(string? name);
}
