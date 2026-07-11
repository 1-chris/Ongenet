using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Tests.Audio;

public class ClipRenderTests
{
    private static readonly AudioFormat Format = new(44100, 2);
    private const double Bpm = 120.0;

    private static Project NewProject()
    {
        var project = new Project { Tempo = new Tempo(Bpm) };
        project.Tracks.Add(new Track { Name = "Master", Kind = TrackKind.Master });
        return project;
    }

    private static AudioSampleBuffer ToneBuffer(int frames, int channels = 2, double hz = 440.0)
    {
        var data = new float[frames * channels];
        for (var f = 0; f < frames; f++)
        {
            var s = (float)Math.Sin(2 * Math.PI * hz * f / Format.SampleRate);
            for (var c = 0; c < channels; c++) data[f * channels + c] = s;
        }

        return new AudioSampleBuffer(data, channels, Format.SampleRate);
    }

    private static double Rms(AudioSampleBuffer buffer)
    {
        var sum = 0.0;
        foreach (var s in buffer.Samples) sum += s * s;
        return Math.Sqrt(sum / buffer.Samples.Length);
    }

    private static int ExpectedFrames(double beats, double bpm = Bpm)
        => (int)Math.Ceiling(beats * Format.SampleRate * 60.0 / bpm);

    [Fact]
    public void AudioClipRender_LengthMatchesBeatSpan()
    {
        var project = NewProject();
        var track = new Track { Name = "Audio", Kind = TrackKind.Audio };
        const double lengthBeats = 4.0;
        var clip = new Clip
        {
            Name = "Tone",
            IsAudio = true,
            StartBeat = 0,
            LengthBeats = lengthBeats,
            Samples = ToneBuffer(ExpectedFrames(lengthBeats) * 2)
        };
        track.Clips.Add(clip);
        project.Tracks.Add(track);

        var scope = ClipRenderScope.ForClip(project, track, clip);
        var renderer = new OfflineRenderer();
        var rendered = renderer.RenderScopeToBuffer(project, Format, Bpm, scope);

        Assert.Equal(ExpectedFrames(lengthBeats), rendered.FrameCount);
    }

    [Fact]
    public void MidiClipThroughLowPass_IsQuieterThanDry()
    {
        var project = NewProject();
        var track = new Track { Name = "Synth", Kind = TrackKind.Instrument };
        track.Instruments.Add(new InstrumentSlot(new OscillatorInstrument { Waveform = Waveform.Sawtooth }) { Enabled = true });
        track.CommitInstruments();

        var clip = new Clip
        {
            Name = "Note",
            IsAudio = false,
            StartBeat = 0,
            LengthBeats = 2,
            Notes = { new MidiNote { Note = 60, StartBeat = 0, LengthBeats = 2, Velocity = 0.8f } }
        };
        track.Clips.Add(clip);
        project.Tracks.Add(track);

        var dryScope = ClipRenderScope.ForClip(project, track, clip);
        var dry = new OfflineRenderer().RenderScopeToBuffer(project, Format, Bpm, dryScope);

        track.Effects.Add(new FilterEffect
        {
            Mode = FilterMode.LowPass,
            Frequency = 200,
            Resonance = 0.5
        });
        track.CommitEffects();

        var wetScope = ClipRenderScope.ForClip(project, track, clip);
        var wet = new OfflineRenderer().RenderScopeToBuffer(project, Format, Bpm, wetScope);

        Assert.True(Rms(wet) < Rms(dry) * 0.85);
    }

    [Fact]
    public void GroupBusFx_AffectsRenderedClip()
    {
        var project = NewProject();
        var group = new Track { Name = "Group", Kind = TrackKind.Group };
        var track = new Track { Name = "Synth", Kind = TrackKind.Instrument, ParentId = group.Id };
        track.Instruments.Add(new InstrumentSlot(new OscillatorInstrument()) { Enabled = true });
        track.CommitInstruments();

        var clip = new Clip
        {
            Name = "Note",
            IsAudio = false,
            StartBeat = 0,
            LengthBeats = 2,
            Notes = { new MidiNote { Note = 72, StartBeat = 0, LengthBeats = 2, Velocity = 0.8f } }
        };
        track.Clips.Add(clip);
        project.Tracks.Add(group);
        project.Tracks.Add(track);

        var dry = new OfflineRenderer().RenderScopeToBuffer(project, Format, Bpm,
            ClipRenderScope.ForClip(project, track, clip));

        group.Effects.Add(new FilterEffect { Mode = FilterMode.LowPass, Frequency = 150 });
        group.CommitEffects();

        var wet = new OfflineRenderer().RenderScopeToBuffer(project, Format, Bpm,
            ClipRenderScope.ForClip(project, track, clip));

        Assert.True(Rms(wet) < Rms(dry) * 0.85);
    }

    [Fact]
    public void GroupSummary_RendersDescendantMix()
    {
        var project = NewProject();
        var group = new Track { Name = "Group", Kind = TrackKind.Group };
        var a = new Track { Name = "A", Kind = TrackKind.Instrument, ParentId = group.Id };
        var b = new Track { Name = "B", Kind = TrackKind.Instrument, ParentId = group.Id };
        foreach (var t in new[] { a, b })
        {
            t.Instruments.Add(new InstrumentSlot(new OscillatorInstrument()) { Enabled = true });
            t.CommitInstruments();
        }

        a.Clips.Add(new Clip
        {
            Name = "A",
            StartBeat = 0,
            LengthBeats = 2,
            Notes = { new MidiNote { Note = 60, StartBeat = 0, LengthBeats = 2, Velocity = 0.7f } }
        });
        b.Clips.Add(new Clip
        {
            Name = "B",
            StartBeat = 0,
            LengthBeats = 2,
            Notes = { new MidiNote { Note = 72, StartBeat = 0, LengthBeats = 2, Velocity = 0.7f } }
        });

        project.Tracks.Add(group);
        project.Tracks.Add(a);
        project.Tracks.Add(b);

        var scope = ClipRenderScope.ForGroup(project, group, 0, 2, new[] { a, b });
        var rendered = new OfflineRenderer().RenderScopeToBuffer(project, Format, Bpm, scope);

        Assert.True(Rms(rendered) > 0.01);
        Assert.Equal(ExpectedFrames(2), rendered.FrameCount);
    }

    [Fact]
    public void VolumeAutomation_ReducesOutput()
    {
        var project = NewProject();
        var track = new Track { Name = "Synth", Kind = TrackKind.Instrument, Volume = 0.9 };
        track.Instruments.Add(new InstrumentSlot(new OscillatorInstrument()) { Enabled = true });
        track.CommitInstruments();

        var clip = new Clip
        {
            Name = "Note",
            IsAudio = false,
            StartBeat = 0,
            LengthBeats = 2,
            Notes = { new MidiNote { Note = 60, StartBeat = 0, LengthBeats = 2, Velocity = 0.8f } }
        };
        track.Clips.Add(clip);

        var lane = new AutomationLane(
            new DelegateAutomationTarget("Volume", 0, 1, () => track.Volume, v => track.Volume = v))
        {
            Binding = new AutomationBinding(AutomationTargetKind.TrackVolume, -1, -1)
        };
        lane.Points.Add(new AutomationPoint(0, 0));
        lane.Points.Add(new AutomationPoint(2, 0));
        track.AutoLanes.Add(lane);
        track.CommitAutoLanes();
        project.Tracks.Add(track);

        var automated = new OfflineRenderer().RenderScopeToBuffer(project, Format, Bpm,
            ClipRenderScope.ForClip(project, track, clip));

        track.AutoLanes.Clear();
        track.CommitAutoLanes();
        track.Volume = 0.9;
        var full = new OfflineRenderer().RenderScopeToBuffer(project, Format, Bpm,
            ClipRenderScope.ForClip(project, track, clip));

        Assert.True(Rms(automated) < Rms(full) * 0.1);
    }

    [Fact]
    public void SidechainSource_IsIncludedInRender()
    {
        var project = NewProject();
        var kick = new Track { Name = "Kick", Kind = TrackKind.Instrument };
        kick.Instruments.Add(new InstrumentSlot(new OscillatorInstrument { Waveform = Waveform.Sine }) { Enabled = true });
        kick.CommitInstruments();
        kick.Clips.Add(new Clip
        {
            Name = "Kick",
            StartBeat = 0,
            LengthBeats = 2,
            Notes = { new MidiNote { Note = 36, StartBeat = 0, LengthBeats = 0.25, Velocity = 1f } }
        });

        var bass = new Track { Name = "Bass", Kind = TrackKind.Instrument };
        bass.Instruments.Add(new InstrumentSlot(new OscillatorInstrument()) { Enabled = true });
        bass.CommitInstruments();
        bass.Effects.Add(new SidechainEffect { SourceTrackId = kick.Id, Amount = 0.9, AttackMs = 1, ReleaseMs = 80 });
        bass.CommitEffects();
        var bassClip = new Clip
        {
            Name = "Bass",
            StartBeat = 0,
            LengthBeats = 2,
            Notes = { new MidiNote { Note = 48, StartBeat = 0, LengthBeats = 2, Velocity = 0.8f } }
        };
        bass.Clips.Add(bassClip);

        project.Tracks.Add(kick);
        project.Tracks.Add(bass);

        var sidechained = new OfflineRenderer().RenderScopeToBuffer(project, Format, Bpm,
            ClipRenderScope.ForClip(project, bass, bassClip));

        bass.Effects.Clear();
        bass.CommitEffects();
        var dry = new OfflineRenderer().RenderScopeToBuffer(project, Format, Bpm,
            ClipRenderScope.ForClip(project, bass, bassClip));

        Assert.NotEqual(Rms(dry), Rms(sidechained));
    }
}
