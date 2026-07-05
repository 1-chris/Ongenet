using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Instruments.Sampler;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Field.Nodes;

/// <summary>
/// Plays a loaded audio sample pitched by the incoming note (resampled around a root note), retriggered on
/// the gate rising edge. Hosts its own sample so it embeds in projects/presets like the Basic Sampler.
/// </summary>
public sealed class SamplePlayerNode : FieldNode, ISampleHost
{
    public const string Type = "smp.player";
    public override string TypeId => Type;
    public override string DisplayName => "Sample Player";
    public override string Category => FieldNodeCategories.Sampler;

    public double RootNote { get; set; } = 60;
    public double Gain { get; set; } = 1.0;
    public bool Loop { get; set; }

    private volatile AudioSampleBuffer? _sample;
    public string? SampleName { get; private set; }
    public AudioSampleBuffer? CurrentSample => _sample;
    public void LoadSample(AudioSampleBuffer sample, string name) { _sample = sample; SampleName = name; }

    private double[] _pos = Array.Empty<double>();
    private float[] _prevGate = Array.Empty<float>();

    public SamplePlayerNode()
    {
        AddInput("pitch", "Pitch", FieldSignalKind.Note);
        AddInput("gate", "Gate", FieldSignalKind.Note);
        AddOutput("l", "L");
        AddOutput("r", "R");
        AddParam(new FloatParameter("Root", 0, 127, () => RootNote, v => RootNote = Math.Round(v), "0"), modulatable: false);
        AddParam(new FloatParameter("Gain", 0, 2, () => Gain, v => Gain = v, "0.00"));
        AddParam(new BoolParameter("Loop", () => Loop, v => Loop = v));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _pos = new double[VoiceCount];
        _prevGate = new float[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) _pos[i] = double.MaxValue;
    }

    public override void ResetVoice(int voice)
    {
        if (voice >= _pos.Length) return;
        _pos[voice] = 0;
        _prevGate[voice] = 0;
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var pitch = ctx.Input(0);
        var gate = ctx.Input(1);
        var outL = ctx.Output(0);
        var outR = ctx.Output(1);
        var sample = _sample;

        var g0 = gate.Length > 0 ? gate[0] : 0f;
        if (_prevGate[v] <= 0.5f && g0 > 0.5f) _pos[v] = 0;
        _prevGate[v] = g0;

        if (sample is null)
        {
            Array.Clear(outL, 0, ctx.Frames);
            Array.Clear(outR, 0, ctx.Frames);
            return;
        }

        var frameCount = sample.FrameCount;
        var rootHz = MusicalMath.NoteToFrequency((int)RootNote);
        var srRatio = (double)sample.SampleRate / (Format.SampleRate <= 0 ? 44100 : Format.SampleRate);
        var gain = (float)Gain;
        var pos = _pos[v];
        for (var i = 0; i < ctx.Frames; i++)
        {
            if (pos >= frameCount)
            {
                if (Loop) pos = 0; else { outL[i] = 0; outR[i] = 0; continue; }
            }

            var f0 = (long)pos;
            var frac = (float)(pos - f0);
            var l0 = sample.Sample(f0, 0);
            var l1 = sample.Sample(f0 + 1, 0);
            var rc = sample.Channels > 1 ? 1 : 0;
            var r0 = sample.Sample(f0, rc);
            var r1 = sample.Sample(f0 + 1, rc);
            outL[i] = (l0 + (l1 - l0) * frac) * gain;
            outR[i] = (r0 + (r1 - r0) * frac) * gain;

            var ratio = rootHz > 0 ? pitch[i] / rootHz : 1.0;
            pos += srRatio * ratio;
        }

        _pos[v] = pos;
    }
}

/// <summary>
/// A mip-mapped wavetable oscillator and generic wavetable source: scans a morphing table at the note pitch
/// (alias-free), with built-in Basic/Harmonics/Random presets or a loaded sample sliced into a table
/// (<see cref="ISampleHost"/>). Exposes the live table for the on-graph 3D wavetable view
/// (<see cref="IWavetableView"/>). Changing the preset or loading a sample rebuilds the table live.
/// </summary>
public sealed class WavetableOscNode : FieldNode, ISampleHost, IWavetableView
{
    public const string Type = "osc.wavetable";
    public override string TypeId => Type;
    public override string DisplayName => "Wavetable Osc";
    public override string Category => FieldNodeCategories.Oscillators;
    public override bool HasVisual => true;

    public int PresetIndex { get; set; }
    public double Position { get; set; }
    public double Coarse { get; set; }
    public double Level { get; set; } = 1.0;

    private volatile Wavetable _table;
    private volatile AudioSampleBuffer? _sample;
    private int _revision;
    private float _displayPos;
    private double[] _phase = Array.Empty<double>();

    public WavetableOscNode()
    {
        _table = WavetableGenerator.BuildPreset(WavetablePreset.Basic);
        AddInput("pitch", "Pitch", FieldSignalKind.Note);
        AddOutput("out", "Out");
        AddParam(new ChoiceParameter("Table", new[] { "Basic", "Harmonics", "Random" },
            () => PresetIndex, i => { PresetIndex = i; RebuildTable(); }), modulatable: false);
        AddParam(new FloatParameter("Position", 0, 1, () => Position, v => Position = v, "0.00"));
        AddParam(new FloatParameter("Coarse", -48, 48, () => Coarse, v => Coarse = v, "0.#", "st"));
        AddParam(new FloatParameter("Level", 0, 1, () => Level, v => Level = v));
        Build();
    }

    // --- IWavetableView (on-graph 3D wavetable render) ---
    public Wavetable Table => _table;
    public int TableRevision => _revision;
    public float DisplayPosition => _displayPos;

    // --- ISampleHost (load a sample and slice it into a table) ---
    public string? SampleName { get; private set; }
    public AudioSampleBuffer? CurrentSample => _sample;

    public void LoadSample(AudioSampleBuffer sample, string name)
    {
        _sample = sample;
        SampleName = name;
        RebuildTable();
    }

    /// <summary>Rebuilds the table from the loaded sample if present, else the selected preset. Safe off the audio thread.</summary>
    public void RebuildTable()
    {
        _table = _sample is { } s
            ? WavetableGenerator.FromSample(s)
            : WavetableGenerator.BuildPreset((WavetablePreset)Math.Clamp(PresetIndex, 0, 2));
        _revision++;
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        RebuildTable();
        _phase = new double[VoiceCount];
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _phase.Length) _phase[voice] = 0;
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var table = _table;
        var v = ctx.Voice;
        var pitch = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate <= 0 ? 44100 : Format.SampleRate;
        var semi = Math.Pow(2.0, Coarse / 12.0);
        var phase = _phase[v];
        _displayPos = (float)ModValue(ctx, 1, Position, 0);
        for (var i = 0; i < ctx.Frames; i++)
        {
            var freq = pitch[i] * semi;
            var inc = (float)(freq / sr);
            outBuf[i] = table.Read((float)ModValue(ctx, 1, Position, i), (float)phase, inc) * (float)ModValue(ctx, 3, Level, i);
            phase += inc;
            if (phase >= 1.0) phase -= Math.Floor(phase);
        }

        _phase[v] = phase;
    }
}

/// <summary>
/// Loads a soundfont (SFZ/SF2) from disk and exposes it on an <b>Asset</b> output, so it can be patched into
/// a <see cref="SamplerNode"/>. Loading uses the shared sampler load service; the path (and SFZ source) is
/// persisted so the patch survives save/reload.
/// </summary>
public sealed class SoundFontNode : FieldNode, IFieldAssetProvider, IProjectStatefulComponent
{
    public const string Type = "smp.soundfont";
    public override string TypeId => Type;
    public override string DisplayName => "SoundFont";
    public override string Category => FieldNodeCategories.Sampler;

    private SamplerLoadResult? _result;

    public string SourcePath { get; private set; } = string.Empty;
    public int PresetIndex { get; private set; } = -1;
    public string Status => _result is { } r
        ? $"{r.DisplayName} — {r.Regions.Count} region(s)"
        : SourcePath.Length > 0 ? System.IO.Path.GetFileName(SourcePath) : "(no soundfont loaded)";

    public SoundFontNode()
    {
        AddOutput("sf", "SoundFont", FieldSignalKind.Asset);
        Build();
    }

    public object? GetAsset(string portId) => _result;

    public override void ProcessBlock(FieldRenderContext ctx) { /* asset-only node: no audio */ }

    /// <summary>Loads an <c>.sfz</c>/<c>.sf2</c> from disk (synchronous; call off the UI thread for big files).</summary>
    public bool LoadFromPath(string path, int presetIndex = -1)
    {
        var loader = SamplerInstrument.Loader;
        if (loader is null) return false;
        var result = loader.Load(path, presetIndex);
        if (result is null) return false;
        _result = result;
        SourcePath = path;
        PresetIndex = result.PresetIndex;
        return true;
    }

    public void WriteProjectState(OngenWriter writer)
    {
        writer.WriteString(SourcePath);
        writer.WriteInt(PresetIndex);
        writer.WriteString(_result?.SourceText ?? string.Empty);
    }

    public void ReadProjectState(OngenReader reader)
    {
        SourcePath = reader.ReadString();
        PresetIndex = reader.ReadInt();
        var text = reader.ReadString();
        var loader = SamplerInstrument.Loader;
        if (loader is null) return;
        _result = text.Length > 0 ? loader.LoadFromText(text, SourcePath) : (SourcePath.Length > 0 ? loader.Load(SourcePath, PresetIndex) : null);
    }
}

/// <summary>
/// A multi-sample sampler voice engine (SFZ/SF2) fed by a <see cref="SoundFontNode"/> on its <b>SoundFont</b>
/// asset inlet. It manages its own polyphony from the note stream (like the whole-instrument module wrapper)
/// and outputs stereo audio, so a soundfont can be combined with Field primitives and effects.
/// </summary>
public sealed class SamplerNode : FieldNode, IFieldNoteReceiver, IFieldAssetConsumer
{
    public const string Type = "smp.sampler";
    public override string TypeId => Type;
    public override string DisplayName => "Sampler";
    public override string Category => FieldNodeCategories.Sampler;
    public override bool ForceGlobal => true;

    private readonly SamplerInstrument _sampler = new();
    private float[] _temp = Array.Empty<float>();

    public SamplerNode()
    {
        AddInput("sf", "SoundFont", FieldSignalKind.Asset);
        AddOutput("l", "L");
        AddOutput("r", "R");
        foreach (var p in _sampler.Parameters) AddParam(p);
        Build();
    }

    public void SetAsset(string portId, object? asset)
    {
        if (portId == "sf" && asset is SamplerLoadResult result) _sampler.ApplyLoad(result);
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _sampler.Prepare(format);
        _temp = new float[maxBlock * Math.Max(1, format.Channels)];
    }

    public void NoteOn(int midiNote, float velocity) => _sampler.NoteOn(midiNote, velocity);
    public void NoteOff(int midiNote) => _sampler.NoteOff(midiNote);
    public void AllNotesOff() => _sampler.AllNotesOff();
    public void PitchBend(double semitones) => _sampler.PitchBend((int)Math.Clamp(8192 + semitones / 2.0 * 8192.0, 0, 16383));

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var channels = Math.Max(1, Format.Channels);
        var len = ctx.Frames * channels;
        Array.Clear(_temp, 0, len);
        _sampler.Render(_temp.AsSpan(0, len));
        var outL = ctx.Output(0);
        var outR = ctx.Output(1);
        for (var f = 0; f < ctx.Frames; f++)
        {
            var b = f * channels;
            outL[f] = _temp[b];
            outR[f] = channels > 1 ? _temp[b + 1] : _temp[b];
        }
    }
}
