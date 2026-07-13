using System;

namespace Ongenet.App.ViewModels.VideoTimeline;

public sealed record VideoAudioSourceOption(Guid? Id, string Label);

public sealed record WaveformColorPreset(string Label, uint Argb);
