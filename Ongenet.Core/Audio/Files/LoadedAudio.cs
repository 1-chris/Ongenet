namespace Ongenet.Core.Audio.Files;

/// <summary>
/// The result of loading an audio file: decoded PCM for playback, a peak summary for display, and
/// the sample's natural tempo if known. <see cref="Tempo"/> comes from the file/folder name when
/// <see cref="TempoFromName"/> is true, otherwise it is an estimate (or null if undetermined).
/// </summary>
public sealed record LoadedAudio(
    AudioSampleBuffer Samples,
    AudioWaveform Waveform,
    double? Tempo = null,
    bool TempoFromName = false);
