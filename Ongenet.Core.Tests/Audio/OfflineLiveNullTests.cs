using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Implementation;
using Ongenet.Core.Services.Interfaces;
using Xunit;

namespace Ongenet.Core.Tests.Audio;

/// <summary>Null-test: offline render vs live engine output should match within tolerance.</summary>
public sealed class OfflineLiveNullTests
{
    private const int SampleRate = 44100;
    private const int Channels = 2;
    private const int BlockFrames = 512;
    private const double Bpm = 120;
    private static readonly AudioFormat Format = new(SampleRate, Channels);

    [Fact]
    public void OfflineAndLiveEngine_ProduceMatchingOutput_ForOscillatorClip()
    {
        const double beats = 2;
        var project = BuildProject();

        var offlinePath = Path.Combine(Path.GetTempPath(), $"ongenet-null-{Guid.NewGuid():N}.wav");
        try
        {
            new OfflineRenderer().RenderToWav(project, Format, Bpm, offlinePath, progress: null, bitDepth: 32,
                regionStartBeat: 0, regionEndBeat: beats);

            using var offlineStream = File.OpenRead(offlinePath);
            var offline = WavParser.Parse(offlineStream);

            var output = new CapturingAudioOutput(Format);
            var instruments = new InstrumentRegistry();
            var projectSvc = new ProjectService(instruments);
            projectSvc.SetCurrentProject(project);
            var transport = new TransportService();
            var playback = new PlaybackModeService(projectSvc, transport);
            var events = new EventAggregator();
            var engine = new AudioEngine(output, projectSvc, transport, playback, events, new AuditionPlayer());

            engine.Start();
            transport.StartBeat = 0;
            transport.Play();

            var frames = (int)Math.Ceiling(beats * SampleRate * 60.0 / Bpm);
            output.Pump(frames);
            engine.Stop();

            var live = output.Samples.ToArray();
            var compareFrames = (int)Math.Min(offline.FrameCount, live.Length / Channels);
            var sampleCount = compareFrames * Channels;
            var diffRms = DiffRms(offline.Samples, live, sampleCount);
            var refRms = Rms(offline.Samples, sampleCount);
            Assert.True(refRms > 1e-4, "Reference render should be audible.");
            Assert.True(diffRms / refRms < 0.08, $"Null residual too high: {diffRms / refRms:P1}");
        }
        finally
        {
            if (File.Exists(offlinePath)) File.Delete(offlinePath);
        }
    }

    private static Project BuildProject()
    {
        var project = new Project { Tempo = new Tempo(Bpm) };
        project.Tracks.Add(new Track { Name = "Master", Kind = TrackKind.Master });
        var track = new Track { Name = "Synth", Kind = TrackKind.Instrument };
        track.Instruments.Add(new InstrumentSlot(new OscillatorInstrument { Waveform = Waveform.Sine })
            { Enabled = true });
        track.CommitInstruments();
        track.Clips.Add(new Clip
        {
            Name = "Note",
            IsAudio = false,
            StartBeat = 0,
            LengthBeats = 2,
            Notes = { new MidiNote { Note = 60, StartBeat = 0, LengthBeats = 2, Velocity = 0.8f } }
        });
        project.Tracks.Add(track);
        return project;
    }

    private static double Rms(IReadOnlyList<float> samples, int count)
    {
        var sum = 0.0;
        for (var i = 0; i < count; i++) sum += samples[i] * samples[i];
        return Math.Sqrt(sum / count);
    }

    private static double DiffRms(IReadOnlyList<float> a, IReadOnlyList<float> b, int count)
    {
        var sum = 0.0;
        for (var i = 0; i < count; i++)
        {
            var d = a[i] - b[i];
            sum += d * d;
        }

        return Math.Sqrt(sum / count);
    }

    private sealed class CapturingAudioOutput : IAudioOutput
    {
        public CapturingAudioOutput(AudioFormat format) => Format = format;
        public AudioFormat Format { get; }
        public bool IsRunning { get; private set; }
        public event Action? FormatChanged;
        public List<float> Samples { get; } = new();
        private AudioRenderCallback? _callback;

        public void Start(AudioRenderCallback callback)
        {
            _callback = callback;
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
            _callback = null;
        }

        public void Pump(int totalFrames)
        {
            var block = new float[BlockFrames * Format.Channels];
            var written = 0;
            while (written < totalFrames)
            {
                var frames = Math.Min(BlockFrames, totalFrames - written);
                var span = block.AsSpan(0, frames * Format.Channels);
                span.Clear();
                _callback?.Invoke(span);
                foreach (var s in span) Samples.Add(s);
                written += frames;
            }
        }

        public void Dispose() => Stop();
    }
}
