using System;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Implementation;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Scripting;
using Ongenet.Scripting.Export;
using Xunit;

namespace Ongenet.Core.Tests.Services;

public sealed class ScriptingApiTests
{
    private readonly CaptureHistory _history = new();
    private readonly EventAggregator _events = new();
    private readonly InstrumentRegistry _instruments = new();
    private readonly EffectRegistry _effects = new();
    private readonly ProjectService _project;
    private readonly TransportService _transport;
    private readonly ProjectScriptExporter _projectExporter = new();
    private readonly PresetScriptExporter _presetExporter = new();
    private readonly ExportService _export = new(new NullVideoCompositor(), new NullVideoMuxer());
    private readonly ScriptingApi _api;

    public ScriptingApiTests()
    {
        _project = new ProjectService(_instruments);
        _transport = new TransportService();
        _api = new ScriptingApi(_project, _transport, _history, _events, _instruments, _effects, _projectExporter, _presetExporter, _export, new FakeAudioEngine());
    }

    [Fact]
    public void SetMasterMeterTap_UpdatesEngineTap()
    {
        _api.SetMasterMeterTap(ScriptMasterMeterTap.PreLimiter);
        Assert.Equal(ScriptMasterMeterTap.PreLimiter, _api.GetMasterMeterTap());
        _api.SetMasterMeterTap(ScriptMasterMeterTap.PostChain);
        Assert.Equal(ScriptMasterMeterTap.PostChain, _api.GetMasterMeterTap());
    }

    [Fact]
    public void SetTempo_UpdatesProjectAndTransport()
    {
        _api.SetTempo(128);
        Assert.Equal(128, _project.Current.Tempo.BeatsPerMinute, 3);
        Assert.Equal(128, _transport.Tempo.BeatsPerMinute, 3);
        Assert.Contains("Change tempo", _history.Labels);
    }

    [Fact]
    public void SetTimeSignature_UpdatesProject()
    {
        _api.SetTimeSignature(3, 4);
        Assert.Equal(3, _project.Current.TimeSignature.Numerator);
        Assert.Equal(4, _project.Current.TimeSignature.Denominator);
        Assert.Contains("Change time signature", _history.Labels);
    }

    [Fact]
    public void SetBarCount_PublishesArrangementEvent()
    {
        var fired = false;
        _events.Subscribe<ArrangementLengthChangedEvent>(_ => fired = true);
        _api.SetBarCount(32);
        Assert.Equal(32, _project.Current.BarCount);
        Assert.True(fired);
    }

    [Fact]
    public void AddInstrumentTrack_InsertsTrack()
    {
        var id = _api.AddInstrumentTrack("Script Track");
        var track = _project.Current.Tracks.First(t => t.Id == id);
        Assert.Equal("Script Track", track.Name);
        Assert.Equal(TrackKind.Instrument, track.Kind);
        Assert.Contains("Add instrument track", _history.Labels);
    }

    [Fact]
    public void CreateMidiClip_AddsClipToTrack()
    {
        var trackId = _api.AddInstrumentTrack("Notes");
        var clipId = _api.CreateMidiClip(trackId, "Part", 0, 4);
        var clip = _project.Current.Tracks.SelectMany(t => t.Clips).First(c => c.Id == clipId);
        Assert.Equal("Part", clip.Name);
        Assert.False(clip.IsAudio);
        Assert.Equal(4, clip.LengthBeats);
    }

    [Fact]
    public void DuplicateClip_CreatesCopyWithOffset()
    {
        var trackId = _api.AddInstrumentTrack("Dup");
        var clipId = _api.CreateMidiClip(trackId, "A", 0, 4);
        var copyId = _api.DuplicateClip(clipId);
        var clips = _api.GetClips(trackId);
        Assert.Equal(2, clips.Count);
        var copy = clips.First(c => c.Id == copyId);
        Assert.Equal(4, copy.StartBeat, 3);
    }

    [Fact]
    public void TransposeAllMidiClips_ChangesNotes()
    {
        var trackId = _api.AddInstrumentTrack("Transpose");
        var clipId = _api.CreateMidiClip(trackId, "Notes", 0, 4);
        var track = _project.Current.Tracks.First(t => t.Id == trackId);
        track.Clips.First(c => c.Id == clipId).Notes.Add(new MidiNote { Note = 60, StartBeat = 0, LengthBeats = 1 });
        _api.TransposeAllMidiClips(2);
        Assert.Equal(62, track.Clips[0].Notes[0].Note);
    }

    [Fact]
    public void Log_AppendsOutput()
    {
        _api.Log("hello");
        _api.Log("world");
        Assert.Equal(2, _api.OutputLines.Count);
        Assert.Equal("hello", _api.OutputLines[0]);
    }

    [Fact]
    public void SetLoopRegion_UpdatesTransport()
    {
        _api.SetLoopRegion(0, 8);
        Assert.Equal(0, _transport.LoopStart, 3);
        Assert.Equal(8, _transport.LoopEnd, 3);
    }

    [Fact]
    public void ClearProject_ResetsTracks()
    {
        var before = _api.GetTracks().Count;
        Assert.True(before > 0);
        _api.ClearProject();
        Assert.Empty(_api.GetTracks());
    }

    [Fact]
    public void SetTrackMuted_UpdatesTrack()
    {
        var id = _api.AddInstrumentTrack("Mute Me");
        _api.SetTrackMuted(id, true);
        Assert.True(_api.GetTrack(id)!.IsMuted);
    }

    [Fact]
    public void AddMidiNotes_RoundTrips()
    {
        var trackId = _api.AddInstrumentTrack("Notes");
        var clipId = _api.CreateMidiClip(trackId, "Part", 0, 4);
        _api.AddMidiNote(clipId, new ScriptMidiNote(64, 0, 1, 0.8f));
        var notes = _api.GetMidiNotes(clipId);
        Assert.Single(notes);
        Assert.Equal(64, notes[0].Note);
    }

    [Fact]
    public void ExportProjectAsScript_ReturnsRunnableHeader()
    {
        var script = _api.ExportProjectAsScript();
        Assert.Contains("api.ClearProject()", script);
        Assert.Contains("Generated by Ongenet", script);
    }

    [Fact]
    public void AddMarker_AppearsInGetMarkers()
    {
        var id = Guid.NewGuid();
        _api.AddMarker(new ScriptMarkerInfo(id, "Intro", 0));
        Assert.Contains(_api.GetMarkers(), m => m.Id == id);
    }

    [Fact]
    public void ApplyMasteringChain_ReplacesMasterWithNamedTypeOrder()
    {
        var master = _project.Current.Tracks.Single(t => t.Kind == TrackKind.Master);

        _api.ApplyMasteringChain(master.Id, "full");
        Assert.Equal(new[]
        {
            EqEffect.TypeId, MidSideEqEffect.TypeId, CompressorEffect.TypeId,
            StereoWidthEffect.TypeId, ClipperEffect.TypeId, PeakLimiterEffect.TypeId, SpectrumEffect.TypeId
        }, master.Effects.Select(e => e.TypeId));

        _api.ApplyMasteringChain(master.Id, "techno");
        Assert.Equal(new[]
        {
            FilterEffect.TypeId, MultibandCompressorEffect.TypeId,
            StereoWidthEffect.TypeId, ExciterEffect.TypeId, PeakLimiterEffect.TypeId
        }, master.Effects.Select(e => e.TypeId));
    }

    [Theory]
    [InlineData("full", EqEffect.TypeId, SpectrumEffect.TypeId)]
    [InlineData("full+", EqEffect.TypeId, SpectrumEffect.TypeId)]
    [InlineData("streaming", EqEffect.TypeId, SpectrumEffect.TypeId)]
    [InlineData("premaster", DcOffsetEffect.TypeId, CompressorEffect.TypeId)]
    [InlineData("club", MultibandCompressorEffect.TypeId, PeakLimiterEffect.TypeId)]
    [InlineData("podcast", EqEffect.TypeId, PeakLimiterEffect.TypeId)]
    [InlineData("glue", CompressorEffect.TypeId, PeakLimiterEffect.TypeId)]
    [InlineData("techno", FilterEffect.TypeId, PeakLimiterEffect.TypeId)]
    [InlineData("audiophile", LinearPhaseEqEffect.TypeId, SpectrumEffect.TypeId)]
    [InlineData("audiophile master", LinearPhaseEqEffect.TypeId, SpectrumEffect.TypeId)]
    [InlineData("reference", EqEffect.TypeId, SpectrumEffect.TypeId)]
    [InlineData("reference master", EqEffect.TypeId, SpectrumEffect.TypeId)]
    public void ApplyMasteringChain_AllNames_ProduceExpectedFirstAndLast(string chainName, string firstTypeId, string lastTypeId)
    {
        var master = _project.Current.Tracks.Single(t => t.Kind == TrackKind.Master);
        _api.ApplyMasteringChain(master.Id, chainName);
        Assert.NotEmpty(master.Effects);
        Assert.Equal(firstTypeId, master.Effects[0].TypeId);
        Assert.Equal(lastTypeId, master.Effects[^1].TypeId);
    }

    [Fact]
    public void ApplyMasteringChain_NonMasterTrack_Throws()
    {
        var trackId = _api.AddInstrumentTrack("Not Master");
        Assert.Throws<InvalidOperationException>(() => _api.ApplyMasteringChain(trackId, "full"));
    }

    [Fact]
    public void ExportAudio_WritesTempWav()
    {
        var format = new AudioFormat(48000, 2);
        var frames = format.SampleRate;
        var samples = new float[frames * 2];
        for (var f = 0; f < frames; f++)
        {
            var s = 0.2f * MathF.Sin(2 * MathF.PI * 440f * f / format.SampleRate);
            samples[f * 2] = s;
            samples[f * 2 + 1] = s;
        }

        var trackId = _api.AddInstrumentTrack("Tone");
        var track = _project.Current.Tracks.First(t => t.Id == trackId);
        track.Kind = TrackKind.Audio;
        track.Clips.Add(new Clip
        {
            Name = "Tone",
            IsAudio = true,
            StartBeat = 0,
            LengthBeats = 2,
            Samples = new AudioSampleBuffer(samples, 2, format.SampleRate)
        });

        var path = Path.Combine(Path.GetTempPath(), "ongenet-script-export-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            _api.ExportAudio(path, analyzeLoudness: false);
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 44);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class CaptureHistory : IHistoryCapture
    {
        public System.Collections.Generic.List<string> Labels { get; } = new();
        public void Capture(string label) => Labels.Add(label);
    }

    private sealed class FakeAudioEngine : IAudioEngine
    {
        public bool IsRunning { get; set; }
        public AudioFormat Format { get; } = new(48000, 2);
        public float MasterLevelLeft { get; set; }
        public float MasterLevelRight { get; set; }
        public float MasterTruePeakLeftDbTp { get; set; }
        public float MasterTruePeakRightDbTp { get; set; }
        public float MasterTruePeakMaxDbTp { get; set; }
        public float MasterMomentaryLufs { get; set; }
        public float MasterShortTermLufs { get; set; }
        public float MasterIntegratedLufs { get; set; }
        public float MasterLoudnessRangeLu { get; set; }
        public float MasterCorrelation { get; set; }
        public MasterMeterTap MasterMeterTap { get; set; } = MasterMeterTap.PostFader;
        public void ResetMasterLoudness() { }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Dispose() { }
    }
}
