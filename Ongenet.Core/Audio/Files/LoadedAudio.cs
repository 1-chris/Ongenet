namespace Ongenet.Core.Audio.Files;

/// <summary>The result of loading an audio file: decoded PCM for playback plus a peak summary for display.</summary>
public sealed record LoadedAudio(AudioSampleBuffer Samples, AudioWaveform Waveform);
